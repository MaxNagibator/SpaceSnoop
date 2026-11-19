using KeepShell.Diagnostics;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class SpaceSnoopBundleSource(ISettingsStore settings, PerformanceOperations operations, string settingsPath) : IDiagnosticsBundleSource
{
    public const string SettingsEntry = "settings.toml";

    public const string SettingsFailureEntry = "settings-unavailable.txt";

    public const string OperationsEntry = "operations.json";

    public const string SessionEntry = "session.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public static string SessionId { get; } = Guid.NewGuid().ToString("N");

    public IEnumerable<DiagnosticsEntry> Collect()
    {
        var (name, text) = ReadSettings();

        yield return new(name, text);

        using var process = Process.GetCurrentProcess();
        yield return new(
            SessionEntry,
            JsonSerializer.Serialize(
                new
                {
                    SessionId,
                    Environment.ProcessId,
                    ProcessStartedAtUtc = process.StartTime.ToUniversalTime(),
                    CapturedAtUtc = DateTime.UtcNow,
                    BuildVersion = AppInfo.InformationalVersion,
                },
                Json));

        yield return new(
            OperationsEntry,
            JsonSerializer.Serialize(
                new
                {
                    Operation = operations.Current,
                    LastRun = operations.Last,
                    operations.LastCleanup,
                },
                Json));
    }

    private static string Note(string reason)
    {
        return string.Join(
            Environment.NewLine,
            "Настройки приложения в пакет не попали.",
            reason,
            "Остальные сведения в пакете на месте, разбор пойдёт без настроек.");
    }

    private (string Name, string Text) ReadSettings()
    {
        settings.Flush();

        try
        {
            using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            var text = reader.ReadToEnd();

            return text.Length > 0
                ? (SettingsEntry, text)
                : (SettingsFailureEntry, Note("Файл настроек пуст."));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return (SettingsFailureEntry, Note("Файла настроек ещё нет: приложение пока ничего в него не записало."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (SettingsFailureEntry, Note("Файл настроек не удалось прочитать: он занят другой программой или закрыт для чтения."));
        }
    }
}
