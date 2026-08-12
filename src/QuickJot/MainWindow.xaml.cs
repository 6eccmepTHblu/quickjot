using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickJot.ViewModels;

namespace QuickJot;

public partial class MainWindow : Window
{
    /// <summary>Отступ от края рабочей области — раздел 5.</summary>
    private const double EdgeMargin = 16;

    /// <summary>Ниже этого прокручиваемая часть не сжимается, даже если потолок задан крохотным.</summary>
    private const double MinScrollHeight = 120;

    /// <summary>Жёсткий предел заголовка — раздел 7. Тот же, что MaxLength у поля в разметке.</summary>
    private const int MaxTitleLength = 500;

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Width = viewModel.WindowWidth;

        ApplyThemeBrushes();
        ApplyLayoutOrder();
        DataObject.AddPastingHandler(Input, OnTitlePasting);
    }

    /// <summary>
    /// Появление окна: fade + scale 0.98→1 за 120 мс от угла привязки — раздел 15.
    /// Анимируется содержимое, а не само окно: прозрачность окна требует AllowsTransparency,
    /// который ломает Mica (раздел 5).
    /// </summary>
    public void PlayAppearance()
    {
        if (!_viewModel.AnimationsOn)
        {
            Root.Opacity = 1;
            Root.RenderTransform = Transform.Identity;
            return;
        }

        Root.RenderTransformOrigin = _viewModel.Corner switch
        {
            AnchorCorner.TopLeft => new Point(0, 0),
            AnchorCorner.TopRight => new Point(1, 0),
            AnchorCorner.BottomLeft => new Point(0, 1),
            _ => new Point(1, 1),
        };

        var scale = new ScaleTransform(0.98, 0.98);
        Root.RenderTransform = scale;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.98, 1, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };

        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    /// <summary>Применить всё, что изменилось в окне настроек — раздел 13.</summary>
    public void ApplySettings()
    {
        Width = _viewModel.WindowWidth;
        ApplyThemeBrushes();
        ApplyLayoutOrder();
        ApplyHeightCeiling();
    }

    /// <summary>
    /// Зеркалирование при нижних углах — раздел 5: поле ввода снизу, список растёт вверх,
    /// блок выполненного уезжает на дальний от поля конец. Без этого поле ввода уползало бы
    /// вместе с растущим окном и мышечная память не складывалась бы.
    /// </summary>
    public void ApplyLayoutOrder()
    {
        bool mirrored = _viewModel.IsMirrored;

        Grid.SetRow(InputArea, mirrored ? 4 : 0);
        Grid.SetRow(Note, mirrored ? 3 : 1);
        Grid.SetRow(Divider, 2);
        Grid.SetRow(Scroll, mirrored ? 1 : 3);
        Grid.SetRow(Toast, mirrored ? 0 : 4);

        UIElement[] order = mirrored
            ? [CompletedBlock, EmptyState, List]
            : [List, EmptyState, CompletedBlock];

        ScrollContent.Children.Clear();
        foreach (var element in order) ScrollContent.Children.Add(element);
    }

    /// <summary>Куда ставить каретку при показе окна.</summary>
    public TextBox InputField => Input;

    /// <summary>Esc и Ctrl+Enter. Прятать окно должен тот, кто умеет возвращать фокус, — это не дело окна.</summary>
    public event Action? HideRequested;

    private TaskCardViewModel? EditingCard => _viewModel.Tasks.FirstOrDefault(card => card.IsEditing);

    private TaskCardViewModel? SelectedCard => List.SelectedItem as TaskCardViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.ApplyMicaAndRoundCorners(new WindowInteropHelper(this).Handle);

        // Раскрытие заметки увеличивает шапку уже после того, как потолок посчитан, — пересчитываем.
        // Отложенно: пересчёт сам вызывает UpdateLayout и внутри чужого прохода разметки делать его нельзя.
        Note.IsVisibleChanged += (_, _) =>
            Dispatcher.BeginInvoke(ApplyHeightCeiling, DispatcherPriority.Background);
    }

    /// <summary>
    /// Вся клавиатура ловится здесь: до вложенных полей и до того, как Tab или стрелка уведут фокус сами.
    /// Карта — раздел 10.
    /// </summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_viewModel.IsHelpOpen)
        {
            if (e.Key is Key.F1 or Key.Escape) _viewModel.IsHelpOpen = false;
            e.Handled = true; // пока открыта шпаргалка, остальные клавиши не работают
            return;
        }

        if (e.Key == Key.F1)
        {
            _viewModel.IsHelpOpen = true;
            e.Handled = true;
            return;
        }

        // Отмена работает откуда угодно, включая поле ввода — раздел 10.
        if (e.Key == Key.Z && Ctrl)
        {
            if (Shift) _viewModel.RedoCommand.Execute(null);
            else _viewModel.UndoCommand.Execute(null);

            e.Handled = true;
            return;
        }

        if (EditingCard is { } editing) HandleEditKeys(e, editing);
        else if (Input.IsKeyboardFocused) HandleTitleKeys(e);
        else if (Note.IsKeyboardFocused) HandleNoteKeys(e);
        // Поля внутри карточки разбираются до списка: иначе пробел в тексте заметки
        // достался бы списку и выполнил задачу.
        else if (Keyboard.FocusedElement is TextBox { Name: "SubtaskInput" } adder) HandleSubtaskInputKeys(e, adder);
        else if (FocusedSubtaskList() is { } checklist) HandleSubtaskKeys(e, checklist);
        else if (Keyboard.FocusedElement is TextBox { Name: "NoteEditor" } cardNote) HandleCardNoteKeys(e, cardNote);
        else if (List.IsKeyboardFocusWithin) HandleListKeys(e, List);
        else if (CompletedList.IsKeyboardFocusWithin) HandleListKeys(e, CompletedList);
        else if (e.Key == Key.Escape)
        {
            HideRequested?.Invoke();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Настоящая клавиша. При зажатом Alt WPF кладёт её в SystemKey, а в Key оставляет System, —
    /// без этого Alt+↑↓ не доходит ни до перестановки задач, ни до перестановки пунктов чеклиста.
    /// </summary>
    private static Key Pressed(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    private static bool Ctrl => (Keyboard.Modifiers & ModifierKeys.Control) != 0;
    private static bool Shift => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
    private static bool Alt => (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

    private void HandleTitleKeys(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (_viewModel.Create() && Ctrl) HideRequested?.Invoke();
                break;

            case Key.Tab when !Shift:
                e.Handled = true;
                OpenNote();
                break;

            // Фокус обязан физически уйти в список: иначе Space продолжит печатать пробел — раздел 10.
            // При нижних углах список лежит над полем ввода, поэтому входить в него надо стрелкой вверх.
            case Key.Down when !_viewModel.IsMirrored:
            case Key.Up when _viewModel.IsMirrored:
            case Key.J when Ctrl:
                e.Handled = true;
                FocusList(_viewModel.IsMirrored ? List.Items.Count - 1 : 0);
                break;

            case Key.Escape:
                e.Handled = true;
                HideRequested?.Invoke();
                break;
        }
    }

    private void HandleNoteKeys(KeyEventArgs e)
    {
        switch (e.Key)
        {
            // В заметке Enter переносит строку, создаёт только Ctrl+Enter.
            case Key.Enter when Ctrl:
                e.Handled = true;
                if (_viewModel.Create()) Input.Focus();
                break;

            case Key.Tab when Shift:
                e.Handled = true;
                Input.Focus();
                break;

            case Key.Escape:
                e.Handled = true;
                ReturnToTitle();
                break;
        }
    }

    /// <summary>Работает и для активного списка, и для блока «Выполнено сегодня» — раздел 9.</summary>
    private void HandleListKeys(KeyEventArgs e, ListBox list)
    {
        var card = list.SelectedItem as TaskCardViewModel;
        int index = list.SelectedIndex;

        switch (Pressed(e))
        {
            case Key.Escape:
                e.Handled = true;
                if (card is not null) ForgetEmptyRequests(card);
                Input.Focus(); // повторный Esc уже из поля спрячет окно — раздел 10
                break;

            // Alt+↑↓ двигают карточку так, как её видит человек: при нижних углах список перевёрнут.
            case Key.Up when Alt && card is not null:
                e.Handled = true;
                if (_viewModel.IsMirrored) _viewModel.MoveDown(card);
                else _viewModel.MoveUp(card);
                FocusList(list.Items.IndexOf(card), list);
                break;

            case Key.Down when Alt && card is not null:
                e.Handled = true;
                if (_viewModel.IsMirrored) _viewModel.MoveUp(card);
                else _viewModel.MoveDown(card);
                FocusList(list.Items.IndexOf(card), list);
                break;

            // Уйти обратно в поле ввода: стрелкой в его сторону с ближней к нему строки.
            case Key.Up when index == 0 && !_viewModel.IsMirrored:
            case Key.Down when index == list.Items.Count - 1 && _viewModel.IsMirrored:
                e.Handled = true;
                Input.Focus();
                break;

            case Key.K when Ctrl:
                e.Handled = true;
                FocusList(index - 1, list);
                break;

            case Key.J when Ctrl:
                e.Handled = true;
                FocusList(index + 1, list);
                break;

            // В блоке выполненных та же клавиша возвращает задачу обратно — раздел 9.
            case Key.Space when card is not null:
                e.Handled = true;
                card.IsCompleted = !card.IsCompleted;
                FocusList(index, list);
                break;

            case Key.Enter when card is not null:
                e.Handled = true;
                card.BeginEdit();
                break;

            case Key.Delete when card is not null:
                e.Handled = true;
                card.DeleteCommand.Execute(null);
                FocusList(index, list);
                break;

            // Tab уводит вглубь карточки: заметка, оттуда чеклист — раздел 10.
            case Key.Tab when !Shift && card is not null:
                e.Handled = true;
                FocusCardNote(card);
                break;

            case Key.Right when card is not null:
                e.Handled = true;
                card.IsExpanded = true;
                // Разворачивать нечего — значит, просят место под заметку.
                if (!card.HasNotes && !card.HasSubtasks) card.IsNoteRequested = true;
                break;

            case Key.Left when card is not null:
                e.Handled = true;
                card.IsExpanded = false;
                break;

            // «!» на большинстве раскладок — Shift+1, поэтому обе комбинации из раздела 10.
            case Key.I when Ctrl && card is not null:
            case Key.D1 when Shift && card is not null:
                e.Handled = true;
                card.ToggleFlagCommand.Execute(null);
                break;
        }
    }

    private void HandleEditKeys(KeyEventArgs e, TaskCardViewModel card)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                card.CommitEdit();
                FocusList(List.Items.IndexOf(card));
                break;

            case Key.Escape:
                e.Handled = true;
                card.CancelEdit();
                FocusList(List.Items.IndexOf(card));
                break;

            // Tab из правки открывает заметку этой задачи — раздел 10.
            case Key.Tab when !Shift:
                e.Handled = true;
                card.CommitEdit();
                card.IsExpanded = true;
                FocusCardNote(card);
                break;
        }
    }

    // --- чеклист карточки, раздел 8 ---

    /// <summary>Чеклист, внутри которого сейчас фокус, — или null, если фокус не в нём.</summary>
    private static ListBox? FocusedSubtaskList()
    {
        for (var node = Keyboard.FocusedElement as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListBox { Name: "SubtaskList" } list) return list;
        }

        return null;
    }

    /// <summary>
    /// Клавиши чеклиста — те же, что у списка задач (раздел 10): Space, Enter, Delete, Alt+↑↓.
    /// Ничего своего тут заводить не нужно, это тот же список, только вложенный.
    /// </summary>
    private void HandleSubtaskKeys(KeyEventArgs e, ListBox list)
    {
        if (list.DataContext is not TaskCardViewModel card) return;

        if (card.Subtasks.FirstOrDefault(item => item.IsEditing) is { } editing)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    editing.CommitEdit();
                    FocusSubtask(list, list.Items.IndexOf(editing));
                    break;

                case Key.Escape:
                    e.Handled = true;
                    editing.CancelEdit();
                    FocusSubtask(list, list.Items.IndexOf(editing));
                    break;
            }

            return;
        }

        var subtask = list.SelectedItem as SubtaskViewModel;
        int index = list.SelectedIndex;

        switch (Pressed(e))
        {
            case Key.Space when subtask is not null:
                e.Handled = true;
                subtask.ToggleCommand.Execute(null);
                FocusSubtask(list, index);
                break;

            case Key.Enter when subtask is not null:
                e.Handled = true;
                subtask.BeginEdit();
                break;

            case Key.Delete when subtask is not null:
                e.Handled = true;
                subtask.DeleteCommand.Execute(null);
                if (card.HasSubtasks) FocusSubtask(list, index);
                else FocusSubtaskInput(card);
                break;

            case Key.Up when Alt && subtask is not null:
                e.Handled = true;
                card.MoveSubtask(subtask, -1);
                FocusSubtask(list, list.Items.IndexOf(subtask));
                break;

            case Key.Down when Alt && subtask is not null:
                e.Handled = true;
                card.MoveSubtask(subtask, 1);
                FocusSubtask(list, list.Items.IndexOf(subtask));
                break;

            // Вверх с первого пункта — в заметку, вниз с последнего — в строку добавления.
            case Key.Up:
                e.Handled = true;
                if (index <= 0) FocusCardNote(card);
                else FocusSubtask(list, index - 1);
                break;

            case Key.Down:
                e.Handled = true;
                if (index >= list.Items.Count - 1) FocusSubtaskInput(card);
                else FocusSubtask(list, index + 1);
                break;

            case Key.Tab when !Shift:
                e.Handled = true;
                FocusSubtaskInput(card);
                break;

            case Key.Tab when Shift:
                e.Handled = true;
                FocusCardNote(card);
                break;

            case Key.Escape:
                e.Handled = true;
                FocusCard(card);
                break;
        }
    }

    /// <summary>Строка «+ подзадача». Набранное не теряется: его подбирает уход фокуса.</summary>
    private void HandleSubtaskInputKeys(KeyEventArgs e, TextBox input)
    {
        if (input.DataContext is not TaskCardViewModel card) return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                AddSubtask(input, card); // каретка остаётся здесь: список диктуется подряд
                break;

            case Key.Up:
                e.Handled = true;
                if (card.HasSubtasks && CardElement(card, "SubtaskList") is ListBox list)
                    FocusSubtask(list, card.Subtasks.Count - 1);
                else
                    FocusCardNote(card);
                break;

            case Key.Tab when Shift:
                e.Handled = true;
                FocusCardNote(card);
                break;

            case Key.Tab:
            case Key.Escape:
                e.Handled = true;
                FocusCard(card);
                break;
        }
    }

    /// <summary>
    /// Заметка внутри карточки. Без своей ветки её клавиши уходили бы в список задач:
    /// пробел в тексте выполнял бы задачу, а стрелки сворачивали карточку.
    /// </summary>
    private void HandleCardNoteKeys(KeyEventArgs e, TextBox note)
    {
        if (note.DataContext is not TaskCardViewModel card) return;

        switch (e.Key)
        {
            case Key.Tab when !Shift:
                e.Handled = true;
                FocusSubtaskInput(card);
                break;

            case Key.Escape:
                e.Handled = true;
                FocusCard(card);
                break;
        }
    }

    private static void AddSubtask(TextBox input, TaskCardViewModel card)
    {
        var title = input.Text.Trim();
        if (title.Length == 0) return;

        card.AddSubtask(title);
        input.Clear();
    }

    private void OnSubtaskInputLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: TaskCardViewModel card } input) return;

        AddSubtask(input, card);
        if (!card.HasSubtasks) card.IsSubtaskRequested = false; // ушли, ничего не добавив — строка снова не нужна
    }

    /// <summary>Ушли с карточки, ничего не написав, — открытые по запросу пустые поля прячутся.</summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var card in e.RemovedItems.OfType<TaskCardViewModel>()) ForgetEmptyRequests(card);
    }

    private static void ForgetEmptyRequests(TaskCardViewModel card)
    {
        if (!card.HasNotes) card.IsNoteRequested = false;
        if (!card.HasSubtasks) card.IsSubtaskRequested = false;
    }

    /// <summary>Ушли из пустого поля заметки — оно прячется: пустая коробка в карточке ни о чём.</summary>
    private void OnCardNoteLostFocus(object sender, RoutedEventArgs e)
    {
        // Текст берётся у поля, а не у карточки: привязка обновляет её тоже по уходу фокуса,
        // и порядок этих двух событий не определён.
        if (sender is TextBox { DataContext: TaskCardViewModel card } editor && editor.Text.Trim().Length == 0)
        {
            card.IsNoteRequested = false;
        }
    }

    /// <summary>Вставка списка: каждая строка становится отдельным пунктом.</summary>
    private void OnSubtaskPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox { DataContext: TaskCardViewModel card } input) return;
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted) return;

        var lines = pasted.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count < 2) return; // одна строка — обычная вставка в поле

        e.CancelCommand();
        foreach (var line in lines) card.AddSubtask(line.Length > 500 ? line[..500] : line);
        input.Clear();
    }

    private void OnSubtaskClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is not SubtaskViewModel subtask) return;

        subtask.BeginEdit();
        e.Handled = true;
    }

    private void OnSubtaskEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox editor) return;

        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>Как и у заголовка задачи: уход фокуса завершает правку, а не подвешивает её.</summary>
    private void OnSubtaskEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SubtaskViewModel subtask) subtask.CommitEdit();
    }

    private static void FocusSubtask(ListBox list, int index)
    {
        if (list.Items.Count == 0) return;

        index = Math.Clamp(index, 0, list.Items.Count - 1);
        list.SelectedIndex = index;

        // Длинный чеклист прокручивается внутри карточки, и строки за пределами видимой части
        // ещё не созданы: без ScrollIntoView контейнера просто нет и фокусировать нечего.
        list.ScrollIntoView(list.Items[index]);
        list.UpdateLayout();

        if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row) row.Focus();
    }

    private void FocusSubtaskInput(TaskCardViewModel card)
    {
        card.IsExpanded = true;
        card.IsSubtaskRequested = true; // пришли сюда сами — строку добавления показать

        // Следующим проходом: поле могло стать видимым только что и ещё не в дереве.
        Dispatcher.BeginInvoke(() =>
        {
            if (CardElement(card, "SubtaskInput") is TextBox input)
            {
                input.Focus();
                input.CaretIndex = input.Text.Length;
            }
        }, DispatcherPriority.Input);
    }

    /// <summary>Вернуть фокус на саму карточку — из заметки и из чеклиста по Esc.</summary>
    private void FocusCard(TaskCardViewModel card)
    {
        var list = List.Items.Contains(card) ? List : CompletedList;
        FocusList(list.Items.IndexOf(card), list);
    }

    /// <summary>Элемент внутри карточки по имени: строка живёт в одном из двух списков.</summary>
    private FrameworkElement? CardElement(TaskCardViewModel card, string name)
    {
        foreach (var list in new[] { List, CompletedList })
        {
            int index = list.Items.IndexOf(card);
            if (index < 0) continue;

            list.UpdateLayout();
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row) return FindDescendant(row, name);
        }

        return null;
    }

    /// <summary>При пустом заголовке Tab не делает ничего и молчит — раздел 7.</summary>
    private void OpenNote()
    {
        if (_viewModel.Draft.Trim().Length == 0) return;

        _viewModel.IsNoteOpen = true;
        // Фокус — следующим проходом: поле только что стало видимым и ещё не в дереве.
        Dispatcher.BeginInvoke(() => Note.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Esc из заметки: фокус в заголовок, но непустое поле остаётся раскрытым — раздел 7.</summary>
    private void ReturnToTitle()
    {
        Input.Focus();
        if (string.IsNullOrWhiteSpace(_viewModel.NoteDraft)) _viewModel.IsNoteOpen = false;
    }

    /// <summary>
    /// Выделение в списке должно получать физический фокус клавиатуры, а не только подсветку:
    /// иначе Space уйдёт в поле ввода и напечатает пробел — раздел 10.
    /// </summary>
    private void FocusList(int index, ListBox? list = null)
    {
        list ??= List;

        if (list.Items.Count == 0)
        {
            Input.Focus();
            return;
        }

        index = Math.Clamp(index, 0, list.Items.Count - 1);
        list.SelectedIndex = index;
        list.UpdateLayout();

        if (list.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row) row.Focus();
    }

    private void FocusCardNote(TaskCardViewModel card)
    {
        var list = List.Items.Contains(card) ? List : CompletedList;
        list.SelectedIndex = list.Items.IndexOf(card);
        card.IsExpanded = true;
        card.IsNoteRequested = true; // пришли сюда сами — поле показать, даже если заметки ещё нет

        // Следующим проходом: поле могло стать видимым только что и ещё не в дереве.
        Dispatcher.BeginInvoke(() =>
        {
            if (CardElement(card, "NoteEditor") is TextBox editor)
            {
                editor.Focus();
                editor.CaretIndex = editor.Text.Length;
            }
        }, DispatcherPriority.Input);
    }

    private static FrameworkElement? FindDescendant(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } element && element.Name == name) return element;

            if (FindDescendant(child, name) is { } found) return found;
        }

        return null;
    }

    // --- перетаскивание порядка мышью, раздел 9 ---

    private Point _dragOrigin;
    private TaskCardViewModel? _dragCandidate;

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);
        _dragCandidate = CardUnder(e.OriginalSource as DependencyObject);
    }

    /// <summary>
    /// При активном фильтре перетаскивание не начинается вовсе: курсор не меняется, карточка
    /// не поднимается. Позиция вставки была бы неоднозначной из-за скрытых задач — раздел 9.
    /// </summary>
    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        if (_viewModel.IsFiltered) return;

        var shift = e.GetPosition(null) - _dragOrigin;
        if (Math.Abs(shift.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(shift.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var dragged = _dragCandidate;
        _dragCandidate = null;

        DragDrop.DoDragDrop(List, dragged, DragDropEffects.Move);
        ClearDropIndicators();
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        if (e.Data.GetData(typeof(TaskCardViewModel)) is not TaskCardViewModel dragged) return;

        var (target, below) = DropTarget(e.GetPosition(List));
        ClearDropIndicators();

        if (target is not null && target != dragged) target.DropIndicator = below ? DropSide.Below : DropSide.Above;
    }

    private void OnListDragLeave(object sender, DragEventArgs e) => ClearDropIndicators();

    private void OnListDrop(object sender, DragEventArgs e)
    {
        ClearDropIndicators();
        e.Handled = true;

        if (e.Data.GetData(typeof(TaskCardViewModel)) is not TaskCardViewModel dragged) return;

        var (target, below) = DropTarget(e.GetPosition(List));
        int index = target is null
            ? List.Items.Count
            : List.Items.IndexOf(target) + (below ? 1 : 0);

        _viewModel.MoveTo(dragged, index);
    }

    /// <summary>Карточка под точкой и половина, в которую попал курсор: выше или ниже её середины.</summary>
    private (TaskCardViewModel? Card, bool Below) DropTarget(Point point)
    {
        if (List.InputHitTest(point) is not DependencyObject hit) return (null, false);
        if (ItemsControl.ContainerFromElement(List, hit) is not ListBoxItem row) return (null, false);

        double offset = point.Y - row.TranslatePoint(new Point(0, 0), List).Y;
        return (row.DataContext as TaskCardViewModel, offset > row.ActualHeight / 2);
    }

    private TaskCardViewModel? CardUnder(DependencyObject? source) =>
        source is not null && ItemsControl.ContainerFromElement(List, source) is ListBoxItem row
            ? row.DataContext as TaskCardViewModel
            : null;

    private void ClearDropIndicators()
    {
        foreach (var card in _viewModel.Tasks) card.DropIndicator = DropSide.None;
    }

    /// <summary>Клик по строке «Выполнено сегодня» разворачивает и сворачивает блок — раздел 9.</summary>
    private void OnCompletedHeaderClicked(object sender, MouseButtonEventArgs e)
    {
        _viewModel.IsCompletedExpanded = !_viewModel.IsCompletedExpanded;
        e.Handled = true;
    }

    /// <summary>Двойной клик по заголовку открывает правку — таблица «Мышь», раздел 10.</summary>
    private void OnTitleClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if ((sender as FrameworkElement)?.DataContext is not TaskCardViewModel card) return;

        card.BeginEdit();
        e.Handled = true;
    }

    private void OnTitleEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox editor) return;

        Dispatcher.BeginInvoke(() =>
        {
            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Правку завершает не только Enter: клик мимо карточки или уход фокуса из окна — тоже.
    /// Иначе карточка навсегда остаётся однострочным полем и длинный заголовок больше не переносится.
    /// </summary>
    private void OnTitleEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskCardViewModel card) card.CommitEdit();
    }

    /// <summary>
    /// Вставка длиннее 500 символов: первые 500 в заголовок, остаток — в заметку, она раскрывается.
    /// Фокус остаётся в заголовке — раздел 7.
    /// </summary>
    private void OnTitlePasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.SourceDataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        if (e.SourceDataObject.GetData(DataFormats.UnicodeText) is not string pasted) return;

        var merged = Input.Text
            .Remove(Input.SelectionStart, Input.SelectionLength)
            .Insert(Input.SelectionStart, pasted.ReplaceLineEndings(" "));

        if (merged.Length <= MaxTitleLength) return; // обычная вставка, не мешаем

        e.CancelCommand();

        _viewModel.Draft = merged[..MaxTitleLength];
        Input.CaretIndex = MaxTitleLength;

        var tail = merged[MaxTitleLength..].Trim();
        if (tail.Length == 0) return;

        _viewModel.NoteDraft = string.IsNullOrWhiteSpace(_viewModel.NoteDraft)
            ? tail
            : $"{_viewModel.NoteDraft}{Environment.NewLine}{tail}";
        _viewModel.IsNoteOpen = true;
    }

    /// <summary>Палитра общая с окном настроек — она живёт в Theme.</summary>
    private void ApplyThemeBrushes() => Theme.Apply(this, _viewModel.Theme);

    /// <summary>
    /// Показ окна: сначала потолок высоты, потом привязка к углу рабочей области монитора под курсором.
    /// Порядок важен — позиция нижних углов считается от уже известной высоты окна (раздел 5).
    /// </summary>
    public void PlaceOnScreen()
    {
        ApplyHeightCeiling();
        Placement.Place(this, _viewModel.Corner, EdgeMargin);
    }

    /// <summary>
    /// Высота растёт по контенту до потолка, дальше список прокручивается — раздел 5.
    /// Потолок считается на прокручиваемую часть: всё остальное (поле ввода, заметка, тост)
    /// обязано остаться видимым при любом количестве задач.
    /// </summary>
    private void ApplyHeightCeiling()
    {
        double ceiling = Placement.WorkAreaHeight() * _viewModel.MaxHeightShare;

        Scroll.MaxHeight = double.PositiveInfinity;
        UpdateLayout();

        double aroundScroll = ActualHeight - Scroll.ActualHeight;
        Scroll.MaxHeight = Math.Max(MinScrollHeight, ceiling - aroundScroll);
        UpdateLayout();
    }
}
