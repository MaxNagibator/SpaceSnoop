namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
{
    protected static readonly SizeFormatter SizeFormatter = new();

    /// <summary>
    /// Родительская директория.
    /// </summary>
    public SpaceBase? Parent { get; } = parent;

    /// <summary>
    /// Название директории.
    /// </summary>
    public string Name { get; } = string.Intern(name);

    /// <summary>
    /// Полный путь до директории.
    /// </summary>
    public string AbsolutePath => GetAbsolutePath();

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

    private string GetAbsolutePath()
    {
        var segments = new List<string>();
        var current = this;

        while (current != null)
        {
            segments.Add(current.Name);
            current = current.Parent;
        }

        var result = segments.ToArray();
        result.AsSpan().Reverse();
        return Path.Combine(result);
    }
}
