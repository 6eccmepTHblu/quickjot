using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Data.Sqlite;
using QuickJot.Data;
using QuickJot.ViewModels;

namespace QuickJot;

public partial class App : Application
{
    /// <summary>
    /// Прогон на dev-базе получает свои имена: иначе он упирается в единственный экземпляр
    /// и вместо себя показывает рабочее приложение — вплоть до чужого окна поверх чужих данных.
    /// </summary>
    private static readonly string Suffix =
        Environment.GetEnvironmentVariable("QUICKJOT_DEV") is null ? "" : ".dev";

    private static readonly string MutexName = $@"Local\QuickJot.SingleInstance{Suffix}";
    private static readonly string ShowEventName = $@"Local\QuickJot.ShowWindow{Suffix}";

    private Mutex? _instanceLock;
    private SettingsWindow? _settingsWindow;
    private EventWaitHandle? _showSignal;
    private SqliteConnection? _db;
    private TaskbarIcon? _tray;
    private HotkeyService? _hotkey;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private readonly WindowActivator _activator = new();

    public TaskRepository Tasks { get; private set; } = null!;
    public SettingsStore Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Служебный запуск через UAC: создать задачу в Планировщике и сразу выйти — раздел 3.
        // Разбирается до мьютекса, иначе элевированный процесс упрётся в основной экземпляр.
        if (e.Args is ["--autostart", var mode])
        {
            Autostart.ApplyFromElevatedHelper(mode);
            Shutdown();
            return;
        }

        _instanceLock = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Второй запуск не показывает своё окно, а будит первый экземпляр и уходит — раздел 3.
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var running))
            {
                running.Set();
                running.Dispose();
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) => Log.Write($"UNHANDLED (dispatcher): {args.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"UNHANDLED (domain): {args.ExceptionObject}");

        // QUICKJOT_DEV=<путь к базе> — прогон интерфейса на отдельной базе, не трогая рабочую.
        var devDatabase = Environment.GetEnvironmentVariable("QUICKJOT_DEV");

        // Бэкап кладётся рядом с открытой базой: иначе прогон на dev-базе вытесняет своими
        // пустыми копиями настоящие бэкапы рабочей.
        var dbPath = devDatabase ?? Db.DefaultDbPath;
        _db = Db.Open(dbPath);
        Db.Backup(_db, Db.BackupsDirFor(dbPath));
        Tasks = new TaskRepository(_db);
        Settings = new SettingsStore(_db);

        if (devDatabase is not null) PrepareDevMode();

        ApplyTheme();
        WarmUpWindow();
        CreateTray();
        if (devDatabase is null) RegisterHotkey(); // хоткей занят рабочим экземпляром — прогон за него не дерётся
        ListenForSecondInstance();

        // Путь к базе — в лог. У приложений, запущенных из контейнера упакованного приложения,
        // %APPDATA% перенаправлен в его песочницу: тот же exe работает с другой базой и показывает
        // пустой список. Без этой строки такое видно только по косвенным признакам.
        Log.Write($"старт: окно прогрето, приложение в трее · база {dbPath}");

        if (devDatabase is not null)
        {
            // QUICKJOT_DEV_FILTER — прогон отфильтрованного состояния без эмуляции клавиатуры.
            var query = Environment.GetEnvironmentVariable("QUICKJOT_DEV_FILTER");
            if (!string.IsNullOrEmpty(query) && _viewModel is not null) _viewModel.Draft = query;

            ShowWindow();

            // QUICKJOT_DEV_SETTINGS=1 — сразу открыть настройки: из скрипта до них иначе не добраться,
            // они открываются только из меню в трее.
            if (Environment.GetEnvironmentVariable("QUICKJOT_DEV_SETTINGS") == "1") ShowSettings();
        }
    }

    /// <summary>
    /// Только для прогона интерфейса. Ошибки привязок WPF иначе не видит никто: они уходят в отладочный
    /// вывод, а не в исключения, и битая карточка выглядит просто пустой.
    /// </summary>
    private void PrepareDevMode()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new LogTraceListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        // QUICKJOT_DEV_CORNER=BottomRight и т.п. — прогон зеркальной раскладки до окна настроек.
        var corner = Environment.GetEnvironmentVariable("QUICKJOT_DEV_CORNER");
        if (!string.IsNullOrEmpty(corner)) Settings.Set("window.corner", corner);

        if (Tasks.Active().Count > 0) return;

        Tasks.Create("короткая задача");
        var withNote = Tasks.Create("задача с заметкой", $"первая строка{Environment.NewLine}вторая строка");
        Tasks.SetExpanded(withNote.Id, true);

        var flagged = Tasks.Create("важная задача с очень длинным заголовком, который обязан упереться " +
                                   "в лимит строк и закончиться многоточием, чтобы было видно, как это выглядит");
        Tasks.SetFlagged(flagged.Id, true);

        var big = Tasks.Create("задача с чеклистом", "описание задачи целиком");
        Tasks.SetSubtasks(big.Id, string.Join('\n',
            "[x] собрать список полей",
            "[x] согласовать с Димой",
            "[ ] описать поле «Радар»",
            "[ ] примеры заполнения",
            "[ ] выложить в общий доступ"));
        Tasks.SetExpanded(big.Id, true);
    }

    /// <summary>
    /// Первый рендер WPF стоит около 200 мс (замер этапа 0) и не должен попадать на первое нажатие
    /// хоткея. Окно создаётся и прячется сразу, за пределами экрана, чтобы не мигнуть на старте — раздел 3.
    /// </summary>
    private void WarmUpWindow()
    {
        _viewModel = new MainViewModel(Tasks, Settings);
        _viewModel.Load();

        _window = new MainWindow(_viewModel) { Left = -30000, Top = -30000 };
        _window.HideRequested += HideWindow;
        _window.Show();
        _window.Hide();
    }

    private void CreateTray()
    {
        var show = new MenuItem { Header = "Показать" };
        show.Click += (_, _) => ShowWindow();

        var settings = new MenuItem { Header = "Настройки" };
        settings.Click += (_, _) => ShowSettings();

        var exit = new MenuItem { Header = "Выход" };
        exit.Click += (_, _) => Shutdown();

        // Показать · Настройки · Выход — раздел 14.
        var menu = new ContextMenu();
        menu.Items.Add(show);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        _tray = new TaskbarIcon
        {
            ToolTipText = "QuickJot",
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
            ContextMenu = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new SimpleCommand(ShowWindow),
        };
        _tray.ForceCreate();
    }

    private void RegisterHotkey()
    {
        _hotkey = new HotkeyService();
        _hotkey.Pressed += OnHotkeyPressed;

        var hotkey = Hotkey.Parse(Settings.Get(SettingKeys.Hotkey));
        if (TryRegisterHotkey(hotkey)) return;

        // Комбинация занята — раздел 4: говорим об этом и открываем экран настройки хоткея.
        Log.Write($"RegisterHotKey вернул false: {hotkey} занята другой программой");
        _tray?.ShowNotification("QuickJot", $"Комбинация {hotkey} занята другой программой");
        ShowSettings($"{hotkey} занята другой программой — задайте другую");
    }

    private bool TryRegisterHotkey(Hotkey hotkey) =>
        _hotkey is not null && _hotkey.Register(hotkey.NativeModifiers, hotkey.VirtualKey);

    /// <summary>Окно настроек — раздел 13. Оно одно на приложение, второй раз просто поднимается.</summary>
    private void ShowSettings(string? hotkeyError = null)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(Settings)
        {
            TryRegisterHotkey = TryRegisterHotkey,
            HotkeyError = hotkeyError, // окно открылось само, потому что комбинация занята — раздел 4
        };
        viewModel.Changed += ApplySettings;

        _settingsWindow = new SettingsWindow(viewModel);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettings()
    {
        _viewModel?.ReloadSettings();
        ApplyTheme();
        _window?.ApplySettings();
    }

    /// <summary>Тема: системная, светлая или тёмная — раздел 13.</summary>
    private void ApplyTheme()
    {
        ThemeMode = Settings.Get(SettingKeys.Theme) switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    /// <summary>Три состояния из раздела 4.</summary>
    private void OnHotkeyPressed()
    {
        if (_window is null) return;

        if (!_window.IsVisible)
        {
            ShowWindow();
        }
        else if (!_activator.IsForeground(_window))
        {
            _activator.ShowAndFocus(_window, _window.InputField); // фокус вернуть, окно не прятать
        }
        else
        {
            HideWindow();
        }
    }

    /// <summary>Черновик поля ввода переживает скрытие окна — раздел 7.</summary>
    private void HideWindow()
    {
        if (_window is null) return;

        _viewModel?.SaveDraft();
        _viewModel?.SaveWindowWidth(_window.Width); // ширину меняют мышью — раздел 5
        _activator.HideAndRestore(_window);
    }

    private void ShowWindow()
    {
        if (_window is null) return;

        // Проверка локальной полуночи при каждом показе — приложение может проработать всю ночь (раздел 9).
        _viewModel?.CheckDayRollover();

        bool appearing = !_window.IsVisible;
        if (appearing) _window.PlaceOnScreen();

        _activator.ShowAndFocus(_window, _window.InputField);
        if (appearing) _window.PlayAppearance(); // после показа: анимация не должна задерживать каретку
    }

    private void ListenForSecondInstance()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showSignal.WaitOne())
            {
                Dispatcher.Invoke(ShowWindow); // трогать окно можно только из UI-потока
            }
        })
        {
            IsBackground = true,
            Name = "second-instance-listener",
        };
        listener.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveDraft();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _db?.Dispose();
        _showSignal?.Dispose();
        _instanceLock?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Ошибки привязок из dev-режима — в тот же лог, что и падения.</summary>
internal sealed class LogTraceListener : TraceListener
{
    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message)) Log.Write($"BINDING: {message}");
    }
}

internal sealed class SimpleCommand(Action run) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
