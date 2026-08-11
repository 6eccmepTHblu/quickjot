using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace QuickJot.Data;

/// <summary>Таблица key/value из раздела 12: черновик поля ввода, размеры окна, угол привязки, хоткей.</summary>
public sealed class SettingsStore(SqliteConnection db)
{
    public string? Get(string key) =>
        db.ExecuteScalar<string?>("SELECT value FROM settings WHERE key = @key", new { key });

    public void Set(string key, string value) => db.Execute(
        "INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = @value",
        new { key, value });

    public void Remove(string key) => db.Execute("DELETE FROM settings WHERE key = @key", new { key });

    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public void SetDouble(string key, double value) =>
        Set(key, value.ToString(CultureInfo.InvariantCulture));

    public bool GetBool(string key, bool fallback) => Get(key) switch
    {
        "1" => true,
        "0" => false,
        _ => fallback,
    };

    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");
}
