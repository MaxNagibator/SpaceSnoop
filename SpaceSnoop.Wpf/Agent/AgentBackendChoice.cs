using System.Collections.Immutable;

namespace SpaceSnoop.Wpf.Agent;

public readonly record struct AgentBackendProbe(AgentBackendKind Kind, AgentCliInfo? Cli)
{
    public bool Found => Cli is not null;
}

public sealed record AgentBackendChoice
{
    public static ImmutableArray<AgentBackendKind> Order { get; } =
        [AgentBackendKind.Claude, AgentBackendKind.Codex, AgentBackendKind.OpenCode];

    public required AgentBackendKind Current { get; init; }

    public required bool CurrentFound { get; init; }

    public required IReadOnlyList<AgentBackendKind> Alternatives { get; init; }

    public AgentBackendKind? Suggested => Alternatives.Count > 0 ? Alternatives[0] : null;

    public bool NothingFound => !CurrentFound && Alternatives.Count == 0;

    public static AgentBackendChoice From(AgentBackendKind current, IReadOnlyList<AgentBackendProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        var found = probes
            .Where(probe => probe.Found)
            .Select(probe => probe.Kind)
            .Distinct()
            .OrderBy(kind => Order.IndexOf(kind))
            .ToList();

        return new()
        {
            Current = current,
            CurrentFound = found.Contains(current),
            Alternatives = [.. found.Where(kind => kind != current)],
        };
    }
}
