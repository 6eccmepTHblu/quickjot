namespace QuickJot.Data;

/// <summary>Ключи таблицы settings — раздел 12. Собраны в одном месте, чтобы не разъезжались по строкам.</summary>
public static class SettingKeys
{
    public const string Hotkey = "hotkey";

    public const string Corner = "window.corner";
    public const string Width = "window.width";
    public const string MaxHeightShare = "window.max_height_share";

    public const string TitleLines = "card.title_lines";
    public const string TitleFont = "card.title_font";
    public const string TitleSize = "card.title_size";
    public const string SubtaskLines = "card.subtask_lines";

    public const string TagColors = "tags.colors";

    public const string Theme = "ui.theme";
    public const string Animations = "ui.animations";

    public const string DraftTitle = "draft.title";
    public const string DraftNote = "draft.note";

    public const string AutostartEnabled = "autostart.enabled";
    public const string AutostartAdmin = "autostart.admin";
}
