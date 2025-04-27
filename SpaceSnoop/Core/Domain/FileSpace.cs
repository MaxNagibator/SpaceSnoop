namespace SpaceSnoop.Core.Domain;

public class FileSpace : SpaceBase
{
    public FileSpace(string absolutePath, DateTime creationDate, DateTime lastAccessTime, long size)
        : base(absolutePath, creationDate, lastAccessTime)
    {
        Size = size;
    }

    public static FileSpace Create(FileInfo info)
    {
        return new(info.FullName, info.CreationTime, info.LastAccessTime, info.Length);
    }

    public override string ToString()
    {
        return $"{Name} [{SizeText}]";
    }

    public override string GetTooltipText()
    {
        return $"""
                {base.GetTooltipText()}
                Размер файла: {SizeText}
                """;
    }
}
