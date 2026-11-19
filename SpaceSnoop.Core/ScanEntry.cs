namespace SpaceSnoop.Core;

internal readonly struct ScanEntry(
    string name,
    long length,
    DateTime creationTime,
    DateTime lastAccessTime,
    FileAttributes attributes,
    bool isDirectory)
{
    public string Name { get; } = name;

    public long Length { get; } = length;

    public DateTime CreationTime { get; } = creationTime;

    public DateTime LastAccessTime { get; } = lastAccessTime;

    public FileAttributes Attributes { get; } = attributes;

    public bool IsDirectory { get; } = isDirectory;

    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
}
