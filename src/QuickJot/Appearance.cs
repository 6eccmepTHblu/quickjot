using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuickJot;

/// <summary>
/// Появление окна — раздел 15: проявление и лёгкий рост от точки привязки. Анимируется
/// содержимое, а не само окно: прозрачность окна требует AllowsTransparency, который ломает Mica.
/// </summary>
public static class Appearance
{
    private static readonly Duration Grow = new(TimeSpan.FromMilliseconds(120));

    /// <summary>Центр экрана: у окон настроек и тегов расти неоткуда, кроме как из середины.</summary>
    public static readonly Point FromCenter = new(0.5, 0.5);

    /// <summary>
    /// Ставится списком задач: настройки читает только он. Статикой по той же причине, что и
    /// у <see cref="ViewModels.Leaving"/> — окно одно, а знать про настройки должно каждое.
    /// </summary>
    public static bool Animated { get; set; }

    public static void Play(FrameworkElement root, Point origin)
    {
        if (!Animated)
        {
            root.Opacity = 1;
            root.RenderTransform = Transform.Identity;
            return;
        }

        root.RenderTransformOrigin = origin;

        var scale = new ScaleTransform(0.98, 0.98);
        root.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.98, 1, Grow) { EasingFunction = ease };

        root.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Grow));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }
}
