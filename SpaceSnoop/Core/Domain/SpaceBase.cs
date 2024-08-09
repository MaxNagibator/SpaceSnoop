namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase(string name, string path, DateTime creationDate, DateTime lastAccessTime)
{
    protected readonly ISizeFormatter SizeFormatter = new SizeFormatter();

    /// <summary>
    ///     Название директории.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    ///     Полный путь до директории.
    /// </summary>
    public string Path { get; } = path;

    /// <summary>
    ///     Дата создания директории.
    /// </summary>
    public DateTime CreationDate { get; } = creationDate;

    /// <summary>
    ///     Время последнего доступа к директории.
    /// </summary>
    public DateTime LastAccessTime { get; } = lastAccessTime;

    /// <summary>
    ///     Размер файлов в директории, исключая подкаталоги.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    ///     Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => SizeFormatter.Format(Size);

    /// <summary>
    ///     Возвращает строку, представляющую информацию о директории для tooltip.
    /// </summary>
    /// <returns>Информация о директории в формате tooltip.</returns>
    public virtual string GetTooltipText()
    {
        return $"""
                Название: {Name}
                Путь: {Path} 
                Дата создания: {CreationDate} 
                Последний доступ: {LastAccessTime}
                """;
    }
}
