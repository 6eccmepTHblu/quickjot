namespace QuickJot.Data;

/// <summary>
/// Выбранные вручную цвета тегов — раздел 13. Хранятся одной строкой «тег=номер тег=номер»:
/// теги нормализованы и не содержат ни пробела, ни знака равенства, так что разбор однозначен.
/// Тега, которому цвет не назначали, здесь нет — он берёт цвет из имени.
/// </summary>
public sealed class TagColors(SettingsStore settings)
{
    private Dictionary<string, int>? _map;

    private Dictionary<string, int> Map => _map ??= Read();

    /// <summary>null — цвет не задан, берётся автоматический.</summary>
    public int? Chosen(string tag) => Map.TryGetValue(tag, out int index) ? index : null;

    public void Choose(string tag, int? index)
    {
        if (index is null) Map.Remove(tag);
        else Map[tag] = index.Value;

        Write();
    }

    /// <summary>Тег удалили целиком — его цвет больше не нужен.</summary>
    public void Forget(string tag)
    {
        if (Map.Remove(tag)) Write();
    }

    private Dictionary<string, int> Read()
    {
        var map = new Dictionary<string, int>();
        var stored = settings.Get(SettingKeys.TagColors);
        if (string.IsNullOrWhiteSpace(stored)) return map;

        foreach (var pair in stored.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && int.TryParse(parts[1], out int index)) map[parts[0]] = index;
        }

        return map;
    }

    private void Write() =>
        settings.Set(SettingKeys.TagColors, string.Join(' ', Map.Select(pair => $"{pair.Key}={pair.Value}")));
}
