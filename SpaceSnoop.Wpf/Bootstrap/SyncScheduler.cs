using System.Diagnostics;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class SyncScheduler
{
    public static string LegacyTaskName => $"{AppInfo.Name} Sync";

    public static string TaskNameFor(string id)
    {
        return $"{AppInfo.Name} Sync [{id}]";
    }

    public static bool Exists(string taskName)
    {
        return Run(["/query", "/tn", taskName], out _) == 0;
    }

    public static bool Create(string taskName, ScheduleInterval interval, TimeSpan time, string argument, out string error)
    {
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            error = "Не удалось определить путь к приложению.";
            return false;
        }

        return Run(BuildCreateArgs(taskName, interval, time, exe, argument), out error) == 0;
    }

    public static bool Remove(string taskName, out string error)
    {
        return Run(["/delete", "/tn", taskName, "/f"], out error) == 0;
    }

    public static bool RunNow(string taskName, out string error)
    {
        return Run(["/run", "/tn", taskName], out error) == 0;
    }

    public static bool SetEnabled(string taskName, bool enabled, out string error)
    {
        return Run(["/change", "/tn", taskName, enabled ? "/enable" : "/disable"], out error) == 0;
    }

    public static ScheduleStatus Query(string taskName)
    {
        return Execute(["/query", "/tn", taskName, "/fo", "CSV", "/v", "/nh"], out var output, out _) == 0
            ? ScheduleStatus.Parse(output)
            : ScheduleStatus.Missing;
    }

    public static List<string> BuildCreateArgs(string taskName, ScheduleInterval interval, TimeSpan time, string exePath, string argument)
    {
        var target = string.IsNullOrEmpty(argument) ? $"\"{exePath}\"" : $"\"{exePath}\" {argument}";

        var args = new List<string>
        {
            "/create", "/tn", taskName,
            "/tr", target,
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
        var code = Execute(args, out _, out var standardError);
        error = code == 0 ? string.Empty : standardError.Trim();
        return code;
    }

    private static int Execute(IEnumerable<string> args, out string standardOutput, out string standardError)
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
            standardOutput = process.StandardOutput.ReadToEnd();
            standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode;
        }
        catch (Exception exception)
        {
            standardOutput = string.Empty;
            standardError = exception.Message;
            return -1;
        }
    }
}
