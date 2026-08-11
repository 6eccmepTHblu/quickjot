using System.Diagnostics;
using System.Security.Principal;

namespace QuickJot;

/// <summary>
/// Автозапуск через Планировщик заданий — раздел 3. Ветка реестра Run не используется:
/// с ней Windows спрашивает UAC при каждой загрузке, если приложению нужны права администратора.
/// </summary>
internal static class Autostart
{
    private const string TaskName = "QuickJot";

    public static string ExePath => Environment.ProcessPath ?? "";

    public static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static bool IsEnabled() => Schtasks($"/Query /TN {TaskName}") == 0;

    /// <summary>
    /// Задача с наивысшими правами создаётся только из элевированного процесса. Если прав нет —
    /// перезапускаем сами себя через UAC одним служебным вызовом, который создаст задачу и выйдет.
    /// </summary>
    public static bool Apply(bool enabled, bool admin)
    {
        if (!enabled)
        {
            if (!IsEnabled()) return true; // удалять нечего — это не ошибка
            if (Schtasks($"/Delete /F /TN {TaskName}") == 0) return true;

            // Задачу с наивысшими правами создавал элевированный процесс, и удалить её обычный не может.
            return RequestElevation("off", shouldExist: false);
        }

        if (admin && !IsElevated) return RequestElevation("on-admin", shouldExist: true);

        var command = $"/Create /F /TN {TaskName} /TR \"\\\"{ExePath}\\\"\" /SC ONLOGON";
        if (admin) command += " /RL HIGHEST";

        return Schtasks(command) == 0;
    }

    /// <summary>Служебный режим: приложение запущено самим собой через UAC только ради Планировщика.</summary>
    public static void ApplyFromElevatedHelper(string mode)
    {
        switch (mode)
        {
            case "on-admin":
                Schtasks($"/Create /F /TN {TaskName} /TR \"\\\"{ExePath}\\\"\" /SC ONLOGON /RL HIGHEST");
                break;
            case "off":
                Schtasks($"/Delete /F /TN {TaskName}");
                break;
        }
    }

    private static bool RequestElevation(string mode, bool shouldExist)
    {
        try
        {
            var elevated = Process.Start(new ProcessStartInfo(ExePath, $"--autostart {mode}")
            {
                Verb = "runas", // здесь Windows покажет запрос UAC
                UseShellExecute = true,
            });

            elevated?.WaitForExit(30_000);
            return IsEnabled() == shouldExist;
        }
        catch (Exception ex)
        {
            Log.Write($"автозапуск ({mode}) не применён: {ex.Message}"); // чаще всего отказ в UAC
            return false;
        }
    }

    private static int Schtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process is null) return -1;

            process.WaitForExit(15_000);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Write($"schtasks {arguments} упал: {ex.Message}");
            return -1;
        }
    }
}
