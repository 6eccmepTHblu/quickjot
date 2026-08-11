using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace QuickJot;

/// <summary>Угол привязки окна — раздел 5.</summary>
public enum AnchorCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Позиционирование окна — разделы 5 и 16. Все расчёты идут в физических пикселях и через SetWindowPos:
/// Left/Top у WPF живут в своих единицах, и на связке «ноутбук + внешний монитор» с разным масштабом
/// окно уезжает мимо экрана.
/// </summary>
internal static class Placement
{
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>Рабочая область монитора под курсором в физических пикселях и его масштаб.</summary>
    private static (RECT Work, double Scale) MonitorUnderCursor()
    {
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);

        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        return (info.Work, scale);
    }

    /// <summary>Высота рабочей области того монитора в единицах WPF — из неё считается потолок окна.</summary>
    public static double WorkAreaHeight()
    {
        var (work, scale) = MonitorUnderCursor();
        return (work.Bottom - work.Top) / scale;
    }

    /// <summary>
    /// Ставит окно в заданный угол рабочей области монитора, на котором сейчас курсор.
    /// Привязка именно к WorkArea, а не к границам экрана: иначе окно лезет под панель задач.
    /// </summary>
    public static void Place(Window window, AnchorCorner corner, double margin)
    {
        var (work, scale) = MonitorUnderCursor();
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Размер берётся уже посчитанный WPF: SizeToContent мог изменить высоту прямо перед показом.
        window.UpdateLayout();
        if (!GetWindowRect(hwnd, out var rect)) return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        int gap = (int)Math.Round(margin * scale);

        int x = corner is AnchorCorner.TopLeft or AnchorCorner.BottomLeft
            ? work.Left + gap
            : work.Right - width - gap;

        int y = corner is AnchorCorner.TopLeft or AnchorCorner.TopRight
            ? work.Top + gap
            : work.Bottom - height - gap;

        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
