using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickJot;

internal static class Native
{
    public const int HWND_MESSAGE = -3;
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_MAINWINDOW = 2; // Mica

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    /// <summary>Mica и скругление углов. AllowsTransparency не используется — раздел 5.</summary>
    public static void ApplyMicaAndRoundCorners(IntPtr hwnd)
    {
        int backdrop = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }
}

/// <summary>
/// Глобальный хоткей на message-only окне — раздел 4. Регистрация живёт отдельно от главного окна,
/// поэтому переживает его скрытие и пересоздание.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int Id = 1;
    private readonly HwndSource _source;

    public event Action? Pressed;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("QuickJot.Hotkey")
        {
            ParentWindow = Native.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(Hook);
    }

    /// <summary>false = комбинация занята другой программой. Вызывающий обязан это показать — раздел 4.</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        Native.UnregisterHotKey(_source.Handle, Id);
        return Native.RegisterHotKey(_source.Handle, Id, modifiers | Native.MOD_NOREPEAT, virtualKey);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY) return IntPtr.Zero;
        handled = true;
        Pressed?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Native.UnregisterHotKey(_source.Handle, Id);
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}

/// <summary>
/// Показ окна с кареткой в поле ввода и возврат фокуса при скрытии — раздел 16.
/// Порядок действий проверен спайком этапа 0 и менять его нельзя:
/// без AttachThreadInput каретка не встаёт, а фокус чужому окну надо отдавать до Hide().
/// </summary>
internal sealed class WindowActivator
{
    private IntPtr _previousForeground;

    public void ShowAndFocus(Window window, IInputElement focusTarget)
    {
        _previousForeground = Native.GetForegroundWindow();
        var hwnd = new WindowInteropHelper(window).Handle;

        uint foreignThread = _previousForeground == IntPtr.Zero
            ? 0
            : Native.GetWindowThreadProcessId(_previousForeground, out _);
        uint ownThread = Native.GetCurrentThreadId();
        bool attached = foreignThread != 0 && foreignThread != ownThread
                        && Native.AttachThreadInput(ownThread, foreignThread, true);

        try
        {
            window.Show();
            window.Activate();
            Native.SetForegroundWindow(hwnd);
            Keyboard.Focus(focusTarget);
        }
        finally
        {
            if (attached) Native.AttachThreadInput(ownThread, foreignThread, false);
        }
    }

    public void HideAndRestore(Window window)
    {
        // Фокус отдаётся ДО Hide(): после него процесс уже не активен и SetForegroundWindow
        // вернёт true, ничего не сделав.
        if (_previousForeground != IntPtr.Zero && Native.IsWindow(_previousForeground))
            Native.SetForegroundWindow(_previousForeground);

        window.Hide();
    }

    public bool IsForeground(Window window) =>
        Native.GetForegroundWindow() == new WindowInteropHelper(window).Handle;
}
