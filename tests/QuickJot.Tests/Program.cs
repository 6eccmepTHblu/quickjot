using System.IO;
using System.Windows.Input;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Windows.Threading;
using QuickJot.Data;
using QuickJot.ViewModels;

namespace QuickJot.Tests;

/// <summary>
/// Проверки без фреймворка: запускается как обычная программа, падает с кодом 1.
/// Здесь только нетривиальная логика — порядок задач и жизненный цикл записи.
/// </summary>
internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main()
    {
        Run("sort_order выдерживает исчерпание точности", SortOrderSurvivesPrecisionLoss);
        Run("жизненный цикл задачи", TaskLifecycle);
        Run("метки времени возвращаются в UTC", TimestampsStayUtc);
        Run("цикл захвата задачи", CaptureCycle);
        Run("карточка: заметка, важность, удаление", CardActions);
        Run("перестановка и правка заголовка", ReorderAndEdit);
        Run("фильтр и черновик", FilterAndDraft);
        Run("выполнение и блок «Выполнено сегодня»", CompletionFlow);
        Run("сброс блока по локальной полуночи", MidnightRollover);
        Run("отмена и повтор операций", UndoRedo);
        Run("глубина стека отмены", UndoDepth);
        Run("зеркальный порядок при нижних углах", MirroredOrder);
        Run("разбор комбинации хоткея", HotkeyParsing);
        Run("настройки применяются на лету", SettingsReload);
        Run("перетаскивание порядка мышью", DragReorder);
        Run("задачи лежат в самой базе, а не только в -wal", DataLandsInTheDatabaseFile);
        Run("чеклист: формат хранения", SubtaskFormatRoundTrip);
        Run("чеклист: правка, порядок, отмена, фильтр", Checklist);

        Console.WriteLine(_failures == 0 ? "\nвсе проверки прошли" : $"\nпровалено: {_failures}");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Раздел 9: вставка `(a+b)/2` между одной и той же парой соседей вычерпывает double
    /// примерно за 30 шагов. Без перенумерации значения совпадут и порядок сломается молча.
    /// </summary>
    private static void SortOrderSurvivesPrecisionLoss(TaskRepository repo)
    {
        var last = repo.Create("A");   // окажется последним в списке
        var top = repo.Create("B");    // окажется первым

        string below = last.Id;
        for (int i = 0; i < 60; i++)
        {
            var inserted = repo.Create($"T{i}");
            repo.MoveBetween(inserted.Id, top.Id, below);
            below = inserted.Id;
        }

        var list = repo.Active();
        Check(list.Count == 62, $"ожидалось 62 задачи, получено {list.Count}");
        Check(repo.RenumberCount >= 1, "перенумерация ни разу не сработала — проверка ничего не проверила");

        for (int i = 1; i < list.Count; i++)
        {
            Check(list[i].SortOrder > list[i - 1].SortOrder,
                $"порядок сломан на позиции {i}: {list[i - 1].SortOrder} >= {list[i].SortOrder}");
        }

        // Каждая новая задача вставлялась сразу под B, значит T идут в обратном порядке.
        var titles = list.Select(t => t.Title).ToList();
        var expected = new List<string> { "B" };
        for (int i = 59; i >= 0; i--) expected.Add($"T{i}");
        expected.Add("A");
        Check(titles.SequenceEqual(expected), $"порядок не тот:\n  ожидалось {string.Join(",", expected.Take(5))}…\n  получено  {string.Join(",", titles.Take(5))}…");
    }

    private static void TaskLifecycle(TaskRepository repo)
    {
        var task = repo.Create("купить хлеб", "серый, не белый");
        Check(repo.Active().Count == 1, "созданная задача не попала в активные");

        repo.SetFlagged(task.Id, true);
        repo.SetTitle(task.Id, "купить хлеб и молоко");
        var reloaded = repo.Find(task.Id)!;
        Check(reloaded.IsFlagged, "важность не сохранилась");
        Check(reloaded.Title == "купить хлеб и молоко", "заголовок не сохранился");
        Check(reloaded.Notes == "серый, не белый", "заметка не сохранилась");

        repo.Complete(task.Id);
        Check(repo.Active().Count == 0, "выполненная задача осталась в активных");
        Check(repo.CompletedSince(DateTime.UtcNow.AddMinutes(-1)).Count == 1, "выполненной нет в блоке «Выполнено сегодня»");

        repo.Uncomplete(task.Id);
        Check(repo.Active().Count == 1, "возврат из выполненных не сработал");
        Check(Math.Abs(repo.Active()[0].SortOrder - task.SortOrder) < double.Epsilon,
            "задача вернулась не на прежнее место — sort_order изменился");

        repo.Delete(task.Id);
        Check(repo.Active().Count == 0, "удалённая задача осталась в списке");

        Check(repo.PurgeDeletedOlderThan(TimeSpan.FromHours(24)) == 0, "свежее удаление вычищено раньше срока");
        Check(repo.Find(task.Id) is not null, "запись физически удалена раньше суток");

        repo.Restore(task.Id);
        Check(repo.Active().Count == 1, "восстановление не сработало");

        // Полуночная чистка: всё, что удалено раньше среза, уходит из базы физически — раздел 9.
        repo.Delete(task.Id);
        Check(repo.PurgeDeletedOlderThan(TimeSpan.Zero) == 1, "чистка не удалила просроченную запись");
        Check(repo.Find(task.Id) is null, "запись осталась в базе после чистки");
    }

    private static void TimestampsStayUtc(TaskRepository repo)
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var task = repo.Create("метки времени");
        var loaded = repo.Find(task.Id)!;

        Check(loaded.CreatedAt.Kind == DateTimeKind.Utc, $"created_at приехал как {loaded.CreatedAt.Kind}, а не Utc");
        Check(loaded.CreatedAt > before && loaded.CreatedAt < DateTime.UtcNow.AddSeconds(1),
            $"created_at вне разумного диапазона: {loaded.CreatedAt:O}, ожидался интервал {before:O} .. {DateTime.UtcNow.AddSeconds(1):O}, в памяти было {task.CreatedAt:O}");
    }

    /// <summary>Этап 2: поле ввода создаёт задачу, новая встаёт наверх, чекбокс убирает её из списка.</summary>
    private static void CaptureCycle(TaskRepository repo)
    {
        var vm = new MainViewModel(repo);

        vm.Draft = "   ";
        Check(!vm.Create(), "пустой заголовок создал задачу");
        Check(vm.Tasks.Count == 0, "в списке что-то появилось из пустого поля");

        vm.Draft = "первая";
        Check(vm.Create(), "задача не создалась");
        vm.Draft = "  вторая  ";
        Check(vm.Create(), "вторая задача не создалась");

        Check(vm.Draft.Length == 0, "поле ввода не очистилось после создания");
        Check(vm.Tasks[0].Title == "вторая", "новая задача не встала в начало списка");
        Check(vm.Tasks[1].Title == "первая", "порядок списка нарушен");
        Check(vm.Tasks[0].IsNew, "новая задача не подсвечена");

        // Перезапуск приложения: список читается из базы в том же порядке.
        var reopened = new MainViewModel(repo);
        reopened.Load();
        Check(reopened.Tasks.Select(t => t.Title).SequenceEqual(["вторая", "первая"]),
            "после перезагрузки порядок другой");

        reopened.Tasks[0].IsCompleted = true;
        Pump(TimeSpan.FromSeconds(2)); // задержка перед уходом в блок — раздел 9
        Check(reopened.Tasks.Count == 1, "выполненная задача осталась в списке");
        Check(repo.Active().Count == 1, "выполнение не записалось в базу");
        Check(repo.CompletedSince(DateTime.UtcNow.AddMinutes(-1))[0].Title == "вторая", "выполнена не та задача");
    }

    /// <summary>Этап 3: заметка при создании, важность, разворот, мягкое удаление.</summary>
    private static void CardActions(TaskRepository repo)
    {
        var vm = new MainViewModel(repo);

        vm.Draft = "задача с заметкой";
        vm.NoteDraft = "  подробности  ";
        vm.IsNoteOpen = true;
        Check(vm.Create(), "задача не создалась");
        Check(!vm.IsNoteOpen, "поле заметки осталось раскрытым после создания");
        Check(vm.NoteDraft.Length == 0, "поле заметки не очистилось");

        var card = vm.Tasks[0];
        Check(card.Notes == "подробности", $"заметка не сохранилась: «{card.Notes}»");
        Check(card.HasNotes, "карточка не считает, что заметка есть");

        card.ToggleFlagCommand.Execute(null);
        card.IsExpanded = !card.IsExpanded;
        Check(card.IsFlagged, "важность не переключилась");
        Check(card.IsExpanded, "заметка не развернулась");

        // Состояние важности и разворота помнится между открытиями окна — разделы 8 и 12.
        var reopened = new MainViewModel(repo);
        reopened.Load();
        Check(reopened.Tasks[0].IsFlagged, "важность не дожила до перезапуска");
        Check(reopened.Tasks[0].IsExpanded, "разворот заметки не дожил до перезапуска");

        // Правка заметки прямо в карточке.
        reopened.Tasks[0].Notes = "другой текст";
        Check(repo.Find(card.Id)!.Notes == "другой текст", "правка заметки не записалась в базу");

        reopened.Tasks[0].DeleteCommand.Execute(null);
        Check(reopened.Tasks.Count == 0, "удалённая карточка осталась в списке");
        Check(repo.Active().Count == 0, "удалённая задача осталась активной");
        Check(repo.Find(card.Id) is not null, "удаление оказалось жёстким, а должно быть мягким");
    }

    /// <summary>Этап 4: Alt+↑↓ и правка заголовка на месте — раздел 10.</summary>
    private static void ReorderAndEdit(TaskRepository repo)
    {
        var vm = new MainViewModel(repo);
        foreach (var title in new[] { "первая", "вторая", "третья" })
        {
            vm.Draft = title;
            vm.Create();
        }

        // Новые уходят наверх, поэтому порядок обратный вводу.
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(["третья", "вторая", "первая"]), "исходный порядок не тот");

        var last = vm.Tasks[2];
        vm.MoveUp(last);
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(["третья", "первая", "вторая"]), "Alt+↑ переставил не так");

        vm.MoveUp(last);
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(["первая", "третья", "вторая"]), "второй Alt+↑ переставил не так");

        vm.MoveUp(last); // уже наверху — ничего не меняется
        vm.MoveDown(vm.Tasks[2]); // уже внизу — тоже
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(["первая", "третья", "вторая"]), "перестановка на краю списка сдвинула что-то");

        var reopened = new MainViewModel(repo);
        reopened.Load();
        Check(reopened.Tasks.Select(t => t.Title).SequenceEqual(["первая", "третья", "вторая"]),
            "порядок не пережил перезапуск");

        // Правка: Enter сохраняет.
        var card = reopened.Tasks[0];
        card.BeginEdit();
        Check(card.EditTitle == "первая", "правка началась не с текущего заголовка");
        card.EditTitle = "  первая исправленная  ";
        card.CommitEdit();
        Check(!card.IsEditing, "правка не закрылась");
        Check(repo.Find(card.Id)!.Title == "первая исправленная", "правка не записалась в базу");

        // Правка: Esc откатывает.
        card.BeginEdit();
        card.EditTitle = "мусор";
        card.CancelEdit();
        Check(card.Title == "первая исправленная", "Esc не откатил заголовок");
        Check(repo.Find(card.Id)!.Title == "первая исправленная", "Esc всё-таки записал изменение в базу");

        // Пустой заголовок правкой не сохраняется.
        card.BeginEdit();
        card.EditTitle = "   ";
        card.CommitEdit();
        Check(card.Title == "первая исправленная", "пустой заголовок затёр существующий");
    }

    /// <summary>Этап 5: поле ввода одновременно создаёт и фильтрует — раздел 7.</summary>
    private static void FilterAndDraft(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);
        foreach (var title in new[] { "купить хлеб", "позвонить в банк", "КУПИТЬ билеты" })
        {
            vm.Draft = title;
            vm.Create();
        }

        Check(vm.VisibleTasks.Count() == 3, "полный список показывает не всё");
        Check(!vm.IsListEmpty, "непустой список считается пустым");

        vm.Draft = "купить";
        Check(vm.VisibleTasks.Count() == 2, "фильтр не регистронезависимый или не работает");
        Check(vm.Tasks.Count == 3, "фильтр удалил задачи вместо того, чтобы их скрыть");

        vm.Draft = "  купить  ";
        Check(vm.VisibleTasks.Count() == 2, "пробелы по краям запроса ломают фильтр");

        vm.Draft = "чего тут нет";
        Check(vm.IsListEmpty, "список должен быть пустым");
        Check(vm.EmptyMessage.Contains("Enter"), "нет подсказки «Enter — создать»");

        // Enter создаёт задачу всегда, даже при точном совпадении — раздел 7.
        vm.Draft = "купить хлеб";
        Check(vm.VisibleTasks.Count() == 1, "точное совпадение не отфильтровалось");
        vm.Create();
        Check(vm.Draft.Length == 0, "поле не очистилось");
        Check(vm.VisibleTasks.Count() == 4, "после создания список не вернулся к полному");
        Check(repo.Active().Count(t => t.Title == "купить хлеб") == 2, "дубль при точном совпадении не создался");

        // При активном фильтре перестановка заблокирована — раздел 9.
        vm.Draft = "купить";
        var order = vm.Tasks.Select(t => t.Title).ToList();
        vm.MoveDown(vm.Tasks[0]);
        vm.MoveUp(vm.Tasks[^1]);
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(order), "перестановка сработала при активном фильтре");

        // Черновик переживает закрытие окна вместе с заметкой — раздел 7.
        vm.Draft = "недописанное";
        vm.NoteDraft = "и заметка";
        vm.SaveDraft();

        var reopened = new MainViewModel(repo, settings);
        reopened.Load();
        Check(reopened.Draft == "недописанное", "черновик заголовка не восстановился");
        Check(reopened.NoteDraft == "и заметка", "черновик заметки не восстановился");
        Check(reopened.IsNoteOpen, "непустая заметка должна быть раскрыта");
        Check(reopened.VisibleTasks.Count() == 0, "восстановленный черновик обязан фильтровать список");
    }

    /// <summary>Этап 6: задержка 1.5 с, блок «Выполнено сегодня», возврат на прежнее место — раздел 9.</summary>
    private static void CompletionFlow(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);
        foreach (var title in new[] { "первая", "вторая" })
        {
            vm.Draft = title;
            vm.Create();
        }

        var card = vm.Tasks[1]; // «первая», она внизу
        card.IsCompleted = true;

        Check(repo.Active().Count == 1, "выполнение не записалось в базу сразу");
        Check(vm.Tasks.Contains(card), "карточка уехала из списка, не отстояв 1.5 с");
        Check(vm.CompletedToday.Count == 0, "карточка попала в блок раньше времени");

        Pump(TimeSpan.FromSeconds(2));

        Check(!vm.Tasks.Contains(card), "карточка не уехала в блок");
        Check(vm.CompletedToday.Count == 1, "блок пуст");
        Check(vm.HasCompleted && vm.CompletedHeader.Contains("· 1"), $"заголовок блока не тот: {vm.CompletedHeader}");

        // Возврат: задача встаёт на прежнее место, sort_order не менялся.
        card.IsCompleted = false;
        Check(vm.CompletedToday.Count == 0, "карточка осталась в блоке");
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(["вторая", "первая"]), "задача вернулась не на прежнее место");
        Check(repo.Active().Count == 2, "возврат не записался в базу");

        // Галочку сняли раньше, чем истекли 1.5 с — задача никуда не едет.
        card.IsCompleted = true;
        card.IsCompleted = false;
        Pump(TimeSpan.FromSeconds(2));
        Check(vm.CompletedToday.Count == 0, "задача уехала в блок после того, как галочку сняли");
        Check(vm.Tasks.Count == 2, "задача потерялась между списком и блоком");
    }

    /// <summary>Этап 6: блок сбрасывается по локальной полуночи — раздел 9.</summary>
    private static void MidnightRollover(TaskRepository repo, SettingsStore settings)
    {
        var clock = DateTime.Now;
        var vm = new MainViewModel(repo, settings, () => clock);

        vm.Draft = "сегодняшняя";
        vm.Create();
        vm.Tasks[0].IsCompleted = true;
        Pump(TimeSpan.FromSeconds(2));
        Check(vm.CompletedToday.Count == 1, "задача не попала в блок");

        clock = clock.AddDays(1); // приложение проработало всю ночь
        vm.CheckDayRollover();

        Check(vm.CompletedToday.Count == 0, "блок не сбросился по полуночи");
        Check(!vm.HasCompleted, "блок остался видимым");
        Check(repo.CompletedSince(DateTime.UtcNow.AddDays(-1)).Count == 1,
            "выполненная задача пропала из базы, а она нужна для будущей статистики");
    }

    /// <summary>Этап 7: каждая операция знает обратное действие — раздел 11.</summary>
    private static void UndoRedo(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);

        // Создание → удаление.
        vm.Draft = "первая";
        vm.Create();
        var card = vm.Tasks[0];
        vm.UndoCommand.Execute(null);
        Check(vm.Tasks.Count == 0, "созданная задача не убралась при отмене");
        Check(repo.Active().Count == 0, "отмена создания не дошла до базы");

        vm.RedoCommand.Execute(null);
        Check(vm.Tasks.Count == 1, "повтор создания не вернул задачу");
        Check(repo.Active().Count == 1, "повтор создания не дошёл до базы");

        // Правка заголовка → прежний текст.
        card.BeginEdit();
        card.EditTitle = "исправленная";
        card.CommitEdit();
        vm.UndoCommand.Execute(null);
        Check(card.Title == "первая", $"отмена правки дала «{card.Title}»");
        Check(repo.Find(card.Id)!.Title == "первая", "отмена правки не дошла до базы");

        // Важность → прежнее значение.
        card.ToggleFlagCommand.Execute(null);
        Check(card.IsFlagged, "важность не включилась");
        vm.UndoCommand.Execute(null);
        Check(!card.IsFlagged, "отмена важности не сработала");
        Check(!repo.Find(card.Id)!.IsFlagged, "отмена важности не дошла до базы");

        // Удаление → восстановление.
        card.DeleteCommand.Execute(null);
        Check(vm.Tasks.Count == 0, "задача не удалилась");
        Check(vm.Toast is not null, "тост после удаления не показался");
        vm.UndoCommand.Execute(null);
        Check(vm.Tasks.Count == 1, "отмена удаления не вернула задачу");
        Check(repo.Active().Count == 1, "отмена удаления не дошла до базы");

        // Выполнение → снятие.
        card.IsCompleted = true;
        Pump(TimeSpan.FromSeconds(2));
        Check(vm.CompletedToday.Count == 1, "задача не уехала в блок");
        vm.UndoCommand.Execute(null);
        Check(vm.CompletedToday.Count == 0 && vm.Tasks.Count == 1, "отмена выполнения не вернула задачу в список");
        Check(repo.Active().Count == 1, "отмена выполнения не дошла до базы");

        // Перестановка → прежний sort_order.
        vm.Draft = "вторая";
        vm.Create();
        var order = vm.Tasks.Select(t => t.Title).ToList();
        vm.MoveDown(vm.Tasks[0]);
        Check(!vm.Tasks.Select(t => t.Title).SequenceEqual(order), "перестановка не сработала");
        vm.UndoCommand.Execute(null);
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(order), "отмена перестановки не вернула порядок");

        var reopened = new MainViewModel(repo, settings);
        reopened.Load();
        Check(reopened.Tasks.Select(t => t.Title).SequenceEqual(order), "порядок после отмены не дожил до перезапуска");
    }

    /// <summary>Этап 7: стек на 20 операций, дальше самые старые выпадают — раздел 11.</summary>
    private static void UndoDepth(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);
        for (int i = 0; i < 25; i++)
        {
            vm.Draft = $"задача {i}";
            vm.Create();
        }

        for (int i = 0; i < 25; i++) vm.UndoCommand.Execute(null);

        Check(vm.Tasks.Count == 5, $"после 25 отмен осталось {vm.Tasks.Count} задач вместо 5");
        Check(repo.Active().Count == 5, "база разошлась со списком");
    }

    /// <summary>
    /// Этап 8: при нижних углах список растёт вверх, и новая задача обязана оказаться
    /// вплотную к полю ввода, то есть последней в порядке показа — раздел 5.
    /// </summary>
    private static void MirroredOrder(TaskRepository repo, SettingsStore settings)
    {
        var top = new MainViewModel(repo, settings);
        foreach (var title in new[] { "первая", "вторая", "третья" })
        {
            top.Draft = title;
            top.Create();
        }

        Check(!top.IsMirrored, "верхний правый угол не должен зеркалить");
        Check(top.VisibleTasks.Select(t => t.Title).SequenceEqual(["третья", "вторая", "первая"]),
            "при верхних углах новая задача должна быть первой в списке");

        settings.Set("window.corner", nameof(AnchorCorner.BottomRight));
        var bottom = new MainViewModel(repo, settings);
        bottom.Load();

        Check(bottom.IsMirrored, "нижний угол не включил зеркалирование");
        Check(bottom.VisibleTasks.Select(t => t.Title).SequenceEqual(["первая", "вторая", "третья"]),
            "при нижних углах порядок показа должен быть обратным");

        bottom.Draft = "четвёртая";
        bottom.Create();
        Check(bottom.VisibleTasks.Last().Title == "четвёртая",
            "новая задача не встала вплотную к полю ввода");

        // Ширина и угол переживают перезапуск — раздел 5.
        bottom.SaveWindowWidth(720);
        var reopened = new MainViewModel(repo, settings);
        Check(Math.Abs(reopened.WindowWidth - 720) < 0.5, $"ширина не сохранилась: {reopened.WindowWidth}");
        Check(reopened.Corner == AnchorCorner.BottomRight, "угол привязки не сохранился");
    }

    /// <summary>Этап 9: комбинация хранится строкой, а регистрируется числами — раздел 4.</summary>
    private static void HotkeyParsing(TaskRepository repo, SettingsStore settings)
    {
        Check(Hotkey.Parse("Ctrl+Alt+Space").ToString() == "Ctrl+Alt+Space", "разбор и печать разошлись");
        Check(Hotkey.Parse("ctrl+shift+j").ToString() == "Ctrl+Shift+J", "регистр в разборе не игнорируется");
        Check(Hotkey.Parse(null) == Hotkey.Default, "пустая настройка должна давать комбинацию по умолчанию");
        Check(Hotkey.Parse("мусор") == Hotkey.Default, "мусор должен откатываться к комбинации по умолчанию");

        // Комбинация без модификатора отобрала бы клавишу у всей системы.
        Check(Hotkey.Parse("Space") == Hotkey.Default, "комбинация без модификатора принята");
        Check(!new Hotkey(ModifierKeys.Control, Key.LeftShift).IsValid, "модификатор в роли основной клавиши принят");

        var hotkey = Hotkey.Parse("Ctrl+Alt+Space");
        Check(hotkey.NativeModifiers == 0x0003, "модификаторы для RegisterHotKey не те (MOD_ALT | MOD_CONTROL)");
        Check(hotkey.VirtualKey == 0x20, $"код клавиши не тот: 0x{hotkey.VirtualKey:X}");
    }

    /// <summary>Этап 9: окно настроек пишет в базу, модель перечитывает — раздел 13.</summary>
    private static void SettingsReload(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);
        foreach (var title in new[] { "первая", "вторая" })
        {
            vm.Draft = title;
            vm.Create();
        }

        Check(vm.TitleLines == 2 && vm.Corner == AnchorCorner.TopRight, "значения по умолчанию не те");
        Check(vm.VisibleTasks.First().Title == "вторая", "исходный порядок не тот");

        settings.SetDouble(SettingKeys.TitleLines, 4);
        settings.SetDouble(SettingKeys.TitleSize, 16);
        settings.Set(SettingKeys.Corner, nameof(AnchorCorner.BottomLeft));
        settings.SetDouble(SettingKeys.Width, 700);
        settings.Set(SettingKeys.Theme, "dark");
        settings.SetBool(SettingKeys.Animations, false);
        vm.ReloadSettings();

        Check(vm.TitleLines == 4, "лимит строк не перечитался");
        Check(Math.Abs(vm.TitleMaxHeight - 4 * Math.Round(16 * 1.35)) < 0.5, $"потолок заголовка не тот: {vm.TitleMaxHeight}");
        Check(vm.IsMirrored, "угол привязки не перечитался");
        Check(vm.VisibleTasks.First().Title == "первая", "порядок показа не перевернулся вслед за углом");
        Check(Math.Abs(vm.WindowWidth - 700) < 0.5, "ширина не перечиталась");
        Check(vm.Theme == "dark" && !vm.AnimationsEnabled, "тема или анимации не перечитались");

        // Потолок чеклиста считается целыми строками, иначе прокрутка режет пункт пополам.
        Check(vm.SubtaskLines == 6, "по умолчанию чеклист показывает не 6 пунктов");
        double rowHeight = vm.SubtaskListMaxHeight / vm.SubtaskLines;

        settings.SetDouble(SettingKeys.SubtaskLines, 4);
        vm.ReloadSettings();
        Check(vm.SubtaskLines == 4, "число видимых пунктов не перечиталось");
        Check(Math.Abs(vm.SubtaskListMaxHeight - 4 * rowHeight) < 0.001,
            $"потолок чеклиста не равен четырём строкам: {vm.SubtaskListMaxHeight}");

        // Значения из базы могут быть любыми: настройки правит и человек, и предыдущая версия.
        settings.SetDouble(SettingKeys.TitleLines, 9);
        settings.SetDouble(SettingKeys.Width, 99999);
        settings.SetDouble(SettingKeys.MaxHeightShare, 5);
        settings.SetDouble(SettingKeys.SubtaskLines, 99);
        vm.ReloadSettings();

        Check(vm.SubtaskLines == 20, "число видимых пунктов не ограничено сверху");
        Check(vm.TitleLines == 4, "лимит строк не ограничен сверху");
        Check(Math.Abs(vm.WindowWidth - 1200) < 0.5, "ширина не ограничена сверху");
        Check(Math.Abs(vm.MaxHeightShare - 0.95) < 0.001, "потолок высоты не ограничен сверху");
    }

    /// <summary>Этап 10: drag&amp;drop переставляет по порядку показа, а sort_order считается по каноническому.</summary>
    private static void DragReorder(TaskRepository repo, SettingsStore settings)
    {
        var vm = new MainViewModel(repo, settings);
        foreach (var title in new[] { "первая", "вторая", "третья" })
        {
            vm.Draft = title;
            vm.Create();
        }

        var visible = vm.VisibleTasks.ToList(); // третья, вторая, первая
        vm.MoveTo(visible[0], 3); // утащили верхнюю в самый низ
        Check(vm.VisibleTasks.Select(t => t.Title).SequenceEqual(["вторая", "первая", "третья"]),
            "перетаскивание вниз переставило не так");

        vm.MoveTo(vm.VisibleTasks.Last(), 0); // и обратно наверх
        Check(vm.VisibleTasks.Select(t => t.Title).SequenceEqual(["третья", "вторая", "первая"]),
            "перетаскивание вверх переставило не так");

        vm.UndoCommand.Execute(null);
        Check(vm.VisibleTasks.Select(t => t.Title).SequenceEqual(["вторая", "первая", "третья"]),
            "отмена перетаскивания не вернула порядок");

        // При активном фильтре позиция вставки неоднозначна — перетаскивание запрещено (раздел 9).
        vm.Draft = "вт";
        var order = vm.Tasks.Select(t => t.Title).ToList();
        vm.MoveTo(vm.VisibleTasks.First(), 0);
        Check(vm.Tasks.Select(t => t.Title).SequenceEqual(order), "перетаскивание сработало при активном фильтре");
        vm.Draft = "";

        // При нижних углах список показывается перевёрнутым, а sort_order обязан остаться каноническим.
        settings.Set(SettingKeys.Corner, nameof(AnchorCorner.BottomRight));
        var mirrored = new MainViewModel(repo, settings);
        mirrored.Load();

        var bottomCard = mirrored.VisibleTasks.First();
        mirrored.MoveTo(bottomCard, 3); // визуально в самый низ, то есть вплотную к полю ввода
        Check(mirrored.VisibleTasks.Last() == bottomCard, "в зеркальном списке карточка уехала не туда");

        var reopened = new MainViewModel(repo, settings);
        reopened.Load();
        Check(reopened.VisibleTasks.Last().Title == bottomCard.Title, "порядок после перетаскивания не дожил до перезапуска");
    }

    /// <summary>Прокрутка очереди диспетчера: без неё DispatcherTimer в консоли не тикает.</summary>
    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // --- инфраструктура проверок ---

    private static void Run(string name, Action<TaskRepository> test) => Run(name, (repo, _) => test(repo));

    /// <summary>Для проверок со своим жизненным циклом базы: общий Run открывает её сам.</summary>
    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"OK   {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}\n     {ex.Message}");
        }
    }

    private static void Run(string name, Action<TaskRepository, SettingsStore> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quickjot-test-{Guid.NewGuid():N}.db");
        try
        {
            using var db = Db.Open(path);
            test(new TaskRepository(db), new SettingsStore(db));
            Console.WriteLine($"OK   {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}\n     {ex.Message}");
        }
        finally
        {
            SqliteConnectionCleanup(path);
        }
    }

    /// <summary>
    /// Формат чеклиста — раздел 8. Строка без метки тоже читается как пункт: базу могли править руками,
    /// и молча терять такие строки нельзя.
    /// </summary>
    private static void SubtaskFormatRoundTrip()
    {
        var parsed = SubtaskFormat.Parse("[x] сделано\n[ ] не сделано\n\n  голая строка  \n");
        Check(parsed.Count == 3, $"разобрано пунктов: {parsed.Count}, ожидалось 3");
        Check(parsed[0] is { Done: true, Title: "сделано" }, "выполненный пункт разобран не так");
        Check(parsed[1] is { Done: false, Title: "не сделано" }, "невыполненный пункт разобран не так");
        Check(parsed[2] is { Done: false, Title: "голая строка" }, "строка без метки потерялась");

        Check(SubtaskFormat.Format(parsed) == "[x] сделано\n[ ] не сделано\n[ ] голая строка",
            "обратная сборка дала не тот текст");

        Check(SubtaskFormat.Format([]) is null, "пустой чеклист должен быть NULL, а не пустой строкой");
        Check(SubtaskFormat.Format([new Subtask(false, "   ")]) is null, "пункт из пробелов должен отбрасываться");
        Check(SubtaskFormat.Parse(null).Count == 0, "NULL должен читаться как пустой чеклист");
    }

    /// <summary>
    /// Чеклист целиком: добавление, выполнение, порядок, запись в базу, отмена и поиск по пунктам.
    /// </summary>
    private static void Checklist(TaskRepository repo)
    {
        var vm = new MainViewModel(repo);
        vm.Draft = "большая задача";
        vm.Create();

        var card = vm.Tasks[0];
        card.AddSubtask("собрать список полей");
        card.AddSubtask("согласовать с Димой");
        card.AddSubtask("выложить");

        Check(card.Subtasks.Count == 3 && card.SubtasksDone == 0, "пункты добавились не так");
        Check(repo.Find(card.Id)!.Subtasks == "[ ] собрать список полей\n[ ] согласовать с Димой\n[ ] выложить",
            "чеклист не записался в базу");

        card.Subtasks[0].ToggleCommand.Execute(null);
        Check(card.SubtasksDone == 1, "выполнение пункта не дошло до карточки");

        // Пункты переставляются позиционно: sort_order тут не нужен.
        card.MoveSubtask(card.Subtasks[2], -1);
        Check(card.Subtasks.Select(s => s.Title).SequenceEqual(["собрать список полей", "выложить", "согласовать с Димой"]),
            "Alt+↑ переставил пункт не так");

        // Отмена возвращает прежний чеклист целиком — раздел 11.
        vm.UndoCommand.Execute(null);
        Check(card.Subtasks.Select(s => s.Title).SequenceEqual(["собрать список полей", "согласовать с Димой", "выложить"]),
            "отмена не вернула прежний порядок");
        Check(repo.Find(card.Id)!.Subtasks!.Contains("[x] собрать"), "отмена перестановки сбросила выполнение");

        card.Subtasks[1].DeleteCommand.Execute(null);
        Check(card.Subtasks.Count == 2, "удаление пункта не дошло до карточки");

        vm.UndoCommand.Execute(null);
        Check(card.Subtasks.Count == 3 && card.SubtasksDone == 1, "отмена не вернула удалённый пункт");

        // Полностью закрытый чеклист саму задачу не выполняет.
        foreach (var subtask in card.Subtasks) subtask.IsDone = true;
        Check(!card.IsCompleted, "чеклист не должен закрывать саму задачу");

        // Поиск идёт и по пунктам: в большой задаче нужное чаще всего именно там.
        vm.Draft = "Димой";
        Check(vm.VisibleTasks.Contains(card), "фильтр не нашёл задачу по тексту пункта");

        vm.Draft = "нет такого текста";
        Check(!vm.VisibleTasks.Contains(card), "фильтр показывает задачу без совпадений");

        vm.Draft = "";
        var reopened = new MainViewModel(repo);
        reopened.Load();
        Check(reopened.Tasks[0].SubtasksDone == 3, "чеклист не пережил перезапуск");
    }

    /// <summary>
    /// Запись должна оказываться в самой tasks.db, а не только в файле-спутнике tasks.db-wal.
    /// Пока спутник не слит, достаточно SQLite один раз счесть его неактуальным — и приложение
    /// открывается с пустым списком поверх целых задач. Проверяется на копии одной только базы,
    /// без спутников: ровно то, что видно, если спутник потерян.
    /// </summary>
    private static void DataLandsInTheDatabaseFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quickjot-wal-{Guid.NewGuid():N}.db");
        var copy = path + ".copy";
        try
        {
            using (var db = Db.Open(path))
            {
                new TaskRepository(db).Create("задача, пережившая потерю спутника");
                File.Copy(path, copy, overwrite: true); // соединение ещё открыто — как при снятом процессе
            }

            using var reopened = new SqliteConnection($"Data Source={copy};Mode=ReadOnly");
            reopened.Open();
            Check(reopened.QuerySingle<int>("select count(*) from tasks") == 1,
                "задача осталась только в -wal: сама база пуста");
        }
        finally
        {
            SqliteConnectionCleanup(path);
            SqliteConnectionCleanup(copy);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void SqliteConnectionCleanup(string path)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { /* временный файл, не критично */ }
        }
    }
}
