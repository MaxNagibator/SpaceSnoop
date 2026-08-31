using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed partial class DiagnosticsRedactor(string? userName = null, string? machineName = null)
{
    public const string UserMask = "<ПОЛЬЗОВАТЕЛЬ>";

    public const string MachineMask = "<МАШИНА>";

    public const string Ellipsis = "…";

    private readonly string? _userName = string.IsNullOrWhiteSpace(userName) ? null : userName;

    private readonly string? _machineName = string.IsNullOrWhiteSpace(machineName) ? null : machineName;

    public static DiagnosticsRedactor ForCurrentUser()
    {
        return new(Environment.UserName, Environment.MachineName);
    }

    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var result = PathPattern().Replace(text, match => ShortenPath(match.Value));

        if (_machineName is not null)
        {
            result = ReplaceWord(result, _machineName, MachineMask);
        }

        if (_userName is not null)
        {
            result = ReplaceWord(result, _userName, UserMask);
        }

        return result;
    }

    private static string ShortenPath(string path)
    {
        var prefix = string.Empty;
        var body = path;
        var network = false;
        var extended = ExtendedPrefixPattern().Match(path);

        if (extended.Success)
        {
            prefix = extended.Value;
            body = path[extended.Length..];
            network = extended.Groups["unc"].Success;
        }
        else if (path.StartsWith('\\') || path.StartsWith('/'))
        {
            prefix = SeparatorPattern().Match(path).Value;
            body = path[prefix.Length..];
            network = true;
        }

        var separators = SeparatorPattern().Matches(body);

        if (separators.Count == 0)
        {
            return path;
        }

        var separator = separators[^1].Value;
        var segments = SeparatorPattern().Split(body).Where(static segment => segment.Length > 0).ToArray();
        var rootSegments = network ? 2 : 1;

        if (segments.Length <= rootSegments)
        {
            return path;
        }

        var root = string.Concat(prefix, string.Join(separator, segments.Take(rootSegments)));
        var tail = segments.Skip(rootSegments).ToArray();
        var leaf = tail[^1];

        return tail.Length == 1
            ? string.Concat(root, separator, leaf)
            : string.Concat(root, separator, Ellipsis, separator, leaf);
    }

    private static string ReplaceWord(string text, string word, string mask)
    {
        return Regex.Replace(text, Regex.Escape(word), mask, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
    }

    [GeneratedRegex(
        @"(?:[\\/]{2,4}\?[\\/]{1,2}UNC[\\/]{1,2}[^\\/:*?""<>|\r\n]+|[\\/]{2,4}\?[\\/]{1,2}[A-Za-z]:|[A-Za-z]:|[\\/]{2,4}[^\\/:*?""<>|\r\n]+)(?:[\\/]{1,2}[^\\/:*?""<>|\r\n]+)+",
        RegexOptions.None,
        5000)]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"^[\\/]{2,4}\?[\\/]{1,2}(?<unc>UNC[\\/]{1,2})?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 5000)]
    private static partial Regex ExtendedPrefixPattern();

    [GeneratedRegex(@"[\\/]+", RegexOptions.None, 5000)]
    private static partial Regex SeparatorPattern();
}
