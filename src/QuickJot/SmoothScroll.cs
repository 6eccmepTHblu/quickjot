using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace QuickJot;

/// <summary>
/// Плавная прокрутка колесом — раздел 15. Смещение у <see cref="ScrollViewer"/> только для чтения,
/// анимировать его напрямую нечем. Поэтому анимируется приложенное свойство, а оно уже двигает
/// сам ScrollViewer из обработчика изменения.
/// </summary>
public static class SmoothScroll
{
    private static readonly Duration Slide = new(TimeSpan.FromMilliseconds(220));

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset",
        typeof(double),
        typeof(SmoothScroll),
        new PropertyMetadata(0.0, (target, args) => ((ScrollViewer)target).ScrollToVerticalOffset((double)args.NewValue)));

    /// <summary>
    /// Куда прокрутка едет прямо сейчас. Без этого щелчки колеса не складывались бы: каждый
    /// начинал бы разгон с того места, куда доехал предыдущий, и список полз бы медленнее руки.
    /// </summary>
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double?), typeof(SmoothScroll));

    public static void By(ScrollViewer scroll, double delta)
    {
        double from = (double?)scroll.GetValue(TargetProperty) ?? scroll.VerticalOffset;
        double to = Math.Clamp(from + delta, 0, scroll.ScrollableHeight);
        scroll.SetValue(TargetProperty, to);

        // Базовое значение подтягивается к текущему смещению: прокрутить могли и мимо колеса.
        scroll.SetValue(OffsetProperty, scroll.VerticalOffset);

        var ride = new DoubleAnimation(to, Slide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ride.Completed += (_, _) => scroll.ClearValue(TargetProperty);
        scroll.BeginAnimation(OffsetProperty, ride);
    }
}
