namespace SpaceSnoop.Core.Domain;

public class FileSpace : SpaceBase
{
    public FileSpace(string name, string path, DateTime creationDate, DateTime lastAccessTime, long size)
        : base(name, path, creationDate, lastAccessTime)
    {
        Size = size;
    }

    public static FileSpace Create(FileInfo info)
    {
        return new FileSpace(info.Name, info.FullName, info.CreationTime, info.LastAccessTime, info.Length);
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
