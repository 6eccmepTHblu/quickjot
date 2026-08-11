using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using QuickJot.Data;

namespace QuickJot.ViewModels;

public sealed record Option<T>(T Value, string Label);

/// <summary>Окно настроек — раздел 13. Значения пишутся в базу сразу, применяются тут же.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private bool _loading = true;

    public SettingsViewModel(SettingsStore settings)
    {
        _settings = settings;

        _hotkeyText = Hotkey.Parse(settings.Get(SettingKeys.Hotkey)).ToString();
        _corner = Enum.TryParse<AnchorCorner>(settings.Get(SettingKeys.Corner), out var corner) ? corner : AnchorCorner.TopRight;
        _width = settings.GetDouble(SettingKeys.Width, 560);
        _maxHeightPercent = settings.GetDouble(SettingKeys.MaxHeightShare, 0.6) * 100;
        _titleLines = (int)settings.GetDouble(SettingKeys.TitleLines, 2);
        _titleFont = settings.Get(SettingKeys.TitleFont) ?? "Segoe UI Variable Text";
        _titleSize = settings.GetDouble(SettingKeys.TitleSize, 14);
        _theme = settings.Get(SettingKeys.Theme) ?? "system";
        _animations = settings.GetBool(SettingKeys.Animations, true);
        _runAsAdmin = settings.GetBool(SettingKeys.AutostartAdmin, false);

        // Задачу мог удалить кто угодно мимо приложения — правда живёт в Планировщике, а не у нас.
        _autostartEnabled = QuickJot.Autostart.IsEnabled();

        _loading = false;
    }

    /// <summary>Главное окно перечитывает настройки и применяет их на лету.</summary>
    public event Action? Changed;

    /// <summary>Регистрацию хоткея умеет только приложение — оно и подставляет сюда свою проверку.</summary>
    public Func<Hotkey, bool>? TryRegisterHotkey { get; init; }

    public IReadOnlyList<Option<AnchorCorner>> Corners { get; } =
    [
        new(AnchorCorner.TopLeft, "Верхний левый"),
        new(AnchorCorner.TopRight, "Верхний правый"),
        new(AnchorCorner.BottomLeft, "Нижний левый"),
        new(AnchorCorner.BottomRight, "Нижний правый"),
    ];

    public IReadOnlyList<Option<string>> Themes { get; } =
    [
        new("system", "Системная"),
        new("light", "Светлая"),
        new("dark", "Тёмная"),
    ];

    public IReadOnlyList<string> Fonts { get; } =
        [.. System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(name => name)];

    [ObservableProperty] private string _hotkeyText;
    [ObservableProperty] private string? _hotkeyError;
    [ObservableProperty] private AnchorCorner _corner;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _maxHeightPercent;
    [ObservableProperty] private int _titleLines;
    [ObservableProperty] private string _titleFont;
    [ObservableProperty] private double _titleSize;
    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _animations;
    [ObservableProperty] private bool _autostartEnabled;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private string? _autostartError;

    /// <summary>
    /// Новая комбинация проверяется немедленно: `RegisterHotKey` возвращает false, если она занята
    /// другой программой, и человек должен увидеть это здесь же — раздел 4.
    /// </summary>
    public void ApplyHotkey(Hotkey hotkey)
    {
        if (!hotkey.IsValid)
        {
            HotkeyError = "Нужна комбинация с Ctrl, Alt, Shift или Win";
            return;
        }

        if (TryRegisterHotkey?.Invoke(hotkey) == false)
        {
            HotkeyError = $"{hotkey} занята другой программой";
            return;
        }

        HotkeyError = null;
        HotkeyText = hotkey.ToString();
        Save(SettingKeys.Hotkey, hotkey.ToString());
    }

    partial void OnCornerChanged(AnchorCorner value) => Save(SettingKeys.Corner, value.ToString());
    partial void OnWidthChanged(double value) => SaveDouble(SettingKeys.Width, Math.Round(value));
    partial void OnMaxHeightPercentChanged(double value) => SaveDouble(SettingKeys.MaxHeightShare, Math.Round(value) / 100);
    partial void OnTitleLinesChanged(int value) => SaveDouble(SettingKeys.TitleLines, value);
    partial void OnTitleFontChanged(string value) => Save(SettingKeys.TitleFont, value);
    partial void OnTitleSizeChanged(double value) => SaveDouble(SettingKeys.TitleSize, Math.Round(value));
    partial void OnThemeChanged(string value) => Save(SettingKeys.Theme, value);
    partial void OnAnimationsChanged(bool value) => SaveBool(SettingKeys.Animations, value);

    partial void OnAutostartEnabledChanged(bool value)
    {
        if (_loading) return;

        ApplyAutostart(value, RunAsAdmin);
        SaveBool(SettingKeys.AutostartEnabled, AutostartEnabled);
    }

    partial void OnRunAsAdminChanged(bool value)
    {
        if (_loading) return;

        SaveBool(SettingKeys.AutostartAdmin, value);

        // Права даёт задача в Планировщике, поэтому её надо пересоздать. Текущий процесс останется
        // с прежними правами до следующего входа в систему — раздел 3.
        if (AutostartEnabled) ApplyAutostart(true, value);
    }

    private void ApplyAutostart(bool enabled, bool admin)
    {
        AutostartError = null;
        if (QuickJot.Autostart.Apply(enabled, admin)) return;

        AutostartError = enabled
            ? "Не удалось создать задачу в Планировщике — Windows не дала прав"
            : "Не удалось удалить задачу из Планировщика — Windows не дала прав";

        // Возвращаем галку к тому, что на самом деле в Планировщике, не гоняя обработчик по кругу.
        _loading = true;
        AutostartEnabled = QuickJot.Autostart.IsEnabled();
        _loading = false;
    }

    private void Save(string key, string value)
    {
        if (_loading) return;

        _settings.Set(key, value);
        Changed?.Invoke();
    }

    private void SaveDouble(string key, double value)
    {
        if (_loading) return;

        _settings.SetDouble(key, value);
        Changed?.Invoke();
    }

    private void SaveBool(string key, bool value)
    {
        if (_loading) return;

        _settings.SetBool(key, value);
        Changed?.Invoke();
    }
}
