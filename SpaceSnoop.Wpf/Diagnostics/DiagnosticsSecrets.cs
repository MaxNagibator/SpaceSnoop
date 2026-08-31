using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.Diagnostics;

public static partial class DiagnosticsSecrets
{
    public const string Mask = "<СЕКРЕТ>";

    public static string Redact(string text)
    {
        return string.IsNullOrEmpty(text)
            ? text
            : SecretPattern().Replace(text, static match => string.Concat(match.Groups["head"].Value, '"', Mask, '"', match.Groups["tail"].Value));
    }

    [GeneratedRegex(
        """^(?<head>[^\S\r\n]*"?[A-Za-z0-9_.\-]*(?:token|secret|password|credential|api[_\-]?key)[A-Za-z0-9_.\-]*"?[^\S\r\n]*[:=][^\S\r\n]*)(?<value>[^\r\n]*?)(?<tail>,?[^\S\r\n]*)$""",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        5000)]
    private static partial Regex SecretPattern();
}
