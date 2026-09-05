using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class UpdateCheck
{
    public static bool IsSelfContained()
    {
        var path = Environment.ProcessPath;

        // TODO: размер exe – единственный надёжный признак self-contained для single-file (рантайм встроен, рядом hostfxr нет); порог в AppDefaults
        return path is not null && File.Exists(path) && new FileInfo(path).Length > AppDefaults.SelfContainedExeThreshold;
    }

    public static bool IsNewer(string? latestTag, string? currentVersion)
    {
        return TryParse(latestTag, out var latest)
               && TryParse(currentVersion, out var current)
               && latest > current;
    }

    public static string? PickAsset(IEnumerable<string> assetNames, string productName, bool selfContained)
    {
        var prefix = productName + "-v";

        foreach (var name in assetNames)
        {
            if (!IsProduct(name))
            {
                continue;
            }

            var isPortable = name.EndsWith("-portable.zip", StringComparison.OrdinalIgnoreCase);

            if (selfContained && isPortable)
            {
                return name;
            }

            if (!selfContained && !isPortable && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;

        bool IsProduct(string name)
        {
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
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
