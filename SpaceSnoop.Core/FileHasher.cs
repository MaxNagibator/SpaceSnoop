using System.Security.Cryptography;

namespace SpaceSnoop.Core;

public static class FileHasher
{
    public static string ComputeHash(string filePath, CancellationToken cancel)
    {
        using var stream = File.OpenRead(filePath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;

        while ((read = stream.Read(buffer)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
