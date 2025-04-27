namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase(string absolutePath, DateTime creationDate, DateTime lastAccessTime)
{
    protected static readonly SizeFormatter SizeFormatter = new();

    /// <summary>
    /// Название директории.
    /// </summary>
    public string Name => GetFileName();

    /// <summary>
    /// Полный путь до директории.
    /// </summary>
    public string AbsolutePath { get; } = absolutePath;

    /// <summary>
    /// Дата создания директории.
    /// </summary>
    public DateTime CreationDate { get; } = creationDate;

    /// <summary>
    /// Время последнего доступа к директории.
    /// </summary>
    public DateTime LastAccessTime { get; } = lastAccessTime;

    /// <summary>
    /// Размер файлов в директории, исключая подкаталоги.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    /// Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => SizeFormatter.Format(Size);

    /// <summary>
    /// Возвращает строку, представляющую информацию о директории для tooltip.
    /// </summary>
    /// <returns>Информация о директории в формате tooltip.</returns>
    public virtual string GetTooltipText()
    {
        return $"""
                Название: {Name}
                Путь: {AbsolutePath} 
                Дата создания: {CreationDate} 
                Последний доступ: {LastAccessTime}
                """;
    }

    private string GetFileName()
    {
        var fileName = Path.GetFileName(AbsolutePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = Path.GetPathRoot(AbsolutePath);
        }

        return fileName ?? "Error";
    }
}
