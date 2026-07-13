using System.Globalization;

namespace SpaceSnoop.Core.Git;

public sealed record GitCommit(string ShortHash, string Subject, string Author, DateTimeOffset? CommittedAt)
{
    public static IReadOnlyList<GitCommit> ParseLog(string logOutput)
    {
        if (string.IsNullOrEmpty(logOutput))
        {
            return [];
        }

        var commits = new List<GitCommit>();

        foreach (var raw in logOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            var hash = parts[0].Trim();

            if (hash.Length == 0)
            {
                continue;
            }

            DateTimeOffset? when = parts.Length > 1
                && DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    ? parsed
                    : null;

            var author = parts.Length > 2 ? parts[2] : string.Empty;
            var subject = parts.Length > 3 ? string.Join('\t', parts[3..]) : string.Empty;

            commits.Add(new(hash, subject, author, when));
        }

        return commits;
    }
}
