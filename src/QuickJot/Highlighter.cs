using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuickJot;

/// <summary>
/// Подсветка совпадений в заголовке — раздел 7. Простой привязкой это не делается:
/// у TextBlock нужно собирать Inlines, поэтому текст и запрос приходят вложенными свойствами.
/// </summary>
public static class Highlighter
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Highlighter), new PropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty QueryProperty = DependencyProperty.RegisterAttached(
        "Query", typeof(string), typeof(Highlighter), new PropertyMetadata(null, Rebuild));

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetQuery(DependencyObject element, string? value) => element.SetValue(QueryProperty, value);
    public static string? GetQuery(DependencyObject element) => (string?)element.GetValue(QueryProperty);

    private static void Rebuild(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;

        var text = GetText(element) ?? "";
        var query = GetQuery(element)?.Trim() ?? "";

        block.Inlines.Clear();

        if (query.Length == 0)
        {
            block.Inlines.Add(new Run(text));
            return;
        }

        var accent = block.TryFindResource("AccentBrush") as Brush ?? block.Foreground;

        int position = 0;
        while (position <= text.Length)
        {
            int match = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                block.Inlines.Add(new Run(text[position..]));
                return;
            }

            if (match > position) block.Inlines.Add(new Run(text[position..match]));

            block.Inlines.Add(new Run(text.Substring(match, query.Length))
            {
                Foreground = accent,
                FontWeight = FontWeights.SemiBold,
            });

            position = match + query.Length;
        }
    }
}
