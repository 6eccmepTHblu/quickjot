using System.IO;
using QuickJot.Data;

namespace QuickJot;

/// <summary>
/// Приложение резидентное и без окна — без лога его падения не видит никто.
/// Этап 0 показал ровно это: процесс молча умирал, и понять почему было нечем.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string Path => System.IO.Path.Combine(Db.DataDir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Db.DataDir);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Лог, который валит приложение, хуже отсутствующего лога.
        }
    }
}
