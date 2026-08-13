using System.Windows;
using System.Windows.Threading;

namespace QuickJot.ViewModels;

/// <summary>Строка списка, которая умеет доиграть свой уход, прежде чем исчезнуть.</summary>
public interface ILeaving
{
    bool IsLeaving { get; set; }
}

/// <summary>
/// Уход строки — раздел 15. WPF убирает контейнер в тот же кадр, что и элемент из коллекции,
/// поэтому «прощальной» анимации у него нет в принципе: к моменту, когда её играть, играть уже
/// нечему. Единственный рабочий порядок — пометить строку уходящей, дать разметке схлопнуть её
/// и убрать по таймеру.
/// </summary>
public static class Leaving
{
    /// <summary>Ровно столько же длится схлопывание строки в разметке.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    /// <summary>
    /// Ставится списком задач: настройки читает только он. Статикой, потому что окно одно,
    /// а протаскивать флаг до каждого пункта чеклиста дороже, чем он стоит.
    /// </summary>
    public static bool Animated { get; set; }

    public static void Play(ILeaving row, Action remove)
    {
        if (!Animated)
        {
            remove();
            return;
        }

        row.IsLeaving = true;

        var timer = new DispatcherTimer { Interval = Duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!row.IsLeaving) return; // строку успели вернуть, пока она уходила

            row.IsLeaving = false;
            remove();
        };
        timer.Start();
    }
}
