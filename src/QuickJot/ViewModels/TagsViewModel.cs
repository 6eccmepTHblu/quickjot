using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickJot.Data;

namespace QuickJot.ViewModels;

/// <summary>
/// Окно тегов — раздел 13. Справочника тегов в базе нет: список собирается из задач,
/// поэтому здесь можно только перекрасить тег и убрать его из всех задач сразу.
/// </summary>
public sealed partial class TagsViewModel : ObservableObject
{
    private readonly TaskRepository _tasks;
    private readonly TagColors _colors;

    public TagsViewModel(TaskRepository tasks, TagColors colors, string? theme)
    {
        _tasks = tasks;
        _colors = colors;
        Dark = Theme.IsDark(theme);

        Reload();
    }

    public bool Dark { get; }

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    public bool IsEmpty => Tags.Count == 0;

    /// <summary>Цвет тега сменился — список задач должен перекрасить свои чипы.</summary>
    public event Action? ColorsChanged;

    /// <summary>Тег убран из всех задач — список должен убрать его и у себя.</summary>
    public event Action<string>? Removed;

    private void Reload()
    {
        Tags.Clear();

        foreach (var (tag, count) in _tasks.TagUsage())
        {
            var row = new TagRowViewModel(tag, count, _colors.Chosen(tag), Dark);
            row.ColorChosen += OnColorChosen;
            row.DeleteConfirmed += OnDeleteConfirmed;
            Tags.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnColorChosen(TagRowViewModel row, int? index)
    {
        _colors.Choose(row.Name, index);
        ColorsChanged?.Invoke();
    }

    private void OnDeleteConfirmed(TagRowViewModel row)
    {
        _tasks.RemoveTag(row.Name);
        _colors.Forget(row.Name);

        Tags.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
        Removed?.Invoke(row.Name);
    }
}

/// <summary>Строка списка тегов: чип, число задач, палитра и удаление с подтверждением.</summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    private readonly bool _dark;

    public TagRowViewModel(string name, int count, int? chosen, bool dark)
    {
        Name = name;
        Count = count;
        _dark = dark;
        _chosen = chosen;

        // «Авто» — такой же вариант выбора, как цвет: им сбрасывают ручной цвет обратно.
        Swatches = [.. Enumerable.Range(0, TagPalette.Count).Select(index => new TagSwatch(index, dark))];

        UpdateSelection();
    }

    public string Name { get; }

    public string Label => TagFormat.Marker + Name;

    public int Count { get; }

    public string CountLabel => Count == 1 ? "1 задача" : $"{Count} задач";

    public IReadOnlyList<TagSwatch> Swatches { get; }

    [ObservableProperty]
    private int? _chosen;

    public bool IsAuto => Chosen is null;

    public Brush Background => TagPalette.Background(Index, _dark);

    public Brush Foreground => TagPalette.Foreground(Index, _dark);

    private int Index => Chosen ?? TagPalette.IndexOf(Name);

    /// <summary>Подтверждение удаления прямо в строке: тег стирается сразу из всех задач.</summary>
    [ObservableProperty]
    private bool _isConfirming;

    public event Action<TagRowViewModel, int?>? ColorChosen;

    public event Action<TagRowViewModel>? DeleteConfirmed;

    [RelayCommand]
    private void Pick(object? parameter)
    {
        Chosen = parameter is TagSwatch swatch ? swatch.Index : null;
        UpdateSelection();

        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(Foreground));
        OnPropertyChanged(nameof(IsAuto));
        ColorChosen?.Invoke(this, Chosen);
    }

    [RelayCommand]
    private void AskDelete() => IsConfirming = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirming = false;

    [RelayCommand]
    private void Delete() => DeleteConfirmed?.Invoke(this);

    private void UpdateSelection()
    {
        foreach (var swatch in Swatches) swatch.IsSelected = swatch.Index == Chosen;
    }
}

/// <summary>Кружок палитры в строке тега.</summary>
public sealed partial class TagSwatch(int index, bool dark) : ObservableObject
{
    public int Index { get; } = index;

    public Brush Fill { get; } = new SolidColorBrush(TagPalette.Hue(index));

    public Brush Ring { get; } = TagPalette.Foreground(index, dark);

    [ObservableProperty]
    private bool _isSelected;
}
