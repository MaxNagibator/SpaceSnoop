using System.Security;

namespace SpaceSnoop.Core;

public static class PathCase
{
    private const int ProbeLimit = 8;

    public static bool PlatformDefaultIsCaseSensitive { get; } = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

    public static StringComparer ComparerFor(string path)
    {
        return IsCaseSensitive(path) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    public static StringComparer Stricter(StringComparer left, StringComparer right)
    {
        return IsSensitive(left) || IsSensitive(right) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    public static bool IsSensitive(StringComparer comparer)
    {
        return ReferenceEquals(comparer, StringComparer.Ordinal);
    }

    public static bool IsCaseSensitive(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return PlatformDefaultIsCaseSensitive;
            }

            var names = Directory.EnumerateFileSystemEntries(path)
                .Take(ProbeLimit)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            {
                return true;
            }

            var flipped = names.Select(FlipCase).OfType<string>().FirstOrDefault();

            if (flipped is null)
            {
                return PlatformDefaultIsCaseSensitive;
            }

            var probe = Path.Combine(path, flipped);

            return !File.Exists(probe) && !Directory.Exists(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return PlatformDefaultIsCaseSensitive;
        }
    }

    private static string? FlipCase(string name)
    {
        var flipped = string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var symbol = source[i];
                span[i] = char.IsUpper(symbol) ? char.ToLowerInvariant(symbol) : char.ToUpperInvariant(symbol);
            }
        });

        return string.Equals(flipped, name, StringComparison.Ordinal) ? null : flipped;
    }
}
