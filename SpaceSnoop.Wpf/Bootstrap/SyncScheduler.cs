using System.Diagnostics;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class SyncScheduler
{
    public static string TaskName => $"{AppInfo.Name} Sync";

    public static bool Exists()
    {
        return Run(["/query", "/tn", TaskName], out _) == 0;
    }

    public static bool Create(ScheduleInterval interval, TimeSpan time, out string error)
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            error = "Не удалось определить путь к приложению.";
            return false;
        }

        return Run(BuildCreateArgs(interval, time, exe), out error) == 0;
    }

    public static bool Remove(out string error)
    {
        return Run(["/delete", "/tn", TaskName, "/f"], out error) == 0;
    }

    public static List<string> BuildCreateArgs(ScheduleInterval interval, TimeSpan time, string exePath)
    {
        var args = new List<string>
        {
            "/create", "/tn", TaskName,
            "/tr", $"\"{exePath}\" {AppInfo.SyncArgument}",
            "/f",
        };

        switch (interval)
        {
            case ScheduleInterval.Hourly:
                args.AddRange(["/sc", "HOURLY", "/st", FormatTime(time)]);
                break;

            case ScheduleInterval.OnLogon:
                args.AddRange(["/sc", "ONLOGON"]);
                break;

            default:
                args.AddRange(["/sc", "DAILY", "/st", FormatTime(time)]);
                break;
        }

        return args;
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{time.Hours:D2}:{time.Minutes:D2}";
    }

    private static int Run(IEnumerable<string> args, out string error)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info)!;
            var standardError = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            error = process.ExitCode == 0 ? string.Empty : standardError.Trim();
            return process.ExitCode;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return -1;
        }
    }
}
