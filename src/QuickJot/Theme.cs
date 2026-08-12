using System.Windows;
using System.Windows.Media;

namespace QuickJot;

/// <summary>
/// Палитра приложения — раздел 15. Одна на оба окна: поверхности задаются прозрачностью поверх Mica,
/// а не своими цветами. Сплошная заливка убила бы фон окна, а фиксированные серые не совпали бы
/// ни со светлой, ни с тёмной темой.
/// </summary>
internal static class Theme
{
    public static bool IsDark(string? setting) => setting switch
    {
        "dark" => true,
        "light" => false,
        _ => IsSystemDark(), // системная — раздел 13
    };

    public static void Apply(FrameworkElement target, string? setting)
    {
        var accent = SystemParameters.WindowGlassColor;
        bool dark = IsDark(setting);

        // Тон поверхностей: в тёмной теме подмешивается белый, в светлой — чёрный.
        byte tone = dark ? (byte)0xFF : (byte)0x00;
        var resources = target.Resources;

        resources["TextBrush"] = Solid(dark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1B, 0x1B, 0x1B));
        resources["MutedBrush"] = Tinted(dark ? 0x8A : 0x99, tone);

        resources["CardBrush"] = Tinted(dark ? 0x0F : 0x0A, tone);
        resources["CardHoverBrush"] = Tinted(dark ? 0x1C : 0x14, tone);
        resources["CardBorderBrush"] = Tinted(dark ? 0x14 : 0x12, tone);
        resources["NoteSurfaceBrush"] = Tinted(dark ? 0x14 : 0x0D, tone);
        resources["DividerBrush"] = Tinted(dark ? 0x1F : 0x1A, tone);

        // Корешок карточки: заметен ровно настолько, чтобы его нашли, и не спорит с текстом.
        resources["SpineBrush"] = Tinted(dark ? 0x24 : 0x1F, tone);
        resources["SpineHoverBrush"] = Tinted(dark ? 0x5A : 0x4A, tone);

        // Шпаргалка накрывает окно целиком и должна быть непрозрачной: сквозь любую полупрозрачную
        // подложку текст карточек продолжает читаться и мешает. Тост лежит поверх карточек — тоже плотный.
        resources["ScrimBrush"] = Solid(dark ? Color.FromRgb(0x1F, 0x1F, 0x1F) : Color.FromRgb(0xFA, 0xFA, 0xFA));
        resources["ToastBrush"] = Solid(dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFF, 0xFF, 0xFF));

        resources["AccentBrush"] = Solid(dark ? Lighten(accent, 0.35) : accent);
        resources["AccentSoftBrush"] = Solid(Color.FromArgb(0x1F, accent.R, accent.G, accent.B));
        resources["AccentRingBrush"] = Solid(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
        resources["AccentHighlightBrush"] = Solid(Color.FromArgb(0x3D, accent.R, accent.G, accent.B));

        resources["DangerBrush"] = Solid(dark ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0xD1, 0x0F, 0x1F));

        target.SetValue(System.Windows.Controls.Control.ForegroundProperty, resources["TextBrush"]);
    }

    private static SolidColorBrush Solid(Color color) => new(color);

    private static SolidColorBrush Tinted(int alpha, byte tone) => new(Color.FromArgb((byte)alpha, tone, tone, tone));

    /// <summary>Системный акцент на тёмном фоне часто слишком глухой — подмешиваем белого.</summary>
    private static Color Lighten(Color color, double amount) => Color.FromRgb(
        (byte)(color.R + (255 - color.R) * amount),
        (byte)(color.G + (255 - color.G) * amount),
        (byte)(color.B + (255 - color.B) * amount));

    /// <summary>Осветлить цвет — нужно и акценту, и чипам тегов в тёмной теме.</summary>
    public static Color Lightened(Color color, double amount) => Lighten(color, amount);

    /// <summary>Затемнить: в светлой теме текст чипа берётся отсюда.</summary>
    public static Color Darkened(Color color, double amount) => Color.FromRgb(
        (byte)(color.R * (1 - amount)),
        (byte)(color.G * (1 - amount)),
        (byte)(color.B * (1 - amount)));

    private static bool IsSystemDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }
}
