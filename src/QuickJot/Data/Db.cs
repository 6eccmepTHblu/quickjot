using System.Data;
using System.Globalization;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace QuickJot.Data;

/// <summary>Открытие базы, схема, бэкапы. Схема — раздел 12 спецификации.</summary>
public static class Db
{
    public const string AppName = "QuickJot";

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string DefaultDbPath => Path.Combine(DataDir, "tasks.db");

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS tasks (
          id           TEXT PRIMARY KEY,
          title        TEXT NOT NULL,
          notes        TEXT,
          is_flagged   INTEGER NOT NULL DEFAULT 0,
          is_expanded  INTEGER NOT NULL DEFAULT 0,
          sort_order   REAL NOT NULL,
          completed_at TEXT,
          deleted_at   TEXT,
          created_at   TEXT NOT NULL,
          updated_at   TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tasks_active ON tasks(deleted_at, completed_at, sort_order);

        CREATE TABLE IF NOT EXISTS settings (
          key   TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );
        """;

    static Db()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true; // is_flagged -> IsFlagged
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
    }

    /// <summary>
    /// Единственный способ положить метку времени в базу. Обработчик типа тут не помогает:
    /// у Dapper для DateTime своя встроенная карта, и на записи параметра он обработчик не спрашивает —
    /// метка уходила бы в локальном формате без «Z» и на чтении смещалась на часовой пояс.
    /// </summary>
    public static string Iso(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static SqliteConnection Open(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var db = new SqliteConnection($"Data Source={dbPath}");
        db.Open();
        db.Execute("PRAGMA journal_mode=WAL;");
        db.Execute("PRAGMA synchronous=FULL;"); // запись durable на каждое действие

        // Без этого WAL не сливается в саму базу, пока не наберёт 1000 страниц: за день работы
        // tasks.db так и остаётся пустой заготовкой, а все задачи лежат в файле-спутнике tasks.db-wal.
        // Стоит SQLite один раз счесть спутник неактуальным (например, после снятия процесса,
        // оставившего протухший tasks.db-shm) — и приложение молча открывается с пустым списком
        // поверх целых данных. Сливаем после каждой записи: база крошечная, цена — один fsync.
        db.Execute("PRAGMA wal_autocheckpoint=1;");
        db.Execute("PRAGMA wal_checkpoint(TRUNCATE);"); // и сразу подобрать всё, что накопилось раньше

        db.Execute(Schema);
        return db;
    }

    /// <summary>Куда класть бэкапы: рядом с той базой, которая открыта, а не всегда в рабочую папку.</summary>
    public static string BackupsDirFor(string dbPath) =>
        Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");

    /// <summary>
    /// Копия базы при старте, хранятся последние <paramref name="keep"/> штук.
    /// VACUUM INTO даёт консистентный однофайловый снимок и в режиме WAL — простое копирование файла не даёт.
    /// </summary>
    public static void Backup(SqliteConnection db, string backupsDir, int keep = 5)
    {
        Directory.CreateDirectory(backupsDir);
        var path = Path.Combine(backupsDir, $"tasks-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        if (File.Exists(path)) return;

        db.Execute($"VACUUM INTO '{path.Replace("'", "''")}'");

        foreach (var stale in new DirectoryInfo(backupsDir)
                     .GetFiles("tasks-*.db")
                     .OrderByDescending(f => f.Name)
                     .Skip(keep))
        {
            stale.Delete();
        }
    }
}

/// <summary>
/// Все метки времени — UTC в ISO-8601. Без этого обработчика Kind приезжает Unspecified
/// и локальная полночь (разделы 9 и 12) начинает считаться от неизвестно чего.
/// </summary>
internal sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override DateTime Parse(object value) => value switch
    {
        string s => FromIso(s),
        DateTime d => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        _ => DateTime.SpecifyKind(Convert.ToDateTime(value, CultureInfo.InvariantCulture), DateTimeKind.Utc),
    };

    /// <summary>
    /// Строка без «Z» считается UTC, а не локальным временем: в базе локального времени не бывает
    /// (раздел 12), и трактовать его как локальное — молча сдвигать метку на часовой пояс.
    /// </summary>
    private static DateTime FromIso(string text)
    {
        var parsed = DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
    }

    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.String;
        parameter.Value = value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
