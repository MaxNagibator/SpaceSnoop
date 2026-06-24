using System.Diagnostics.CodeAnalysis;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class UpdateCheck
{
    public static bool IsNewer(string? latestTag, string? currentVersion)
    {
        return TryParse(latestTag, out var latest)
               && TryParse(currentVersion, out var current)
               && latest > current;
    }

    private static bool TryParse(string? raw, [NotNullWhen(true)] out Version? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim().TrimStart('v', 'V');

        var dash = trimmed.IndexOf('-');
        if (dash >= 0)
        {
            trimmed = trimmed[..dash];
        }

        return Version.TryParse(trimmed, out version);
    }
}
