namespace QuickJot.Data;

/// <summary>Один пункт чеклиста внутри задачи — раздел 8.</summary>
public readonly record struct Subtask(bool Done, string Title);

/// <summary>
/// Чеклист лежит в одной колонке задачи: по строке на пункт, впереди «[x] » или «[ ] ».
/// Отдельная таблица завела бы вторую историю с sort_order и вторую ветку в отмене,
/// а нужен здесь список, который правится и откатывается целиком. В бэкапе читается глазами.
/// </summary>
public static class SubtaskFormat
{
    public const string DoneMark = "[x] ";
    public const string OpenMark = "[ ] ";

    public static IReadOnlyList<Subtask> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return [];

        var items = new List<Subtask>();
        foreach (var raw in stored.Split('\n'))
        {
            var line = raw.Trim('\r').Trim();
            if (line.Length == 0) continue;

            // Строка без метки — тоже пункт: правленый руками бэкап не должен молча пропадать.
            bool done = line.StartsWith(DoneMark, StringComparison.Ordinal);
            var title = done || line.StartsWith(OpenMark, StringComparison.Ordinal)
                ? line[DoneMark.Length..]
                : line;

            items.Add(new Subtask(done, title.Trim()));
        }

        return items;
    }

    /// <summary>Пустой чеклист — это NULL в базе, а не пустая строка: колонка либо есть, либо её нет.</summary>
    public static string? Format(IEnumerable<Subtask> items)
    {
        var text = string.Join('\n', items
            .Where(item => item.Title.Trim().Length > 0)
            .Select(item => (item.Done ? DoneMark : OpenMark) + item.Title.Trim()));

        return text.Length == 0 ? null : text;
    }
}
