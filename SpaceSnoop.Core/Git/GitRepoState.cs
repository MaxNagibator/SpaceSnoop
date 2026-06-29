using System.Globalization;

namespace SpaceSnoop.Core.Git;

public sealed record GitRepoState(
    string Branch,
    bool IsDetached,
    bool HasCommits,
    string ShortHash,
    string Subject,
    DateTimeOffset? CommittedAt,
    int DirtyCount,
    int? Ahead,
    int? Behind)
{
    private const string OidPrefix = "# branch.oid ";
    private const string HeadPrefix = "# branch.head ";
    private const string AbPrefix = "# branch.ab ";

    public bool IsDirty => DirtyCount > 0;

    public bool HasUpstream => Ahead.HasValue && Behind.HasValue;

    public static GitRepoState Parse(string statusOutput, string logOutput)
    {
        var branch = string.Empty;
        var detached = false;
        var hasCommits = true;
        int? ahead = null;
        int? behind = null;
        var dirty = 0;

        foreach (var raw in statusOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] != '#')
            {
                dirty++;
                continue;
            }

            if (line.StartsWith(OidPrefix, StringComparison.Ordinal))
            {
                hasCommits = !line[OidPrefix.Length..].Trim().Equals("(initial)", StringComparison.Ordinal);
            }
            else if (line.StartsWith(HeadPrefix, StringComparison.Ordinal))
            {
                branch = line[HeadPrefix.Length..].Trim();
                detached = branch.Equals("(detached)", StringComparison.Ordinal);
            }
            else if (line.StartsWith(AbPrefix, StringComparison.Ordinal))
            {
                (ahead, behind) = ParseAheadBehind(line[AbPrefix.Length..]);
            }
        }

        var (hash, subject, committedAt) = ParseLog(logOutput);

        if (hash.Length == 0)
        {
            hasCommits = false;
        }

        return new(branch, detached, hasCommits, hash, subject, committedAt, dirty, ahead, behind);
    }

    private static (int? Ahead, int? Behind) ParseAheadBehind(string text)
    {
        int? ahead = null;
        int? behind = null;

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2 || !int.TryParse(token.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            if (token[0] == '+')
            {
                ahead = value;
            }
            else if (token[0] == '-')
            {
                behind = value;
            }
        }

        return (ahead, behind);
    }

    private static (string Hash, string Subject, DateTimeOffset? CommittedAt) ParseLog(string logOutput)
    {
        var line = logOutput.Split('\n', 2)[0].TrimEnd('\r');

        if (line.Length == 0)
        {
            return (string.Empty, string.Empty, null);
        }

        var parts = line.Split('\t');
        var hash = parts.Length > 0 ? parts[0].Trim() : string.Empty;

        DateTimeOffset? when = parts.Length > 1
            && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

        var subject = parts.Length > 2 ? parts[2] : string.Empty;

        return (hash, subject, when);
    }
}
