using System.Windows;
using System.Windows.Input;
using QuickJot.ViewModels;

namespace QuickJot;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        ApplyTheme(viewModel.Theme);

        // Появление — как у главного окна, только расти неоткуда, кроме как из центра.
        Loaded += (_, _) => Appearance.Play(Root, Appearance.FromCenter);

        // Тему меняют прямо здесь — окно обязано перекраситься сразу, иначе выбор не виден.
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.Theme)) ApplyTheme(viewModel.Theme);
        };
    }

    /// <summary>
    /// Кроме своих кистей окну нужен и ThemeMode: выпадающие списки живут в отдельном визуальном
    /// корне и наши ресурсы до них не достают — без него список рисуется белым.
    /// </summary>
    private void ApplyTheme(string? theme)
    {
        Theme.Apply(this, theme);

        ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Native.ApplyMicaAndRoundCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    /// <summary>
    /// Поле хоткея ловит саму комбинацию, а не текст. Проверка занятости — сразу при нажатии,
    /// потому что `RegisterHotKey` узнаёт об этом только в момент регистрации (раздел 4).
    /// </summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Hotkey.IsModifierKey(key)) return; // ждём, пока нажмут саму клавишу

        if (key == Key.Escape)
        {
            Close();
            return;
        }

        _viewModel.ApplyHotkey(new Hotkey(Keyboard.Modifiers, key));
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => Close();
}
