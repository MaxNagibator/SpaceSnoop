using System.Text.Json;

namespace SpaceSnoop.Core.Docker;

public sealed record DockerDesktopSettings(
    string? CustomWslDistroDir,
    bool WslEngineEnabled,
    string? DataFolder)
{
    // TODO: Добавить отдельную ветку compact для Hyper-V по DataFolder после проверки раскладки Docker Desktop.
}

public interface IDockerDesktopSettingsProvider
{
    DockerDesktopSettings? Read();
}

public sealed class DockerDesktopSettingsProvider : IDockerDesktopSettingsProvider
{
    public DockerDesktopSettings? Read()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                return new(
                    GetString(root, "CustomWslDistroDir"),
                    GetBoolean(root, "WslEngineEnabled", defaultValue: true),
                    GetString(root, "DataFolder"));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(roaming, "Docker", "settings-store.json");
        yield return Path.Combine(local, "Docker", "settings-store.json");
    }

    private static string? GetString(JsonElement root, string name)
    {
        return TryGetProperty(root, name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool GetBoolean(JsonElement root, string name, bool defaultValue)
    {
        return TryGetProperty(root, name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : defaultValue;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
