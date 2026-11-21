namespace SpaceSnoop.Core.Domain;

public class FileSpace : SpaceBase
{
    private DirectorySpace? _owner;
    private string? _standaloneName;

    public override string Name
    {
        get
        {
            var owner = Volatile.Read(ref _owner);

            if (owner?.Store is { } store)
            {
                lock (store.FacadeLock)
                {
                    if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                    {
                        return owner.GetFileName(NodeTag);
                    }
                }
            }

            return _standaloneName!;
        }
    }

    public override DateTime CreationDate
    {
        get
        {
            var owner = Volatile.Read(ref _owner);

            if (owner?.Store is { } store)
            {
                lock (store.FacadeLock)
                {
                    if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                    {
                        return owner.GetFileEntryRef(NodeTag).GetCreationDate();
                    }
                }
            }

            return base.CreationDate;
        }
    }

    public override DateTime LastAccessTime
    {
        get
        {
            var owner = Volatile.Read(ref _owner);

            if (owner?.Store is { } store)
            {
                lock (store.FacadeLock)
                {
                    if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                    {
                        return owner.GetFileEntryRef(NodeTag).GetLastAccessTime();
                    }
                }
            }

            return base.LastAccessTime;
        }
    }

    internal int SlabIndex => NodeTag;

    internal FileSpace(DirectorySpace owner, int slabIndex)
        : base(owner, 0L, 0L, 0)
    {
        _owner = owner;
        NodeTag = slabIndex;
    }

    private FileSpace(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime, long size)
        : base(parent, creationDate, lastAccessTime)
    {
        _standaloneName = name;
        NodeTag = -1;
        base.SetSize(size);
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

    internal static FileSpace Create(in ScanEntry entry, SpaceBase? parent)
    {
        return new(entry.Name, parent, entry.CreationTime, entry.LastAccessTime, entry.Length);
    }

    internal bool IsAttachedTo(DirectorySpace dir)
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                return NodeTag >= 0 && ReferenceEquals(_owner, dir);
            }
        }

        return false;
    }

    internal bool IsAttachedToLocked(DirectorySpace dir)
    {
        return NodeTag >= 0 && ReferenceEquals(_owner, dir);
    }

    internal void BindToSlab(DirectorySpace owner, int slabIndex)
    {
        NodeTag = slabIndex;
        Volatile.Write(ref _owner, owner);
        _standaloneName = null;
    }

    internal void UpdateSlabIndex(int newIndex)
    {
        NodeTag = newIndex;
    }

    internal void Detach(string name, in FileEntry entry)
    {
        _standaloneName = name;
        CreationRaw = entry.CreationRaw;
        LastAccessRaw = entry.LastAccessRaw;
        Flags = entry.Flags;
        base.SetSize(entry.Size);
        base.SetState(entry.State);
        Volatile.Write(ref _owner, null);
        NodeTag = -1;
    }

    internal long GetCreationRaw()
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    return owner.GetFileEntryRef(NodeTag).CreationRaw;
                }
            }
        }

        return CreationRaw;
    }

    internal long GetLastAccessRaw()
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    return owner.GetFileEntryRef(NodeTag).LastAccessRaw;
                }
            }
        }

        return LastAccessRaw;
    }

    internal byte GetFlags()
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    return owner.GetFileEntryRef(NodeTag).Flags;
                }
            }
        }

        return Flags;
    }

    protected override SpaceState GetState()
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    return owner.GetFileEntryRef(NodeTag).State;
                }
            }
        }

        return base.GetState();
    }

    protected override void SetState(SpaceState value)
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    owner.GetFileEntryRef(NodeTag).State = value;
                    return;
                }
            }
        }

        base.SetState(value);
    }

    protected override long GetSize()
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    return owner.GetFileEntryRef(NodeTag).Size;
                }
            }
        }

        return base.GetSize();
    }

    protected override void SetSize(long value)
    {
        var owner = Volatile.Read(ref _owner);

        if (owner?.Store is { } store)
        {
            lock (store.FacadeLock)
            {
                if (NodeTag >= 0 && ReferenceEquals(_owner, owner))
                {
                    owner.GetFileEntryRef(NodeTag).Size = value;
                    return;
                }
            }
        }

        base.SetSize(value);
    }
}
