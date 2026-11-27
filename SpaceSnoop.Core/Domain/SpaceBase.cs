namespace SpaceSnoop.Core.Domain;

public abstract class SpaceBase
{
    protected long CreationRaw;
    protected long LastAccessRaw;
    protected int NodeTag = -1;
    protected byte Flags;

    private SpaceBase? _parent;
    private long _size;
    private SpaceState _state = SpaceState.Added;

    /// <summary>
    /// Название директории.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Родительская директория.
    /// </summary>
    public SpaceBase? Parent
    {
        get => GetParent();
        protected set => SetParent(value);
    }

    /// <summary>
    /// Полный путь до директории.
    /// </summary>
    public string AbsolutePath => GetAbsolutePath();

    /// <summary>
    /// Дата создания директории.
    /// </summary>
    public virtual DateTime CreationDate => TimestampEncoding.ToDateTime(CreationRaw, Flags);

    /// <summary>
    /// Время последнего доступа к директории.
    /// </summary>
    public virtual DateTime LastAccessTime => TimestampEncoding.ToDateTime(LastAccessRaw, Flags);

    /// <summary>
    /// Размер.
    /// </summary>
    public long Size
    {
        get => GetSize();
        protected set => SetSize(value);
    }

    /// <summary>
    /// Занимаемый размер.
    /// </summary>
    public virtual long TotalSize => Size;

    /// <summary>
    /// Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => SizeFormatter.Format(Size);

    public SpaceState State
    {
        get => GetState();
        private set => SetState(value);
    }

    public bool IsDeleted => State == SpaceState.Deleted;

    protected SpaceBase(SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
        : this(parent, TimestampEncoding.FromDateTime(creationDate), TimestampEncoding.FromDateTime(lastAccessTime), 0)
    {
    }

    protected SpaceBase(SpaceBase? parent, long creationRaw, long lastAccessRaw, byte flags)
    {
        _parent = parent;
        CreationRaw = creationRaw;
        LastAccessRaw = lastAccessRaw;
        Flags = flags;
    }

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

    protected virtual SpaceBase? GetParent()
    {
        return _parent;
    }

    protected virtual void SetParent(SpaceBase? value)
    {
        _parent = value;
    }

    protected virtual SpaceState GetState()
    {
        return _state;
    }

    protected virtual void SetState(SpaceState value)
    {
        _state = value;
    }

    protected virtual long GetSize()
    {
        return _size;
    }

    protected virtual void SetSize(long value)
    {
        _size = value;
    }

    protected virtual void RestoreInner()
    {
    }

    protected virtual void DeleteInner()
    {
    }

    protected virtual string GetAbsolutePath()
    {
        var depth = 0;

        for (var current = this; current is not null; current = current.Parent)
        {
            depth++;
        }

        var segments = new string[depth];
        var index = depth;

        for (var current = this; current is not null; current = current.Parent)
        {
            segments[--index] = current.Name;
        }

        return Path.Combine(segments);
    }
}
