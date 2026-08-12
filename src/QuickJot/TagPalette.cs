using System.Windows.Media;

namespace QuickJot;

/// <summary>
/// Цвета чипов — раздел 8. Восемь оттенков: больше на глаз в списке всё равно не различаются.
/// Цвет берётся из имени тега, поэтому один и тот же тег всегда одного цвета и без всякой настройки;
/// выбранный вручную цвет живёт в настройках и просто перебивает вычисленный.
/// </summary>
internal static class TagPalette
{
    private static readonly Color[] Hues =
    [
        Color.FromRgb(0x42, 0x85, 0xF4), // синий
        Color.FromRgb(0xA7, 0x6E, 0xF0), // фиолетовый
        Color.FromRgb(0x00, 0xA6, 0x99), // бирюзовый
        Color.FromRgb(0x4C, 0xAF, 0x50), // зелёный
        Color.FromRgb(0xE6, 0xA0, 0x1E), // янтарный
        Color.FromRgb(0xE9, 0x5A, 0x5A), // коралловый
        Color.FromRgb(0xDB, 0x5A, 0xA0), // розовый
        Color.FromRgb(0x78, 0x8C, 0xAA), // серо-синий
    ];

    public static int Count => Hues.Length;

    public static Color Hue(int index) => Hues[((index % Count) + Count) % Count];

    /// <summary>
    /// Во что превращается сохранённый выбор: номер оттенка, свой цвет «#RRGGBB» или,
    /// если не выбрано ничего, оттенок, посчитанный из имени.
    /// </summary>
    public static Color Resolve(string? chosen, string tag)
    {
        if (string.IsNullOrWhiteSpace(chosen)) return Hue(IndexOf(tag));
        if (int.TryParse(chosen, out int index)) return Hue(index);

        try { return (Color)ColorConverter.ConvertFromString(chosen); }
        catch { return Hue(IndexOf(tag)); } // испорченное значение не должно ронять список
    }

    /// <summary>Цвет в том виде, в каком он ложится в настройки.</summary>
    public static string ToStored(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// Свой хеш, а не string.GetHashCode: тот в .NET рандомизируется при каждом запуске,
    /// и цвет тега менялся бы от запуска к запуску.
    /// </summary>
    public static int IndexOf(string tag)
    {
        int hash = 17;
        foreach (var symbol in tag) hash = unchecked(hash * 31 + symbol);

        return ((hash % Count) + Count) % Count;
    }

    /// <summary>Подложка чипа: тот же оттенок, но прозрачный, чтобы не спорить с Mica.</summary>
    public static SolidColorBrush Background(Color hue, bool dark) =>
        new(Color.FromArgb(dark ? (byte)0x33 : (byte)0x28, hue.R, hue.G, hue.B));

    /// <summary>Текст чипа: на тёмном фоне оттенок осветляется, на светлом — затемняется.</summary>
    public static SolidColorBrush Foreground(Color hue, bool dark) =>
        new(dark ? Theme.Lightened(hue, 0.35) : Theme.Darkened(hue, 0.35));
}
