namespace QuickJot.Data;

/// <summary>
/// Выбранные вручную цвета тегов — раздел 13. Хранятся одной строкой «тег=цвет тег=цвет»:
/// теги нормализованы и не содержат ни пробела, ни знака равенства, так что разбор однозначен.
/// Значение — либо номер оттенка из палитры, либо свой цвет как «#RRGGBB».
/// Тега, которому цвет не назначали, здесь нет — он берёт цвет из имени.
/// </summary>
public sealed class TagColors(SettingsStore settings)
{
    private Dictionary<string, string>? _map;

    private Dictionary<string, string> Map => _map ??= Read();

    /// <summary>null — цвет не задан, берётся автоматический.</summary>
    public string? Chosen(string tag) => Map.TryGetValue(tag, out var value) ? value : null;

    public void Choose(string tag, string? value)
    {
        if (value is null) Map.Remove(tag);
        else Map[tag] = value;

        Write();
    }

    /// <summary>Тег удалили целиком — его цвет больше не нужен.</summary>
    public void Forget(string tag)
    {
        if (Map.Remove(tag)) Write();
    }

    private Dictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>();
        var stored = settings.Get(SettingKeys.TagColors);
        if (string.IsNullOrWhiteSpace(stored)) return map;

        foreach (var pair in stored.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && parts[1].Length > 0) map[parts[0]] = parts[1];
        }

        return map;
    }

    private void Write() =>
        settings.Set(SettingKeys.TagColors, string.Join(' ', Map.Select(pair => $"{pair.Key}={pair.Value}")));
}
