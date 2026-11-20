namespace SpaceSnoop.Core;

public static class UncPath
{
    private const string UncPrefix = @"\\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";
    private const string ExtendedUncBare = @"\\?\UNC";
    private const string ExtendedPrefix = @"\\?\";
    private const string DevicePrefix = @"\\.\";
    private const string NtPrefix = @"\??\";

    public static bool IsUnc(string? path)
    {
        return TryExtractBody(path, out _);
    }

    public static bool IsIncompleteUnc(string? path)
    {
        return TryExtractBody(path, out var body) && !TrySplitHostShare(body, out _, out _, out _);
    }

    public static bool IsShareRoot(string? path)
    {
        return TryExtractBody(path, out var body)
            && TrySplitHostShare(body, out _, out _, out var remainder)
            && remainder.Length == 0;
    }

    public static bool TryParseShareRoot(string? path, out string shareRoot)
    {
        if (TryExtractBody(path, out var body)
            && TrySplitHostShare(body, out var host, out var share, out _))
        {
            shareRoot = string.Concat(UncPrefix, host, @"\", share);
            return true;
        }

        shareRoot = string.Empty;
        return false;
    }

    public static string? GetHost(string? path)
    {
        if (!TryExtractBody(path, out var body))
        {
            return null;
        }

        var normalized = NormalizeBody(body, out var hadEmptyHost);

        if (hadEmptyHost || normalized.Length == 0)
        {
            return null;
        }

        var slash = normalized.IndexOf('\\');
        return slash < 0 ? normalized : normalized[..slash];
    }

    public static string? GetShare(string? path)
    {
        if (!TryExtractBody(path, out var body))
        {
            return null;
        }

        return TrySplitHostShare(body, out _, out var share, out _) ? share : null;
    }

    public static string NormalizeUnc(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!TryExtractBody(path, out var body))
        {
            return path.Trim().Replace('/', '\\');
        }

        var normalized = NormalizeBody(body, out _);
        return UncPrefix + normalized;
    }

    private static bool TryExtractBody(string? path, out ReadOnlySpan<char> body)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            body = default;
            return false;
        }

        var trimmed = path.AsSpan().Trim();

        if (trimmed.Equals(ExtendedUncBare, StringComparison.OrdinalIgnoreCase))
        {
            body = [];
            return true;
        }

        if (trimmed.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            body = trimmed[ExtendedUncPrefix.Length..];
            return true;
        }

        if (trimmed.StartsWith(@"\\?/", StringComparison.Ordinal)
            && trimmed.Length >= 8
            && trimmed[4..].StartsWith("UNC", StringComparison.OrdinalIgnoreCase)
            && (trimmed.Length == 7 || trimmed[7] is '\\' or '/'))
        {
            body = trimmed.Length == 7 ? [] : trimmed[8..];
            return true;
        }

        if (trimmed.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(DevicePrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(NtPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(@"\\?/", StringComparison.Ordinal)
            || trimmed.StartsWith(@"\\./", StringComparison.Ordinal))
        {
            body = default;
            return false;
        }

        if (trimmed.StartsWith(UncPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith(@"\/", StringComparison.Ordinal)
            || trimmed.StartsWith(@"/\", StringComparison.Ordinal))
        {
            body = trimmed[2..];
            return true;
        }

        body = default;
        return false;
    }

    private static bool TrySplitHostShare(ReadOnlySpan<char> body, out string host, out string share, out string remainder)
    {
        var normalized = NormalizeBody(body, out var hadEmptyHost);

        if (hadEmptyHost || normalized.Length == 0)
        {
            host = string.Empty;
            share = string.Empty;
            remainder = string.Empty;
            return false;
        }

        var firstSlash = normalized.IndexOf('\\');

        if (firstSlash <= 0)
        {
            host = string.Empty;
            share = string.Empty;
            remainder = string.Empty;
            return false;
        }

        var afterHost = normalized.AsSpan(firstSlash + 1);

        if (afterHost.Length == 0)
        {
            host = string.Empty;
            share = string.Empty;
            remainder = string.Empty;
            return false;
        }

        var secondSlash = afterHost.IndexOf('\\');

        if (secondSlash == 0)
        {
            host = string.Empty;
            share = string.Empty;
            remainder = string.Empty;
            return false;
        }

        host = normalized[..firstSlash];

        if (secondSlash < 0)
        {
            share = afterHost.ToString();
            remainder = string.Empty;
        }
        else
        {
            share = afterHost[..secondSlash].ToString();
            remainder = afterHost[(secondSlash + 1)..].ToString();
        }

        return share.Length > 0;
    }

    private static string NormalizeBody(ReadOnlySpan<char> body, out bool hadEmptyHost)
    {
        hadEmptyHost = body.Length > 0 && body[0] is '\\' or '/';

        if (body.IsEmpty)
        {
            return string.Empty;
        }

        var chars = new char[body.Length];
        var written = 0;
        var previousWasSeparator = false;

        foreach (var ch in body)
        {
            var normalized = ch == '/' ? '\\' : ch;

            if (normalized == '\\')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
            }
            else
            {
                previousWasSeparator = false;
            }

            chars[written++] = normalized;
        }

        return new(chars, 0, written);
    }
}
