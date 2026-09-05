using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct DiagnosticsEntry(string Name, string Text);

public sealed record DiagnosticsPayload(
    MachineProfile Machine,
    PerformanceSnapshot Snapshot,
    PerformanceHistory History,
    PerformanceHitches Hitches,
    PerformanceOperation? LastRun,
    string? Settings,
    IReadOnlyList<DiagnosticsEntry> Logs);

public static class DiagnosticsBundle
{
    public const string FolderName = "diagnostics";

    public const string MachineEntry = "machine.json";

    public const string PerformanceEntry = "performance.json";

    public const string SummaryEntry = "summary.txt";

    public const string SettingsEntry = "settings.toml";

    public const string LogsFolder = "logs/";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<DiagnosticsEntry> Build(DiagnosticsPayload payload, DiagnosticsRedactor? redactor)
    {
        var entries = new List<DiagnosticsEntry>
        {
            new(MachineEntry, JsonSerializer.Serialize(payload.Machine, Json)),
            new(PerformanceEntry, JsonSerializer.Serialize(
                new
                {
                    payload.Snapshot,
                    payload.History,
                    payload.Hitches,
                    payload.LastRun,
                },
                Json)),
            new(SummaryEntry, BuildSummary(payload)),
        };

        if (payload.Settings is not null)
        {
            entries.Add(new(SettingsEntry, payload.Settings));
        }

        foreach (var log in payload.Logs)
        {
            entries.Add(new(LogsFolder + log.Name, log.Text));
        }

        return entries
            .Select(entry => entry with { Text = DiagnosticsSecrets.Redact(entry.Text) })
            .Select(entry => redactor is null ? entry : entry with { Text = redactor.Apply(entry.Text) })
            .ToList();
    }

    public static string Describe(IReadOnlyList<DiagnosticsEntry> entries, bool redacted)
    {
        var text = new StringBuilder();

        text.AppendLine(redacted
            ? "Пути и имена в пакете обезличены: от каждого пути остаются диск и имя объекта."
            : "Пути, имя пользователя и имя машины уезжают в пакет как есть.");

        text.AppendLine("Токены и пароли вырезаны в обоих режимах.");

        if (!entries.Any(static entry => entry.Name == SettingsEntry))
        {
            text.AppendLine("Настройки в пакет не попали: файл настроек прочитать не удалось.");
        }

        text.AppendLine();

        foreach (var entry in entries)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"{entry.Name} – {SizeFormatter.Format(Encoding.UTF8.GetByteCount(entry.Text))}");
        }

        return text.ToString().TrimEnd();
    }

    public static string Save(string directory, IReadOnlyList<DiagnosticsEntry> entries, DateTime now)
    {
        Directory.CreateDirectory(directory);

        var stream = CreateFile(directory, now, out var path);

        using (stream)
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntries(archive, entries);
        }

        return path;
    }

    private static FileStream CreateFile(string directory, DateTime now, out string path)
    {
        var prefix = Path.Combine(directory, $"{AppInfo.Name.ToLowerInvariant()}-diagnostics-{now:yyyyMMdd-HHmmss}");

        for (var attempt = 0; ; attempt++)
        {
            path = attempt == 0 ? prefix + ".zip" : $"{prefix}-{attempt}.zip";

            try
            {
                return new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(path) && attempt < AppDefaults.DiagnosticsNameAttempts)
            {
            }
        }
    }

    private static void WriteEntries(ZipArchive archive, IReadOnlyList<DiagnosticsEntry> entries)
    {
        foreach (var entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry.Name, CompressionLevel.Optimal).Open(), Encoding.UTF8);
            writer.Write(entry.Text);
        }
    }

    private static string BuildSummary(DiagnosticsPayload payload)
    {
        var text = new StringBuilder();

        foreach (var line in payload.Machine.Describe())
        {
            text.AppendLine(line);
        }

        text.AppendLine();
        text.Append(PerformanceReport.Build(payload.Snapshot, payload.Machine.AppVersion, payload.LastRun));

        return text.ToString();
    }
}
