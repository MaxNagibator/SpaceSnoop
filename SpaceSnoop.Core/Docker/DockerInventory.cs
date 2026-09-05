using System.Text.Json;

namespace SpaceSnoop.Core.Docker;

public static class DockerInventory
{
    public static IReadOnlyList<DockerObject> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            List<DockerObject> result = [];
            ReadImages(root, result);
            ReadContainers(root, result);
            ReadVolumes(root, result);
            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void ReadImages(JsonElement root, List<DockerObject> into)
    {
        foreach (var image in ArrayOf(root, "Images"))
        {
            var repo = Str(image, "Repository");
            var tag = Str(image, "Tag");
            var name = repo is "" or "<none>" ? ShortId(Str(image, "ID")) : $"{repo}:{tag}";
            var size = Str(image, "Size");
            var containers = ParseInt(Str(image, "Containers"));

            into.Add(new(
                DockerObjectKind.Image,
                Str(image, "ID"),
                name,
                size,
                DockerSize.ToBytes(size),
                containers > 0,
                DockerText.Age(Str(image, "CreatedSince"))));
        }
    }

    private static void ReadContainers(JsonElement root, List<DockerObject> into)
    {
        foreach (var container in ArrayOf(root, "Containers"))
        {
            var size = Str(container, "Size");
            var running = Str(container, "State").Equals("running", StringComparison.OrdinalIgnoreCase);

            into.Add(new(
                DockerObjectKind.Container,
                Str(container, "ID"),
                Str(container, "Names"),
                size,
                DockerSize.ToBytes(size),
                running,
                DockerText.Status(Str(container, "Status"))));
        }
    }

    private static void ReadVolumes(JsonElement root, List<DockerObject> into)
    {
        foreach (var volume in ArrayOf(root, "Volumes"))
        {
            var name = Str(volume, "Name");
            var size = Str(volume, "Size");
            var links = ParseInt(Str(volume, "Links"));

            into.Add(new(
                DockerObjectKind.Volume,
                name,
                DockerText.ShortName(name),
                size,
                DockerSize.ToBytes(size),
                links > 0,
                links > 0 ? $"используется: {links}" : "не используется"));
        }
    }

    private static IEnumerable<JsonElement> ArrayOf(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];
    }

    private static string Str(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static int ParseInt(string text)
    {
        return int.TryParse(text, out var n) ? n : 0;
    }

    private static string ShortId(string id)
    {
        var trimmed = id.StartsWith("sha256:", StringComparison.Ordinal) ? id["sha256:".Length..] : id;
        return trimmed.Length > 12 ? trimmed[..12] : trimmed;
    }
}
