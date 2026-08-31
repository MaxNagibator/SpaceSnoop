using System.IO;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class DiagnosticsCollector(PerformanceMonitor monitor, PerformanceRunTracker runs, ISettingsStore settings)
{
    public MachineProfile Machine { get; private set; } = MachineProfile.Capture();

    public void CaptureMachine(Visual visual)
    {
        Machine = MachineProfile.Capture(visual);
    }

    public IReadOnlyList<DiagnosticsEntry> Build(bool rawPaths)
    {
        var payload = new DiagnosticsPayload(
            Machine,
            monitor.Snapshot,
            monitor.CaptureHistory(TimeSpan.FromSeconds(AppDefaults.PerformanceHistorySecondsMax), AppDefaults.PerformanceHistorySamples),
            monitor.CaptureHitches(AppDefaults.PerformanceHitchMs, AppDefaults.DiagnosticsHitchRows),
            runs.Last,
            ReadSettings(),
            ReadLogs());

        return DiagnosticsBundle.Build(payload, rawPaths ? null : DiagnosticsRedactor.ForCurrentUser());
    }

    public string Save(IReadOnlyList<DiagnosticsEntry> entries)
    {
        return DiagnosticsBundle.Save(
            Path.Combine(AppStorage.DataDirectory, DiagnosticsBundle.FolderName),
            entries,
            DateTime.Now);
    }

    private string? ReadSettings()
    {
        settings.Flush();

        var path = Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName);

        return File.Exists(path) ? ReadShared(path, int.MaxValue) : null;
    }

    private static IReadOnlyList<DiagnosticsEntry> ReadLogs()
    {
        var directory = new DirectoryInfo(Path.Combine(AppStorage.DataDirectory, AppStorage.LogsFolderName));

        if (!directory.Exists)
        {
            return [];
        }

        return directory
            .GetFiles(AppInfo.LogFilePrefix + "*.log")
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .Take(AppDefaults.DiagnosticsLogFiles)
            .Select(static file => new DiagnosticsEntry(file.Name, ReadShared(file.FullName, AppDefaults.DiagnosticsLogTailLines)))
            .Where(static entry => entry.Text.Length > 0)
            .ToList();
    }

    private static string ReadShared(string path, int tailLines)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            if (tailLines == int.MaxValue)
            {
                return reader.ReadToEnd();
            }

            var tail = new Queue<string>(tailLines);

            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == tailLines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return string.Join(Environment.NewLine, tail);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
