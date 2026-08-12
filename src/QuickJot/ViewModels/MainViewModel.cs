using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuickJot.Data;

namespace QuickJot.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>Подсветка новой строки — раздел 15.</summary>
    private static readonly TimeSpan HighlightDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>Высота строки заголовка относительно кегля — из неё считается потолок в N строк.</summary>
    private const double LineHeightRatio = 1.35;

    /// <summary>Сколько выполненная задача остаётся на месте, прежде чем уехать в блок — раздел 9.</summary>
    private static readonly TimeSpan CompletionDelay = TimeSpan.FromMilliseconds(1500);

    private readonly TaskRepository _tasks;
    private readonly SettingsStore? _settings;
    private readonly ICollectionView _view;
    private static readonly TimeSpan ToastDuration = TimeSpan.FromSeconds(4);

    private readonly Func<DateTime> _now;
    private DateTime _lastRollover;
    private DispatcherTimer? _toastTimer;
    private bool _recording = true;

    public MainViewModel(TaskRepository tasks, SettingsStore? settings = null, Func<DateTime>? now = null)
    {
        _tasks = tasks;
        _settings = settings;
        _now = now ?? (() => DateTime.Now); // подменяется проверкой полуночи в тестах

        ReadSettings();

        // Фильтр поверх той же коллекции, а не второй список: ListBox и так работает через это представление.
        _view = CollectionViewSource.GetDefaultView(Tasks);
        _view.Filter = Matches;

        // При нижних углах список растёт вверх, поэтому порядок представления переворачивается,
        // а сама коллекция остаётся канонической — по возрастанию sort_order (раздел 5).
        if (_view is ListCollectionView sortable) sortable.CustomSort = new BySortOrder(IsMirrored);
        Tasks.CollectionChanged += (_, _) => NotifyEmptyState();
        CompletedToday.CollectionChanged += (_, _) => NotifyCompletedState();
    }

    /// <summary>Текст поля ввода. Фильтрация и сохранение черновика — этап 5.</summary>
    [ObservableProperty]
    private string _draft = "";

    /// <summary>Текст поля заметки при создании задачи — раздел 7.</summary>
    [ObservableProperty]
    private string _noteDraft = "";

    /// <summary>Поле заметки раскрыто. Свернуть можно только пустое — раздел 7.</summary>
    [ObservableProperty]
    private bool _isNoteOpen;

    /// <summary>Шпаргалка по горячим клавишам, F1 — раздел 10.</summary>
    [ObservableProperty]
    private bool _isHelpOpen;

    /// <summary>Внутренний тост снизу окна — раздел 11. Системного уведомления тут быть не должно.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToast))]
    private string? _toast;

    public bool HasToast => Toast is not null;

    public UndoStack History { get; } = new();

    [RelayCommand]
    private void Undo()
    {
        if (History.Undo() is { } description) ShowToast($"Отменено: {description.ToLowerInvariant()}");
    }

    [RelayCommand]
    private void Redo()
    {
        if (History.Redo() is { } description) ShowToast(description);
    }

    /// <summary>
    /// Пока идёт отмена или повтор, операции не записываются: обратные действия меняют те же свойства,
    /// и стек начал бы отменять сам себя. Запись в базу при этом продолжает работать.
    /// </summary>
    private void Record(string description, Action undo, Action redo)
    {
        if (!_recording) return;

        History.Push(description, Wrap(undo), Wrap(redo));
        return;

        Action Wrap(Action action) => () =>
        {
            _recording = false;
            try { action(); }
            finally { _recording = true; }
        };
    }

    private void ShowToast(string message)
    {
        Toast = message;

        _toastTimer ??= new DispatcherTimer { Interval = ToastDuration };
        _toastTimer.Stop();
        _toastTimer.Tick -= HideToast;
        _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(object? sender, EventArgs e)
    {
        _toastTimer?.Stop();
        Toast = null;
    }

    /// <summary>Лимит строк заголовка, 1–4 — раздел 8.</summary>
    public int TitleLines { get; private set; }

    public string TitleFont { get; private set; } = "Segoe UI Variable Text";

    public double TitleFontSize { get; private set; } = 14;

    public double TitleLineHeight => Math.Round(TitleFontSize * LineHeightRatio);

    public double TitleMaxHeight => TitleLines * TitleLineHeight;

    /// <summary>Угол привязки окна — раздел 5.</summary>
    public AnchorCorner Corner { get; private set; }

    /// <summary>Нижние углы: поле ввода снизу, список растёт вверх (модель мессенджера).</summary>
    public bool IsMirrored => Corner is AnchorCorner.BottomLeft or AnchorCorner.BottomRight;

    public double WindowWidth { get; private set; }

    public double MaxHeightShare { get; private set; }

    public bool AnimationsEnabled { get; private set; } = true;

    /// <summary>
    /// Анимации играют, только если их не выключили ни в настройках, ни в самой Windows — раздел 15.
    /// Системный флаг важнее: его ставят те, кому анимация мешает физически.
    /// </summary>
    public bool AnimationsOn => AnimationsEnabled && SystemParameters.ClientAreaAnimation;

    /// <summary>Тема: system, light или dark — раздел 13.</summary>
    public string Theme { get; private set; } = "system";

    /// <summary>Перечитать настройки после окна настроек и разослать всё, что от них зависит.</summary>
    public void ReloadSettings()
    {
        bool wasMirrored = IsMirrored;
        ReadSettings();

        if (wasMirrored != IsMirrored && _view is ListCollectionView sortable)
        {
            sortable.CustomSort = new BySortOrder(IsMirrored);
        }

        OnPropertyChanged(nameof(AnimationsOn));
        OnPropertyChanged(nameof(TitleLines));
        OnPropertyChanged(nameof(TitleFont));
        OnPropertyChanged(nameof(TitleFontSize));
        OnPropertyChanged(nameof(TitleLineHeight));
        OnPropertyChanged(nameof(TitleMaxHeight));
    }

    private void ReadSettings()
    {
        TitleLines = (int)Math.Clamp(_settings?.GetDouble(SettingKeys.TitleLines, 2) ?? 2, 1, 4);
        TitleFont = _settings?.Get(SettingKeys.TitleFont) ?? "Segoe UI Variable Text";
        TitleFontSize = Math.Clamp(_settings?.GetDouble(SettingKeys.TitleSize, 14) ?? 14, 11, 20);

        Corner = Enum.TryParse<AnchorCorner>(_settings?.Get(SettingKeys.Corner), out var corner)
            ? corner
            : AnchorCorner.TopRight;

        WindowWidth = Math.Clamp(_settings?.GetDouble(SettingKeys.Width, 560) ?? 560, 360, 1200);
        MaxHeightShare = Math.Clamp(_settings?.GetDouble(SettingKeys.MaxHeightShare, 0.6) ?? 0.6, 0.2, 0.95);

        AnimationsEnabled = _settings?.GetBool(SettingKeys.Animations, true) ?? true;
        Theme = _settings?.Get(SettingKeys.Theme) ?? "system";
    }

    /// <summary>Ширина окна меняется мышью и сохраняется — раздел 5.</summary>
    public void SaveWindowWidth(double width) => _settings?.SetDouble(SettingKeys.Width, width);

    public ObservableCollection<TaskCardViewModel> Tasks { get; } = [];

    /// <summary>Блок «Выполнено сегодня» в конце списка — раздел 9.</summary>
    public ObservableCollection<TaskCardViewModel> CompletedToday { get; } = [];

    [ObservableProperty]
    private bool _isCompletedExpanded;

    public bool HasCompleted => CompletedToday.Count > 0;

    public string CompletedHeader => $"{(IsCompletedExpanded ? "▾" : "▸")}  Выполнено сегодня · {CompletedToday.Count}";

    /// <summary>Задачи после фильтра — то, что реально видно в списке.</summary>
    public IEnumerable<TaskCardViewModel> VisibleTasks => _view.Cast<TaskCardViewModel>();

    public bool IsListEmpty => _view.IsEmpty;

    /// <summary>Пустое состояние — раздел 9, и подсказка «Enter — создать» — раздел 7.</summary>
    public string EmptyMessage => Filter.Length > 0
        ? "Совпадений нет · Enter — создать"
        : "Задач нет";

    /// <summary>Поле ввода одновременно создаёт и фильтрует, поэтому запрос — это черновик без краёв.</summary>
    private string Filter => Draft.Trim();

    /// <summary>При активном фильтре перетаскивание запрещено — раздел 9.</summary>
    public bool IsFiltered => Filter.Length > 0;

    /// <summary>
    /// Перетаскивание мышью — раздел 9. Индекс приходит в порядке показа, а соседи для sort_order
    /// нужны канонические: при нижних углах список перевёрнут, и «выше» с «ниже» меняются местами.
    /// </summary>
    public void MoveTo(TaskCardViewModel card, int targetIndex)
    {
        if (IsFiltered) return;

        var visible = VisibleTasks.ToList();
        int current = visible.IndexOf(card);
        if (current < 0) return;

        visible.RemoveAt(current);
        targetIndex = Math.Clamp(targetIndex > current ? targetIndex - 1 : targetIndex, 0, visible.Count);

        var before = targetIndex > 0 ? visible[targetIndex - 1] : null;
        var after = targetIndex < visible.Count ? visible[targetIndex] : null;

        var above = IsMirrored ? after : before;
        var below = IsMirrored ? before : after;

        double previousOrder = card.SortOrder;
        int previousIndex = Tasks.IndexOf(card);

        card.SortOrder = _tasks.MoveBetween(card.Id, above?.Id, below?.Id);
        Tasks.Remove(card);
        InsertByOrder(card); // коллекция остаётся канонической, порядок показа задаёт представление
        _view.Refresh();

        double newOrder = card.SortOrder;
        int newIndex = Tasks.IndexOf(card);
        Record("Задача переставлена",
            () => PlaceAt(card, previousOrder, previousIndex),
            () => PlaceAt(card, newOrder, newIndex));
    }

    public void Load()
    {
        foreach (var card in Tasks) Detach(card);
        Tasks.Clear();

        foreach (var item in _tasks.Active()) Add(new TaskCardViewModel(item));

        _lastRollover = _now().Date;
        ReloadCompleted();

        // Черновик переживает закрытие окна — раздел 7. Вместе с ним восстанавливается и фильтр.
        Draft = _settings?.Get(SettingKeys.DraftTitle) ?? "";
        NoteDraft = _settings?.Get(SettingKeys.DraftNote) ?? "";
        IsNoteOpen = NoteDraft.Length > 0;
    }

    /// <summary>
    /// Вызывается при скрытии окна и при выходе, а не на каждое нажатие клавиши:
    /// запись в базу синхронная, а черновик нужен только к следующему открытию.
    /// </summary>
    public void SaveDraft()
    {
        _settings?.Set(SettingKeys.DraftTitle, Draft);
        _settings?.Set(SettingKeys.DraftNote, NoteDraft);
    }

    /// <summary>
    /// Проверка локальной полуночи — при каждом показе окна, а не по таймеру: приложение может
    /// проработать всю ночь (раздел 9). Она же чистит удалённых старше суток.
    /// </summary>
    public void CheckDayRollover()
    {
        var today = _now().Date;
        if (today == _lastRollover) return;

        _lastRollover = today;
        ReloadCompleted();
        _tasks.PurgeDeletedOlderThan(TimeSpan.FromDays(1));
    }

    private void ReloadCompleted()
    {
        foreach (var card in CompletedToday) Detach(card);
        CompletedToday.Clear();

        // Свежие сверху — раздел 9. Репозиторий отдаёт их уже в этом порядке.
        foreach (var item in _tasks.CompletedSince(TodayStartUtc()))
        {
            var card = new TaskCardViewModel(item) { IsCompleted = true };
            card.PropertyChanged += OnCardChanged;
            card.DeleteRequested += OnDeleteRequested;
            CompletedToday.Add(card);
        }
    }

    private DateTime TodayStartUtc() => _now().Date.ToUniversalTime();

    /// <summary>Задача уезжает в блок не сразу: 1.5 с она остаётся на месте зачёркнутой — раздел 9.</summary>
    private void StartCompletionDelay(TaskCardViewModel card)
    {
        var timer = new DispatcherTimer { Interval = CompletionDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!card.IsCompleted) return; // успели снять галочку — задача никуда не едет

            Tasks.Remove(card);
            CompletedToday.Insert(0, card);
        };
        timer.Start();
    }

    /// <summary>Возврат из выполненных: задача встаёт на прежнее место, sort_order не менялся — раздел 9.</summary>
    private void ReturnToActive(TaskCardViewModel card)
    {
        CompletedToday.Remove(card);
        if (Tasks.Contains(card)) return; // галочку сняли до того, как карточка уехала в блок

        InsertByOrder(card);
    }

    private void InsertByOrder(TaskCardViewModel card)
    {
        int index = 0;
        while (index < Tasks.Count && Tasks[index].SortOrder < card.SortOrder) index++;
        Tasks.Insert(index, card);
    }

    /// <summary>
    /// Ищем и по заголовку, и по чеклисту: в большой задаче нужное чаще всего именно в пунктах.
    /// Заодно помечаем карточку, которую вытащил именно чеклист, — иначе непонятно, за что она всплыла.
    /// </summary>
    private bool Matches(object item)
    {
        if (item is not TaskCardViewModel card) return false;

        if (Filter.Length == 0)
        {
            card.IsSubtaskMatch = false;
            return true;
        }

        bool inTitle = card.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase);
        bool inSubtasks = card.Subtasks.Any(s => s.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase));

        card.IsSubtaskMatch = inSubtasks && !inTitle;
        return inTitle || inSubtasks;
    }

    partial void OnDraftChanged(string value)
    {
        _view.Refresh();
        NotifyEmptyState();
    }

    private void NotifyEmptyState()
    {
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void NotifyCompletedState()
    {
        OnPropertyChanged(nameof(HasCompleted));
        OnPropertyChanged(nameof(CompletedHeader));
    }

    partial void OnIsCompletedExpandedChanged(bool value) => OnPropertyChanged(nameof(CompletedHeader));

    /// <summary>Enter или Ctrl+Enter. false — создавать нечего, заголовок пуст.</summary>
    public bool Create()
    {
        var title = Draft.Trim();
        if (title.Length == 0) return false;

        var notes = string.IsNullOrWhiteSpace(NoteDraft) ? null : NoteDraft.Trim();
        var card = new TaskCardViewModel(_tasks.Create(title, notes)) { IsNew = true };
        Add(card, atTop: true); // новые в начало списка — раздел 9
        Record("Задача создана", () => RemoveCard(card), () => RestoreCard(card));

        Draft = "";
        NoteDraft = "";
        IsNoteOpen = false;

        // Полноценная анимация вставки — этап 10, здесь достаточно снять подсветку по таймеру.
        var highlight = new DispatcherTimer { Interval = HighlightDuration };
        highlight.Tick += (_, _) =>
        {
            highlight.Stop();
            card.IsNew = false;
        };
        highlight.Start();

        return true;
    }

    /// <summary>
    /// Alt+↑ — раздел 10. Позиция считается по соседям в списке, значение — через `(a+b)/2`
    /// с перенумерацией, если точность исчерпана (раздел 9).
    /// </summary>
    public void MoveUp(TaskCardViewModel card)
    {
        if (Filter.Length > 0) return; // при активном фильтре позиция вставки неоднозначна — раздел 9

        int index = Tasks.IndexOf(card);
        if (index <= 0) return;

        double previousOrder = card.SortOrder;
        card.SortOrder = _tasks.MoveBetween(card.Id, index >= 2 ? Tasks[index - 2].Id : null, Tasks[index - 1].Id);
        Tasks.Move(index, index - 1);
        _view.Refresh();

        double newOrder = card.SortOrder;
        Record("Задача переставлена",
            () => PlaceAt(card, previousOrder, index),
            () => PlaceAt(card, newOrder, index - 1));
    }

    /// <summary>Alt+↓ — раздел 10.</summary>
    public void MoveDown(TaskCardViewModel card)
    {
        if (Filter.Length > 0) return;

        int index = Tasks.IndexOf(card);
        if (index < 0 || index >= Tasks.Count - 1) return;

        double previousOrder = card.SortOrder;
        card.SortOrder = _tasks.MoveBetween(card.Id, Tasks[index + 1].Id, index + 2 < Tasks.Count ? Tasks[index + 2].Id : null);
        Tasks.Move(index, index + 1);
        _view.Refresh();

        double newOrder = card.SortOrder;
        Record("Задача переставлена",
            () => PlaceAt(card, previousOrder, index),
            () => PlaceAt(card, newOrder, index + 1));
    }

    /// <summary>Вернуть задачу на конкретное место с конкретным sort_order — раздел 11.</summary>
    private void PlaceAt(TaskCardViewModel card, double order, int index)
    {
        _tasks.SetSortOrder(card.Id, order);
        card.SortOrder = order;

        int current = Tasks.IndexOf(card);
        if (current >= 0 && current != index) Tasks.Move(current, Math.Clamp(index, 0, Tasks.Count - 1));

        _view.Refresh(); // sort_order поменялся — представление о нём само не узнает
    }

    /// <summary>Порядок представления. Коллекция всегда по возрастанию, переворачивается только вид.</summary>
    private sealed class BySortOrder(bool descending) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not TaskCardViewModel first || y is not TaskCardViewModel second) return 0;

            int result = first.SortOrder.CompareTo(second.SortOrder);
            return descending ? -result : result;
        }
    }

    private void Add(TaskCardViewModel card, bool atTop = false)
    {
        card.PropertyChanged += OnCardChanged;
        card.DeleteRequested += OnDeleteRequested;

        if (atTop) Tasks.Insert(0, card);
        else Tasks.Add(card);
    }

    private void Detach(TaskCardViewModel card)
    {
        card.PropertyChanged -= OnCardChanged;
        card.DeleteRequested -= OnDeleteRequested;
    }

    /// <summary>Мягкое удаление — раздел 9, с тостом и записью в стек отмены — раздел 11.</summary>
    private void OnDeleteRequested(TaskCardViewModel card)
    {
        RemoveCard(card);
        Record("Задача удалена", () => RestoreCard(card), () => RemoveCard(card));
        ShowToast("Задача удалена");
    }

    private void RemoveCard(TaskCardViewModel card)
    {
        _tasks.Delete(card.Id);
        Detach(card);
        Tasks.Remove(card);
        CompletedToday.Remove(card);
    }

    private void RestoreCard(TaskCardViewModel card)
    {
        _tasks.Restore(card.Id);
        card.PropertyChanged += OnCardChanged;
        card.DeleteRequested += OnDeleteRequested;

        if (card.IsCompleted) CompletedToday.Insert(0, card);
        else InsertByOrder(card);
    }

    /// <summary>
    /// Единственное место, где изменения карточки уходят в базу.
    /// Выполненная задача пока просто пропадает из списка: задержка 1.5 с, блок «Выполнено сегодня»
    /// и возврат — этап 6.
    /// </summary>
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaskCardViewModel card) return;

        switch (e.PropertyName)
        {
            // Выполнение пишется в базу сразу, а не через 1.5 с: задержка — только для глаз.
            case nameof(TaskCardViewModel.IsCompleted) when card.IsCompleted:
                _tasks.Complete(card.Id);
                StartCompletionDelay(card);
                Record("Задача выполнена", () => card.IsCompleted = false, () => card.IsCompleted = true);
                break;

            case nameof(TaskCardViewModel.IsCompleted):
                _tasks.Uncomplete(card.Id);
                ReturnToActive(card);
                Record("Задача возвращена", () => card.IsCompleted = true, () => card.IsCompleted = false);
                break;

            case nameof(TaskCardViewModel.IsFlagged):
                _tasks.SetFlagged(card.Id, card.IsFlagged);
                bool flagged = card.IsFlagged;
                Record("Важность изменена", () => card.IsFlagged = !flagged, () => card.IsFlagged = flagged);
                break;

            case nameof(TaskCardViewModel.IsExpanded):
                // Разворот заметки — состояние карточки, а не операция: отменять тут нечего (раздел 11).
                _tasks.SetExpanded(card.Id, card.IsExpanded);
                break;

            case nameof(TaskCardViewModel.Title):
            {
                // Item хранит последнее записанное в базу значение, поэтому прежний текст берётся оттуда.
                var previous = card.Item.Title;
                var current = card.Title;
                _tasks.SetTitle(card.Id, current);
                card.Item.Title = current;
                Record("Заголовок изменён", () => card.Title = previous, () => card.Title = current);
                break;
            }

            case nameof(TaskCardViewModel.Notes):
            {
                var previous = card.Item.Notes;
                var current = string.IsNullOrWhiteSpace(card.Notes) ? null : card.Notes;
                _tasks.SetNotes(card.Id, current);
                card.Item.Notes = current;
                Record("Заметка изменена", () => card.Notes = previous, () => card.Notes = current);
                break;
            }

            // Чеклист пишется целиком: отмена возвращает прежнюю строку, а не отдельный пункт.
            case nameof(TaskCardViewModel.SubtasksText):
            {
                var previous = card.Item.Subtasks;
                var current = card.SubtasksText;
                if (previous == current) break;

                _tasks.SetSubtasks(card.Id, current);
                card.Item.Subtasks = current;
                Record(card.SubtaskChange, () => card.LoadSubtasks(previous), () => card.LoadSubtasks(current));
                break;
            }
        }
    }
}

/// <summary>Сторона карточки, у которой рисуется полоска места вставки при перетаскивании.</summary>
public enum DropSide
{
    None,
    Above,
    Below,
}

public sealed partial class TaskCardViewModel : ObservableObject
{
    public TaskCardViewModel(TaskItem item)
    {
        Item = item;
        SortOrder = item.SortOrder;
        _title = item.Title;
        _notes = item.Notes;
        _isFlagged = item.IsFlagged;
        _isExpanded = item.IsExpanded;

        foreach (var parsed in SubtaskFormat.Parse(item.Subtasks)) Subtasks.Add(Attach(new SubtaskViewModel(parsed)));
    }

    public TaskItem Item { get; }

    public string Id => Item.Id;

    /// <summary>Держится в памяти: по нему задача возвращается на прежнее место из выполненных.</summary>
    public double SortOrder { get; set; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotes))]
    private string? _notes;

    [ObservableProperty]
    private bool _isFlagged;

    /// <summary>Развёрнутая заметка и чеклист в карточке — раздел 8.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>Кратковременная подсветка только что созданной задачи.</summary>
    [ObservableProperty]
    private bool _isNew;

    /// <summary>Куда встанет перетаскиваемая задача — полоска сверху или снизу карточки.</summary>
    [ObservableProperty]
    private DropSide _dropIndicator;

    /// <summary>Правка заголовка на месте — раздел 10.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>
    /// Отдельный буфер, а не сам заголовок: иначе каждое нажатие клавиши уходило бы в базу
    /// синхронной записью, а Esc нечем было бы откатить.
    /// </summary>
    [ObservableProperty]
    private string _editTitle = "";

    public void BeginEdit()
    {
        EditTitle = Title;
        IsEditing = true;
    }

    /// <summary>Enter — сохранить. Пустой заголовок не сохраняется, правка просто отменяется.</summary>
    public void CommitEdit()
    {
        IsEditing = false;
        var edited = EditTitle.Trim();
        if (edited.Length > 0 && edited != Title) Title = edited;
    }

    /// <summary>Esc — откатить изменение заголовка — раздел 10.</summary>
    public void CancelEdit() => IsEditing = false;

    /// <summary>Облачко видно всегда, активный цвет — когда заметка есть.</summary>
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

    // --- чеклист, раздел 8 ---

    /// <summary>Ведущая коллекция; строка для базы собирается из неё, а не наоборот.</summary>
    public ObservableCollection<SubtaskViewModel> Subtasks { get; } = [];

    /// <summary>То, что уходит в базу. На него подписана запись, поэтому извещается последним.</summary>
    public string? SubtasksText => SubtaskFormat.Format(Subtasks.Select(s => new Subtask(s.IsDone, s.Title)));

    public bool HasSubtasks => Subtasks.Count > 0;

    public string SubtaskCounter => $"{Subtasks.Count(s => s.IsDone)}/{Subtasks.Count}";

    public bool AllSubtasksDone => HasSubtasks && Subtasks.All(s => s.IsDone);

    /// <summary>Карточку вытащил в фильтр чеклист, а не заголовок, — счётчик подсвечивается.</summary>
    [ObservableProperty]
    private bool _isSubtaskMatch;

    /// <summary>Чем описать последнее изменение чеклиста в стеке отмены — раздел 11.</summary>
    public string SubtaskChange { get; private set; } = "Чеклист изменён";

    public SubtaskViewModel AddSubtask(string title)
    {
        var subtask = Attach(new SubtaskViewModel(new Subtask(false, title.Trim())));
        Subtasks.Add(subtask);
        NotifySubtasks("Подзадача добавлена");
        return subtask;
    }

    public void RemoveSubtask(SubtaskViewModel subtask)
    {
        Detach(subtask);
        if (Subtasks.Remove(subtask)) NotifySubtasks("Подзадача удалена");
    }

    /// <summary>Alt+↑↓ внутри чеклиста — раздел 10. Порядок тут позиционный, sort_order не нужен.</summary>
    public void MoveSubtask(SubtaskViewModel subtask, int delta)
    {
        int index = Subtasks.IndexOf(subtask);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Subtasks.Count) return;

        Subtasks.Move(index, target);
        NotifySubtasks("Подзадача переставлена");
    }

    /// <summary>Пересобрать чеклист из строки: сюда приходит отмена — раздел 11.</summary>
    public void LoadSubtasks(string? stored)
    {
        foreach (var old in Subtasks) Detach(old);
        Subtasks.Clear();

        foreach (var parsed in SubtaskFormat.Parse(stored)) Subtasks.Add(Attach(new SubtaskViewModel(parsed)));
        NotifySubtasks("Чеклист изменён");
    }

    private SubtaskViewModel Attach(SubtaskViewModel subtask)
    {
        subtask.PropertyChanged += OnSubtaskChanged;
        subtask.DeleteRequested += RemoveSubtask;
        return subtask;
    }

    private void Detach(SubtaskViewModel subtask)
    {
        subtask.PropertyChanged -= OnSubtaskChanged;
        subtask.DeleteRequested -= RemoveSubtask;
    }

    private void OnSubtaskChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SubtaskViewModel.IsDone) when sender is SubtaskViewModel { IsDone: true }:
                NotifySubtasks("Подзадача выполнена");
                break;

            case nameof(SubtaskViewModel.IsDone):
                NotifySubtasks("Подзадача возвращена");
                break;

            case nameof(SubtaskViewModel.Title):
                NotifySubtasks("Подзадача изменена");
                break;
        }
    }

    private void NotifySubtasks(string description)
    {
        SubtaskChange = description;
        OnPropertyChanged(nameof(HasSubtasks));
        OnPropertyChanged(nameof(SubtaskCounter));
        OnPropertyChanged(nameof(AllSubtasksDone));
        OnPropertyChanged(nameof(SubtasksText)); // последним: на него подписана запись в базу
    }

    /// <summary>Удаляет не карточка — список. Она только просит.</summary>
    public event Action<TaskCardViewModel>? DeleteRequested;

    [RelayCommand]
    private void ToggleFlag() => IsFlagged = !IsFlagged;

    /// <summary>Клик по облачку: заметка есть — развернуть, нет — открыть пустое поле — раздел 8.</summary>
    [RelayCommand]
    private void ToggleNote() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this);
}

/// <summary>
/// Пункт чеклиста. Ведёт себя как маленькая карточка и слушает те же клавиши (раздел 10):
/// Space — выполнить, Enter — править, Delete — удалить, Alt+↑↓ — переставить.
/// </summary>
public sealed partial class SubtaskViewModel(Subtask subtask) : ObservableObject
{
    [ObservableProperty]
    private bool _isDone = subtask.Done;

    [ObservableProperty]
    private string _title = subtask.Title;

    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Буфер правки: как у заголовка задачи, чтобы Esc было чем откатить.</summary>
    [ObservableProperty]
    private string _editTitle = "";

    public void BeginEdit()
    {
        EditTitle = Title;
        IsEditing = true;
    }

    public void CommitEdit()
    {
        IsEditing = false;
        var edited = EditTitle.Trim();
        if (edited.Length > 0 && edited != Title) Title = edited;
    }

    public void CancelEdit() => IsEditing = false;

    /// <summary>Удаляет не пункт — карточка. Он только просит.</summary>
    public event Action<SubtaskViewModel>? DeleteRequested;

    [RelayCommand]
    private void Toggle() => IsDone = !IsDone;

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this);
}
