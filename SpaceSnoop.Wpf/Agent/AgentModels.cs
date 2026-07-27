using System.IO;
using System.Text;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Agent;

public sealed record AgentModelOption(string Id, string Title, string Hint)
{
    public IReadOnlyList<string>? Efforts { get; init; }

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

    private static readonly string[] OpenCodeVariants = ["minimal", "low", "medium", "high", "max"];

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
        ["none"] = "Без рассуждений",
        ["minimal"] = "Минимальный",
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
        return backend switch
        {
            AgentBackendKind.Claude => ClaudeModels,
            AgentBackendKind.Codex => LazyCodexModels.Value,
            _ => [],
        };
    }

    public static AgentModelOption? Find(AgentBackendKind backend, string? id)
    {
        return string.IsNullOrWhiteSpace(id)
            ? null
            : For(backend).FirstOrDefault(option => string.Equals(option.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<AgentEffortOption> Efforts(AgentBackendKind backend, string? modelId)
    {
        return EffortsFor(Find(backend, modelId), backend);
    }

    public static IReadOnlyList<AgentEffortOption> EffortsFor(AgentModelOption? model, AgentBackendKind backend)
    {
        var levels = model?.Efforts ?? DefaultEfforts(backend);

        return [EffortDefault, .. levels.Select(level => new AgentEffortOption(level, EffortTitle(level)))];
    }

    public static IReadOnlyList<string> DefaultEfforts(AgentBackendKind backend)
    {
        return backend switch
        {
            AgentBackendKind.Codex => CodexEfforts,
            AgentBackendKind.OpenCode => OpenCodeVariants,
            _ => ClaudeEfforts,
        };
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

    internal static IReadOnlyList<AgentModelOption> ParseOpenCodeModels(string output)
    {
        var models = new List<AgentModelOption>();
        var block = new StringBuilder();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (block.Length == 0 && line != "{")
            {
                continue;
            }

            block.AppendLine(line);

            if (line != "}")
            {
                continue;
            }

            if (ReadOpenCodeModel(block.ToString()) is { } option)
            {
                models.Add(option);
            }

            block.Clear();
        }

        return models;
    }

    private static AgentModelOption? ReadOpenCodeModel(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var model = document.RootElement;

            if (model.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = Text(model, "id");
            var provider = Text(model, "providerID");

            if (id.Length == 0 || provider.Length == 0)
            {
                return null;
            }

            var title = Text(model, "name") is { Length: > 0 } name ? name : id;

            return new($"{provider}/{id}", title, DescribeOpenCodeModel(model)) { Efforts = Variants(model) };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DescribeOpenCodeModel(JsonElement model)
    {
        var parts = new List<string>();

        if (model.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
        {
            var input = Amount(cost, "input");
            var output = Amount(cost, "output");

            parts.Add(input == 0 && output == 0
                ? "Бесплатная"
                : $"${input:0.##} за миллион входных, ${output:0.##} за миллион выходных");
        }

        if (model.TryGetProperty("limit", out var limit)
            && limit.ValueKind == JsonValueKind.Object
            && limit.TryGetProperty("context", out var context)
            && context.ValueKind == JsonValueKind.Number)
        {
            parts.Add($"контекст {context.GetInt64():N0}");
        }

        return string.Join(", ", parts);
    }

    private static IReadOnlyList<string>? Variants(JsonElement model)
    {
        if (!model.TryGetProperty("variants", out var variants) || variants.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return [.. variants.EnumerateObject().Select(variant => variant.Name)];
    }

    private static double Amount(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
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

    private static IReadOnlyList<string>? ReasoningLevels(JsonElement model)
    {
        if (!model.TryGetProperty("supported_reasoning_levels", out var levels) || levels.ValueKind != JsonValueKind.Array)
        {
            return null;
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
