using System.Security.Cryptography;

namespace SpaceSnoop.Core;

public static class FileHasher
{
    public static string ComputeHash(string filePath, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        using var stream = File.OpenRead(filePath);
        var hashBytes = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hashBytes);
    }
}
