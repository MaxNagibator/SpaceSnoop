using System.Security.Cryptography;

namespace SpaceSnoop.Core;

public static class FileHasher
{
    public static string ComputeHash(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hashBytes);
    }
}
