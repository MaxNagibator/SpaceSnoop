using System.IO;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Agent;

public sealed record AgentModelOption(string Id, string Title, string Hint)
{
    public IReadOnlyList<string> Efforts { get; init; } = [];

    public override string ToString()
    {
        return Title;
    }
}

public sealed record AgentEffortOption(string Id, string Title)
{
    public override string ToString()
    {
        return Title;
    }
}

public static class AgentModels
{
    public static readonly AgentModelOption CliDefault = new(string.Empty, "По умолчанию CLI", "Модель, выбранная по умолчанию в самом CLI");

    public static readonly AgentEffortOption EffortDefault = new(string.Empty, "По умолчанию");

    private static readonly string[] ClaudeEfforts = ["low", "medium", "high", "xhigh", "max"];

    private static readonly string[] CodexEfforts = ["low", "medium", "high", "xhigh", "max", "ultra"];

    private static readonly AgentModelOption[] ClaudeModels =
    [
        new("fable", "Fable 5", "Самая сильная и самая дорогая") { Efforts = ClaudeEfforts },
        new("opus", "Opus 5", "Сильная модель для сложных задач") { Efforts = ClaudeEfforts },
        new("sonnet", "Sonnet 5", "Баланс скорости и качества") { Efforts = ClaudeEfforts },
        new("haiku", "Haiku 4.5", "Самая быстрая и дешёвая") { Efforts = ClaudeEfforts },
    ];

    private static readonly AgentModelOption[] CodexFallbackModels =
    [
        new("gpt-5.6-sol", "GPT-5.6-Sol", "Флагманская агентная модель") { Efforts = CodexEfforts },
        new("gpt-5.6-terra", "GPT-5.6-Terra", "Баланс скорости и качества") { Efforts = ["low", "medium", "high", "xhigh", "max"] },
        new("gpt-5.6-luna", "GPT-5.6-Luna", "Быстрая модель для простых задач") { Efforts = ["low", "medium", "high", "xhigh", "max"] },
    ];

    private static readonly Dictionary<string, string> EffortTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["low"] = "Низкий",
        ["medium"] = "Средний",
        ["high"] = "Высокий",
        ["xhigh"] = "Очень высокий",
        ["max"] = "Максимальный",
        ["ultra"] = "Предельный",
    };

    private static readonly Lazy<IReadOnlyList<AgentModelOption>> LazyCodexModels = new(LoadCodexModels);

    public static IReadOnlyList<AgentModelOption> For(AgentBackendKind backend)
    {
        return backend == AgentBackendKind.Codex ? LazyCodexModels.Value : ClaudeModels;
    }

    public static AgentModelOption? Find(AgentBackendKind backend, string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : For(backend).FirstOrDefault(option => string.Equals(option.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<AgentEffortOption> Efforts(AgentBackendKind backend, string? modelId)
    {
        var levels = Find(backend, modelId)?.Efforts is { Count: > 0 } known
            ? known
            : backend == AgentBackendKind.Codex ? CodexEfforts : ClaudeEfforts;

        return [EffortDefault, .. levels.Select(level => new AgentEffortOption(level, EffortTitle(level)))];
    }

    public static string EffortTitle(string effort)
    {
        return EffortTitles.TryGetValue(effort, out var title) ? title : effort;
    }

    internal static IReadOnlyList<AgentModelOption>? ParseCodexCache(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<(int Priority, AgentModelOption Option)>();

            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var slug = Text(model, "slug");

                if (slug.Length == 0 || !string.Equals(Text(model, "visibility"), "list", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = Text(model, "display_name") is { Length: > 0 } displayName ? displayName : slug;
                var option = new AgentModelOption(slug, title, Text(model, "description")) { Efforts = ReasoningLevels(model) };

                parsed.Add((Priority(model), option));
            }

            return parsed.Count == 0 ? null : [.. parsed.OrderBy(entry => entry.Priority).Select(entry => entry.Option)];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<AgentModelOption> LoadCodexModels()
    {
        try
        {
            var path = Path.Combine(CodexHome(), "models_cache.json");

            return File.Exists(path) ? ParseCodexCache(File.ReadAllText(path)) ?? CodexFallbackModels : CodexFallbackModels;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CodexFallbackModels;
        }
    }

    private static string CodexHome()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");

        return string.IsNullOrWhiteSpace(home)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : home.Trim();
    }

    private static IReadOnlyList<string> ReasoningLevels(JsonElement model)
    {
        if (!model.TryGetProperty("supported_reasoning_levels", out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. levels.EnumerateArray()
                .Where(level => level.ValueKind == JsonValueKind.Object)
                .Select(level => Text(level, "effort"))
                .Where(effort => effort.Length > 0),
        ];
    }

    private static int Priority(JsonElement model)
    {
        return model.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Number && priority.TryGetInt32(out var value)
            ? value
            : int.MaxValue;
    }

    private static string Text(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }
}
