using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickJot;

/// <summary>Пустая строка — нет сообщения об ошибке, и место под него занимать не надо.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
