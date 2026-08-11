using System.Windows.Input;

namespace QuickJot;

/// <summary>
/// Комбинация глобального хоткея в человеческом виде и в виде, который понимает RegisterHotKey.
/// Хранится строкой в settings — раздел 12.
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static Hotkey Default => new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);

    /// <summary>Комбинация без модификатора отобрала бы клавишу у всей системы.</summary>
    public bool IsValid => Modifiers != ModifierKeys.None && Key is not (Key.None or Key.System)
                           && !IsModifierKey(Key);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? Native.MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Control) ? Native.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? Native.MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? Native.MOD_WIN : 0);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin;

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());

        return string.Join("+", parts);
    }

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Default;

        var modifiers = ModifierKeys.None;
        var key = Key.None;

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed)) key = parsed;
                    break;
            }
        }

        var hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid ? hotkey : Default;
    }
}
