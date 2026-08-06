namespace SpaceSnoop.Core.Domain;

public class FileSpace : SpaceBase
{
    private FileSpace(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime, long size)
        : base(name, parent, creationDate, lastAccessTime)
    {
        Size = size;
    }

    /// <summary>
    /// Инициализирует новый экземпляр класса FileSpace.
    /// </summary>
    /// <param name="info">Системная информация.</param>
    /// <param name="parent">Родительская директория.</param>
    public static FileSpace Create(FileInfo info, SpaceBase? parent)
    {
        return new(info.Name, parent, info.CreationTime, info.LastAccessTime, info.Length);
    }

    internal static FileSpace Create(in ScanEntry entry, SpaceBase? parent)
    {
        return new(entry.Name, parent, entry.CreationTime, entry.LastAccessTime, entry.Length);
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
