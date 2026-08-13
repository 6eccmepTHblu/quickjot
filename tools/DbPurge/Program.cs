using Microsoft.Data.Sqlite;

// Физическое удаление задач по заголовку. Приложение удаляет мягко: строка остаётся в базе
// с меткой deleted_at и вычищается только суточной чисткой. Здесь строка убирается насовсем.
//
//   dotnet run --project tools\DbPurge -- "<путь к tasks.db>" "<заголовок>" [--yes]
//
// Без --yes ничего не удаляется: сначала показывается, что именно нашлось.

if (args.Length < 2)
{
    Console.WriteLine("""
        Удаление задач по заголовку.

          dotnet run --project tools\DbPurge -- "%APPDATA%\QuickJot\tasks.db" "Заголовок" [--yes]

        Без --yes только показывает найденное. Приложение перед удалением надо закрыть.
        """);
    return 1;
}

var path = args[0];
var title = args[1];
bool confirmed = args.Contains("--yes");

if (!File.Exists(path))
{
    Console.WriteLine($"База не найдена: {path}");
    return 1;
}

using var db = new SqliteConnection($"Data Source={path}");
db.Open();

using (var found = db.CreateCommand())
{
    // По вхождению, а не по точному совпадению: лишний пробел или «ё» вместо «е» иначе
    // прячут задачу от поиска. Показанный список — и есть защита от лишнего удаления.
    found.CommandText = "SELECT id, title, completed_at, deleted_at FROM tasks WHERE title LIKE $title";
    found.Parameters.AddWithValue("$title", $"%{title}%");

    int rows = 0;
    using var reader = found.ExecuteReader();
    while (reader.Read())
    {
        rows++;
        var state = reader.IsDBNull(3) ? (reader.IsDBNull(2) ? "активна" : "выполнена") : "уже удалена";
        Console.WriteLine($"  {reader.GetString(0)}  {state}  «{reader.GetString(1)}»");
    }

    if (rows == 0)
    {
        Console.WriteLine($"Задач с заголовком «{title}» в базе нет.");
        return 0;
    }

    Console.WriteLine($"Найдено: {rows}");
}

if (!confirmed)
{
    Console.WriteLine("Ничего не удалено. Повторите с --yes, если это те самые задачи.");
    return 0;
}

using var purge = db.CreateCommand();
purge.CommandText = "DELETE FROM tasks WHERE title LIKE $title";
purge.Parameters.AddWithValue("$title", $"%{title}%");
int deleted = purge.ExecuteNonQuery();

// Слить журнал в саму базу, чтобы удаление не осталось в файле-спутнике.
using (var checkpoint = db.CreateCommand())
{
    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
    checkpoint.ExecuteNonQuery();
}

Console.WriteLine($"Удалено: {deleted}");
return 0;
