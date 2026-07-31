using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Docker;

public readonly record struct DockerFileMetrics(long LogicalBytes, long AllocatedBytes);

public interface IDockerFileSystem
{
    bool FileExists(string path);

    bool IsAvailable(string path);

    DockerFileMetrics GetMetrics(string path);
}

public sealed class WindowsDockerFileSystem : IDockerFileSystem
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool IsAvailable(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public DockerFileMetrics GetMetrics(string path)
    {
        var logicalBytes = new FileInfo(path).Length;
        var allocatedBytes = GetAllocatedBytes(path);
        return new(logicalBytes, allocatedBytes);
    }

    private static long GetAllocatedBytes(string path)
    {
        var low = GetCompressedFileSize(path, out var high);
        if (low == uint.MaxValue)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
            {
                throw new Win32Exception(error);
            }
        }

        return ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);
}
