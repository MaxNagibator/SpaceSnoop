using System.Security.Cryptography;
using System.Text;

namespace SpaceSnoop.Wpf.Mcp;

internal static class McpAuth
{
    public const string TokenHeader = "X-SpaceSnoop-Token";

    private const string BearerPrefix = "Bearer ";

    public static bool IsAuthorized(string? authorization, string? tokenHeader, string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        if (authorization is not null
            && authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            && Matches(authorization[BearerPrefix.Length..].Trim(), token))
        {
            return true;
        }

        return Matches(tokenHeader, token);
    }

    public static bool IsOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }

    private static bool Matches(string? candidate, string token)
    {
        if (candidate is null)
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(candidate);
        var right = Encoding.UTF8.GetBytes(token);

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
