namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
{
    /// <summary>
    /// Родительская директория.
    /// </summary>
    public SpaceBase? Parent { get; protected set; } = parent;

    /// <summary>
    /// Название директории.
    /// </summary>
    public string Name { get; } = name;

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
    /// Размер.
    /// </summary>
    public long Size { get; protected set; }

    /// <summary>
    /// Занимаемый размер.
    /// </summary>
    public virtual long TotalSize => Size;

    /// <summary>
    /// Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => SizeFormatter.Format(Size);

    public SpaceState State { get; private set; } = SpaceState.Added;

    public bool IsDeleted => State == SpaceState.Deleted;

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

    public void Delete()
    {
        if (State == SpaceState.Deleted || State == SpaceState.Error)
        {
            return;
        }

        State = SpaceState.Deleted;

        DeleteInner();
    }

    public void Restore()
    {
        if (State != SpaceState.Deleted || State == SpaceState.Error)
        {
            return;
        }

        State = SpaceState.Added;

        if (Parent is { IsDeleted: true })
        {
            Parent.Restore();
        }

        RestoreInner();
    }

    public void SwapDelete()
    {
        switch (State)
        {
            case SpaceState.Added:
                Delete();
                break;

            case SpaceState.Deleted:
                Restore();
                break;

            case SpaceState.None:
            case SpaceState.Error:
            default:
                break;
        }
    }

    public void Error()
    {
        State = SpaceState.Error;
    }

    protected virtual void RestoreInner()
    {
    }

    protected virtual void DeleteInner()
    {
    }

    private string GetAbsolutePath()
    {
        var segments = new List<string>();
        var current = this;

        while (current is not null)
        {
            segments.Add(current.Name);
            current = current.Parent;
        }

        var result = segments.ToArray();
        result.AsSpan().Reverse();
        return Path.Combine(result);
    }
}
