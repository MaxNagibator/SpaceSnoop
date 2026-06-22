using System.Text.Json;

namespace SpaceSnoop.Core.Docker;

public sealed record DockerUsage(string Type, int TotalCount, int Active, string Size, string Reclaimable)
{
    public static IReadOnlyList<DockerUsage> Parse(string ndjson)
    {
        if (string.IsNullOrWhiteSpace(ndjson))
        {
            return [];
        }

        List<DockerUsage> result = [];

        foreach (var line in ndjson.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var usage = TryParseLine(trimmed);
            if (usage is not null)
            {
                result.Add(usage);
            }
        }

        return result;
    }

    private static DockerUsage? TryParseLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var type = GetString(root, "Type");
            var size = GetString(root, "Size");
            var reclaimable = GetString(root, "Reclaimable");
            var total = ParseCount(GetString(root, "TotalCount"));
            var active = ParseCount(GetString(root, "Active"));

            return new(type, total, active, size, reclaimable);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static int ParseCount(string text)
    {
        return int.TryParse(text, out var n) ? n : 0;
    }
}
