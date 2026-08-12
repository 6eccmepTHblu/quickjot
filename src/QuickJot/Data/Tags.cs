using System.Text;

namespace QuickJot.Data;

/// <summary>
/// Теги живут в той же строке, что и заголовок: «Сделать отчёт #работа #срочно». Отдельного поля нет,
/// поэтому разбор строки — единственное место, где решается, что тег, а что текст.
/// В базе лежат через пробел в колонке задачи — раздел 12.
/// </summary>
public static class TagFormat
{
    public const char Marker = '#';

    /// <summary>Длиннее уже не тег, а фраза: чип перестаёт читаться и ломает строку карточки.</summary>
    public const int MaxLength = 24;

    /// <summary>Разбирает строку ввода на заголовок и теги.</summary>
    public static (string Title, IReadOnlyList<string> Tags) Split(string input)
    {
        var title = new StringBuilder();
        var tags = new List<string>();

        foreach (var token in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tag = token[0] == Marker ? Normalize(token) : null;
            if (tag is not null)
            {
                if (!tags.Contains(tag)) tags.Add(tag);
                continue;
            }

            if (title.Length > 0) title.Append(' ');
            title.Append(token);
        }

        return (title.ToString(), tags);
    }

    /// <summary>
    /// Приводит написанное к виду, в котором тег хранится: без решётки, в нижнем регистре.
    /// null — если после чистки не осталось ничего годного.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null) return null;

        var text = raw.Trim().TrimStart(Marker);
        var clean = new StringBuilder();

        foreach (var symbol in text)
        {
            if (char.IsLetterOrDigit(symbol) || symbol is '_' or '-' or '/' or '.') clean.Append(symbol);
            else break; // тег кончается на первом же постороннем символе, а не выбрасывает его из середины
        }

        if (clean.Length == 0) return null;
        if (clean.Length > MaxLength) clean.Length = MaxLength;

        return clean.ToString().ToLowerInvariant();
    }

    public static IReadOnlyList<string> Parse(string? stored) => string.IsNullOrWhiteSpace(stored)
        ? []
        : stored.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();

    /// <summary>Пустой набор — это NULL в базе, а не пустая строка.</summary>
    public static string? Format(IEnumerable<string> tags)
    {
        var text = string.Join(' ', tags.Select(Normalize).Where(tag => tag is not null).Distinct());
        return text.Length == 0 ? null : text;
    }

    /// <summary>Обратно в одну строку — то, что человек видит и правит в карточке.</summary>
    public static string Compose(string title, IEnumerable<string> tags)
    {
        var text = string.Join(' ', tags.Select(tag => Marker + tag));
        return text.Length == 0 ? title : $"{title} {text}";
    }

    /// <summary>
    /// Тег, который сейчас набирают: последнее слово строки, начатое с решётки.
    /// Из него растёт подсказка, поэтому пустой «#» — тоже запрос, а не «ничего».
    /// </summary>
    public static string? TypedTag(string input, int caret)
    {
        if (caret <= 0 || caret > input.Length) return null;

        int start = input.LastIndexOf(Marker, caret - 1);
        if (start < 0) return null;
        if (start > 0 && input[start - 1] != ' ') return null; // решётка в середине слова тегом не делает
        if (input.IndexOf(' ', start, caret - start) >= 0) return null; // слово уже закончилось

        return input[(start + 1)..caret].ToLowerInvariant();
    }
}
