using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap.Storage;

public sealed class AgentTranscriptStore
{
    public const string FolderName = "chat";

    public const string FilePrefix = "turn-";

    public const string FileExtension = ".jsonl";

    private readonly AgentPreferences _preferences;
    private readonly ILogger<AgentTranscriptStore> _logger;

    public AgentTranscriptStore(AgentPreferences preferences, ILogger<AgentTranscriptStore> logger, string? directoryPath = null)
    {
        _preferences = preferences;
        _logger = logger;
        DirectoryPath = directoryPath ?? Path.Combine(AppStorage.DataDirectory, AppStorage.LogsFolderName, FolderName);
    }

    public string DirectoryPath { get; }

    public static string FileName(AgentBackendKind backend, DateTimeOffset startedAt)
    {
        return $"{FilePrefix}{startedAt:yyyyMMdd-HHmmss-fff}-{backend.ToString().ToLowerInvariant()}{FileExtension}";
    }

    internal static IReadOnlyList<string> Obsolete(IEnumerable<string> files, int keep)
    {
        return [.. files.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Skip(keep)];
    }

    public IAgentTranscript? Begin(AgentBackendKind backend, string? token)
    {
        if (!_preferences.Transcript)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            DropObsolete();

            var path = Path.Combine(DirectoryPath, FileName(backend, DateTimeOffset.Now));
            var transcript = new AgentTranscript(path, token, _logger);
            _logger.AgentTranscriptStarted(path);

            return transcript;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.AgentTranscriptFailed(exception, DirectoryPath);

            return null;
        }
    }

    private void DropObsolete()
    {
        var files = Directory.GetFiles(DirectoryPath, $"{FilePrefix}*{FileExtension}");

        foreach (var file in Obsolete(files, AppDefaults.AgentTranscriptLimit - 1))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.AgentTranscriptFailed(exception, file);
            }
        }
    }
}
