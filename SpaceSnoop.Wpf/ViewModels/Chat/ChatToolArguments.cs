using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels.Chat;

public static class ChatToolArguments
{
    public const int MaxValueLength = 120;

    public static string Describe(string arguments)
    {
        var trimmed = arguments.Trim();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!trimmed.StartsWith('{'))
        {
            return Shorten(trimmed);
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Shorten(trimmed);
            }

            return string.Join(", ", document.RootElement.EnumerateObject().Select(property => $"{property.Name}: {Shorten(Value(property.Value))}"));
        }
        catch (JsonException)
        {
            return Shorten(trimmed);
        }
    }

    private static string Value(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => "–",
            _ => element.GetRawText(),
        };
    }

    private static string Shorten(string value)
    {
        var line = value.ReplaceLineEndings(" ").Trim();

        return line.Length <= MaxValueLength ? line : string.Concat(line.AsSpan(0, MaxValueLength), "…");
    }
}
