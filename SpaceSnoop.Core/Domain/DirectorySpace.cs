namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace : SpaceBase
{
    private const int UnknownCount = -1;
    private const int MaxArenaPathDepth = 512;
    private static readonly Lock StoreAttachLock = new();

    private readonly int _dirIndex = -1;
    private DirectorySpace[] _subDirectories = [];
    private object _nameOrStore;
    private long _totalSize;
    private int _totalFileCount = UnknownCount;
    private int _totalDirectoryCount = UnknownCount;
    private int _fileStart;
    private int _fileCount;

    public override string Name
    {
        get
        {
            if (_dirIndex >= 0)
            {
                var store = (ScanTreeStore)_nameOrStore;
                return store.Names.GetString(store.GetDirectoryRef(_dirIndex).NameId);
            }

            return Volatile.Read(ref _nameOrStore) is ScanTreeStore fallbackStore
                ? fallbackStore.Names.GetString(NodeTag)
                : (string)_nameOrStore;
        }
    }

    public override DateTime CreationDate =>
        _dirIndex >= 0
            ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).GetCreationDate()
            : base.CreationDate;

    public override DateTime LastAccessTime =>
        _dirIndex >= 0
            ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).GetLastAccessTime()
            : base.LastAccessTime;

    /// <summary>
    /// Общий размер всех файлов в директории, включая подкаталоги.
    /// </summary>
    public override long TotalSize =>
        _dirIndex >= 0
            ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).TotalSize
            : _totalSize;

    /// <summary>
    /// Общий размер всех файлов в директории в виде строки с суффиксом размера.
    /// </summary>
    public string TotalSizeText => SizeFormatter.Format(TotalSize);

    /// <summary>
    /// Список файлов.
    /// </summary>
    public IReadOnlyList<FileSpace> Files
    {
        get
        {
            if (_dirIndex >= 0)
            {
                var arenaStore = (ScanTreeStore)_nameOrStore;

                lock (arenaStore.FacadeLock)
                {
                    var count = arenaStore.GetDirectoryRef(_dirIndex).FileCount;

                    if (count == 0)
                    {
                        return Array.Empty<FileSpace>();
                    }

                    var result = new FileSpace[count];

                    for (var i = 0; i < result.Length; i++)
                    {
                        result[i] = arenaStore.GetOrCreateFacadeLocked(this, i);
                    }

                    return result;
                }
            }

            if (Volatile.Read(ref _fileCount) == 0 || Volatile.Read(ref _nameOrStore) is not ScanTreeStore)
            {
                return Array.Empty<FileSpace>();
            }

            var store = (ScanTreeStore)_nameOrStore;

            lock (store.FacadeLock)
            {
                if (_fileCount == 0)
                {
                    return Array.Empty<FileSpace>();
                }

                var result = new FileSpace[_fileCount];

                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = store.GetOrCreateFacadeLocked(this, i);
                }

                return result;
            }
        }
    }

    /// <summary>
    /// Список подкаталогов.
    /// </summary>
    public IReadOnlyList<DirectorySpace> SubDirectories => _dirIndex >= 0 ? BuildArenaSubDirectories() : _subDirectories;

    /// <summary>
    /// Максимальный размер среди подкаталогов.
    /// </summary>
    public long MaxTotalSize => GetMaxSize();

    /// <summary>
    /// Количество всех файлов в директории, включая подкаталоги.
    /// </summary>
    public int TotalFileCount
    {
        get
        {
            if (_dirIndex >= 0)
            {
                var store = (ScanTreeStore)_nameOrStore;
                ref var entry = ref store.GetDirectoryRef(_dirIndex);

                if (entry.TotalFileCount != UnknownCount)
                {
                    return entry.TotalFileCount;
                }

                store.ComputeSubtreeCounts(_dirIndex, out var arenaFiles, out var arenaDirs);
                entry.TotalFileCount = arenaFiles;

                if (entry.TotalDirCount == UnknownCount)
                {
                    entry.TotalDirCount = arenaDirs;
                }

                return arenaFiles;
            }

            if (_totalFileCount != UnknownCount)
            {
                return _totalFileCount;
            }

            var count = _fileCount;

            for (var i = 0; i < _subDirectories.Length; i++)
            {
                count += _subDirectories[i].TotalFileCount;
            }

            _totalFileCount = count;
            return count;
        }
    }

    /// <summary>
    /// Количество всех подкаталогов (включая вложенные).
    /// </summary>
    public int TotalDirectoryCount
    {
        get
        {
            if (_dirIndex >= 0)
            {
                var store = (ScanTreeStore)_nameOrStore;
                ref var entry = ref store.GetDirectoryRef(_dirIndex);

                if (entry.TotalDirCount != UnknownCount)
                {
                    return entry.TotalDirCount;
                }

                store.ComputeSubtreeCounts(_dirIndex, out var arenaFiles, out var arenaDirs);

                if (entry.TotalFileCount == UnknownCount)
                {
                    entry.TotalFileCount = arenaFiles;
                }

                entry.TotalDirCount = arenaDirs;
                return arenaDirs;
            }

            if (_totalDirectoryCount != UnknownCount)
            {
                return _totalDirectoryCount;
            }

            var count = _subDirectories.Length;

            for (var i = 0; i < _subDirectories.Length; i++)
            {
                count += _subDirectories[i].TotalDirectoryCount;
            }

            _totalDirectoryCount = count;
            return count;
        }
    }

    internal int DirIndex => _dirIndex;

    internal int FileCount =>
        _dirIndex >= 0
            ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).FileCount
            : _fileCount;

    internal ReadOnlySpan<FileEntry> FileEntries
    {
        get
        {
            if (_dirIndex >= 0)
            {
                var arenaStore = (ScanTreeStore)_nameOrStore;
                ref readonly var entry = ref arenaStore.GetDirectoryRef(_dirIndex);
                return entry.FileCount > 0 ? arenaStore.GetSpan(entry.FileStart, entry.FileCount) : ReadOnlySpan<FileEntry>.Empty;
            }

            return _fileCount > 0 && _nameOrStore is ScanTreeStore store
                ? store.GetSpan(_fileStart, _fileCount)
                : ReadOnlySpan<FileEntry>.Empty;
        }
    }

    internal DirectorySpace[] SubDirectoriesArray => _dirIndex >= 0 ? BuildArenaSubDirectories() : _subDirectories;

    internal ScanTreeStore? Store => Volatile.Read(ref _nameOrStore) as ScanTreeStore;

    /// <summary>
    /// Инициализирует новый экземпляр класса DirectorySpace.
    /// </summary>
    /// <param name="name">Название директории.</param>
    /// <param name="parent">Родительская директория.</param>
    /// <param name="creationDate">Дата создания директории.</param>
    /// <param name="lastAccessTime">Время последнего доступа к директории.</param>
    public DirectorySpace(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
        : base(parent, creationDate, lastAccessTime)
    {
        _nameOrStore = name;
        NodeTag = -1;
    }

    internal DirectorySpace(ScanTreeStore store, int nameId, SpaceBase? parent, long creationRaw, long lastAccessRaw, byte flags)
        : base(parent, creationRaw, lastAccessRaw, flags)
    {
        _nameOrStore = store;
        NodeTag = nameId;
    }

    internal DirectorySpace(ScanTreeStore store, int dirIndex)
        : base(null, 0L, 0L, 0)
    {
        _nameOrStore = store;
        _dirIndex = dirIndex;
        NodeTag = dirIndex;
    }

    /// <summary>
    /// Инициализирует новый экземпляр класса DirectorySpace.
    /// </summary>
    /// <param name="info">Системная информация.</param>
    /// <param name="parent">Родительская директория.</param>
    public static DirectorySpace Create(DirectoryInfo info, DirectorySpace? parent)
    {
        return new(info.Name, parent, info.CreationTime, info.LastAccessTime);
    }

    /// <summary>
    /// Возвращает строковое представление директории.
    /// </summary>
    /// <returns>Строковое представление директории.</returns>
    public override string ToString()
    {
        return $"{Name} [{SizeText}] {TotalSizeText}";
    }

    public override string GetTooltipText()
    {
        return $"""
            {base.GetTooltipText()}
            Общий размер: {TotalSizeText}
            Размер файлов в директории: {SizeText}
            Вложенных файлов: {TotalFileCount:N0} · каталогов: {TotalDirectoryCount:N0}
            """;
    }

    /// <summary>
    /// Добавляет подкаталог в список подкаталогов и обновляет общий размер директории.
    /// </summary>
    /// <param name="subDirectory">Подкаталог, который нужно добавить.</param>
    public void Add(DirectorySpace subDirectory)
    {
        _totalSize += subDirectory.TotalSize;
        _subDirectories = [.. _subDirectories, subDirectory];
        Invalidate();
    }

    /// <summary>
    /// Добавляет файлы в директорию и обновляет размер директории.
    /// </summary>
    /// <param name="files">Список файлов, которые нужно добавить в директорию.</param>
    public void AddFiles(Span<FileInfo> files)
    {
        if (_dirIndex >= 0)
        {
            if (files.IsEmpty)
            {
                return;
            }

            var arenaStore = (ScanTreeStore)_nameOrStore;
            Span<FileEntry> arenaEntries = files.Length <= 64 ? stackalloc FileEntry[files.Length] : new FileEntry[files.Length];
            var arenaAddedSize = 0L;

            for (var i = 0; i < files.Length; i++)
            {
                var info = files[i];
                arenaEntries[i] = new()
                {
                    Size = info.Length,
                    CreationRaw = TimestampEncoding.FromDateTime(info.CreationTime),
                    LastAccessRaw = TimestampEncoding.FromDateTime(info.LastAccessTime),
                    NameId = arenaStore.Names.Intern(info.Name),
                    State = SpaceState.Added,
                    Flags = 0,
                };
                arenaAddedSize += info.Length;
            }

            arenaStore.AppendFilesAtDirectory(_dirIndex, arenaEntries, arenaAddedSize);
            return;
        }

        if (files.IsEmpty)
        {
            _totalSize = Size;
            Invalidate();
            return;
        }

        var store = EnsureStore();
        Span<FileEntry> entries = files.Length <= 64 ? stackalloc FileEntry[files.Length] : new FileEntry[files.Length];
        var addedSize = 0L;

        for (var i = 0; i < files.Length; i++)
        {
            var info = files[i];
            entries[i] = new()
            {
                Size = info.Length,
                CreationRaw = TimestampEncoding.FromDateTime(info.CreationTime),
                LastAccessRaw = TimestampEncoding.FromDateTime(info.LastAccessTime),
                NameId = store.Names.Intern(info.Name),
                State = SpaceState.Added,
                Flags = 0,
            };
            addedSize += info.Length;
        }

        lock (store.FacadeLock)
        {
            store.AppendFiles(ref _fileStart, ref _fileCount, entries);
            var total = Size + addedSize;
            Size = total;
            _totalSize = total;
            Invalidate();
        }
    }

    public FileSpace AddFile(FileInfo file)
    {
        return AddFile(file.Name, file.Length, file.CreationTime, file.LastAccessTime);
    }

    public FileSpace AddFile(string name, long length, DateTime creationTime, DateTime lastAccessTime)
    {
        if (_dirIndex >= 0)
        {
            var arenaStore = (ScanTreeStore)_nameOrStore;
            var arenaEntry = new FileEntry
            {
                Size = length,
                CreationRaw = TimestampEncoding.FromDateTime(creationTime),
                LastAccessRaw = TimestampEncoding.FromDateTime(lastAccessTime),
                NameId = arenaStore.Names.Intern(name),
                State = SpaceState.Added,
                Flags = 0,
            };

            return arenaStore.AppendFileAtDirectory(this, _dirIndex, in arenaEntry);
        }

        var store = EnsureStore();
        var entry = new FileEntry
        {
            Size = length,
            CreationRaw = TimestampEncoding.FromDateTime(creationTime),
            LastAccessRaw = TimestampEncoding.FromDateTime(lastAccessTime),
            NameId = store.Names.Intern(name),
            State = SpaceState.Added,
            Flags = 0,
        };

        FileSpace space;

        lock (store.FacadeLock)
        {
            var newIndex = _fileCount;
            store.AppendFile(ref _fileStart, ref _fileCount, in entry);
            space = store.GetOrCreateFacadeLocked(this, newIndex);
            Size += length;
        }

        PropagateAddition(length);
        return space;
    }

    /// <summary>
    /// Удаляет дочерний элемент из этой директории и вычитает его размер из TotalSize
    /// вверх по цепочке Parent. Пропагация останавливается на синтетическом родителе,
    /// созданном FixAbsolutePath (он не содержит этого узла в своих коллекциях).
    /// </summary>
    /// <param name="child">Дочерний элемент, который нужно удалить.</param>
    public void Remove(SpaceBase child)
    {
        if (_dirIndex >= 0)
        {
            var arenaStore = (ScanTreeStore)_nameOrStore;

            switch (child)
            {
                case DirectorySpace subDir:
                    arenaStore.RemoveDirectory(_dirIndex, subDir, out _, out _, out _);
                    break;

                case FileSpace file:
                    arenaStore.RemoveFileAtDirectory(this, _dirIndex, file, out _);
                    break;
            }

            return;
        }

        long removedTotalSize;

        switch (child)
        {
            case DirectorySpace subDir:
                if (!Without(ref _subDirectories, subDir))
                {
                    return;
                }

                removedTotalSize = subDir.TotalSize;
                break;

            case FileSpace file:
                if (Volatile.Read(ref _nameOrStore) is ScanTreeStore store
                    && store.RemoveFile(this, _fileStart, ref _fileCount, file, out removedTotalSize))
                {
                    Size -= removedTotalSize;
                    break;
                }

                return;

            default:
                return;
        }

        _totalSize -= removedTotalSize;
        Invalidate();

        if (Parent is DirectorySpace parentDir && parentDir.ContainsChild(this))
        {
            parentDir.PropagateRemoval(removedTotalSize);
        }
    }

    public void FixAbsolutePath(DirectoryInfo directory)
    {
        if (directory.FullName == directory.Root.FullName)
        {
            return;
        }

        var parent = Directory.GetParent(directory.FullName);

        if (parent is null)
        {
            return;
        }

        if (_dirIndex >= 0)
        {
            var store = (ScanTreeStore)_nameOrStore;

            if (store.GetDirectoryRef(_dirIndex).ParentIndex >= 0)
            {
                return;
            }

            store.SetSyntheticParent(_dirIndex, new DirectorySpace(parent.FullName, null, parent.CreationTime, parent.LastAccessTime));
            return;
        }

        Parent = new DirectorySpace(parent.FullName, null, parent.CreationTime, parent.LastAccessTime);
    }

    internal string CombineChildPath(string childName)
    {
        return BuildArenaPath(childName);
    }

    internal bool IsAttachedTo(DirectorySpace parent)
    {
        if (_dirIndex < 0 || parent._dirIndex < 0 || !ReferenceEquals(Store, parent.Store))
        {
            return false;
        }

        var store = (ScanTreeStore)_nameOrStore;

        lock (store.FacadeLock)
        {
            return IsAttachedToLocked(store, parent._dirIndex);
        }
    }

    internal bool IsAttachedToLocked(ScanTreeStore store, int parentIndex)
    {
        if (_dirIndex < 0 || !ReferenceEquals(_nameOrStore, store))
        {
            return false;
        }

        ref readonly var entry = ref store.GetDirectoryRef(_dirIndex);
        return entry.ParentIndex == parentIndex && !entry.IsDetached;
    }

    internal FileSpace GetFile(int index)
    {
        if (Volatile.Read(ref _nameOrStore) is ScanTreeStore store)
        {
            lock (store.FacadeLock)
            {
                var count = _dirIndex >= 0 ? store.GetDirectoryRef(_dirIndex).FileCount : _fileCount;
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);

                return store.GetOrCreateFacadeLocked(this, index);
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal string GetFileName(int index)
    {
        var entries = FileEntries;
        return ((ScanTreeStore)_nameOrStore).Names.GetString(entries[index].NameId);
    }

    internal ReadOnlySpan<byte> GetFileNameUtf8(int index)
    {
        var entries = FileEntries;
        return ((ScanTreeStore)_nameOrStore).Names.GetUtf8(entries[index].NameId);
    }

    internal bool FileNameEquals(int index, ReadOnlySpan<char> value, StringComparison comparison)
    {
        var entries = FileEntries;
        return ((ScanTreeStore)_nameOrStore).Names.Equals(entries[index].NameId, value, comparison);
    }

    internal bool NameEquals(ReadOnlySpan<char> value, StringComparison comparison)
    {
        if (_dirIndex >= 0)
        {
            var arenaStore = (ScanTreeStore)_nameOrStore;
            return arenaStore.Names.Equals(arenaStore.GetDirectoryRef(_dirIndex).NameId, value, comparison);
        }

        if (_nameOrStore is ScanTreeStore store)
        {
            return store.Names.Equals(NodeTag, value, comparison);
        }

        return ((string)_nameOrStore).AsSpan().Equals(value, comparison);
    }

    internal ref FileEntry GetFileEntryRef(int index)
    {
        if (_dirIndex >= 0)
        {
            var arenaStore = (ScanTreeStore)_nameOrStore;
            ref readonly var entry = ref arenaStore.GetDirectoryRef(_dirIndex);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, entry.FileCount);
            return ref arenaStore.GetWritableSpan(entry.FileStart, entry.FileCount)[index];
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _fileCount);

        return ref ((ScanTreeStore)_nameOrStore).GetWritableSpan(_fileStart, _fileCount)[index];
    }

    internal void SetScanned(ScanTreeStore store, int fileStart, int fileCount, long ownSize, DirectorySpace[] subDirectories)
    {
        if (!ReferenceEquals(_nameOrStore, store))
        {
            NodeTag = store.Names.Intern((string)_nameOrStore);
            _nameOrStore = store;
        }

        _subDirectories = subDirectories;
        _fileStart = fileStart;
        _fileCount = fileCount;
        Size = ownSize;
        _totalSize = ownSize;
        Invalidate();
    }

    internal void SetScanned(List<FileSpace> files, DirectorySpace[] subDirectories)
    {
        _subDirectories = subDirectories;

        if (files.Count == 0)
        {
            _fileStart = 0;
            _fileCount = 0;
            Size = 0;
            _totalSize = 0;
            Invalidate();
            return;
        }

        var store = EnsureStore();
        var fileStart = store.AllocateLargeArray(files.Count, out var entries);
        var size = 0L;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            entries[i] = new()
            {
                Size = file.Size,
                CreationRaw = file.GetCreationRaw(),
                LastAccessRaw = file.GetLastAccessRaw(),
                NameId = store.Names.Intern(file.Name),
                State = file.State,
                Flags = file.GetFlags(),
            };
            size += file.Size;
            file.BindToSlab(this, i);
            store.RegisterFacade(this, i, file);
        }

        _fileStart = fileStart;
        _fileCount = files.Count;
        Size = size;
        _totalSize = size;
        Invalidate();
    }

    internal void SetAggregatedTotals(long totalSize, int totalFileCount, int totalDirectoryCount)
    {
        if (_dirIndex >= 0)
        {
            ref var entry = ref ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex);
            entry.TotalSize = totalSize;
            entry.TotalFileCount = totalFileCount;
            entry.TotalDirCount = totalDirectoryCount;
            return;
        }

        _totalSize = totalSize;
        _totalFileCount = totalFileCount;
        _totalDirectoryCount = totalDirectoryCount;
    }

    internal void AggregateTotals()
    {
        var stack = new Stack<(DirectorySpace Node, bool Ascending)>();
        stack.Push((this, false));

        while (stack.Count > 0)
        {
            var (node, ascending) = stack.Pop();

            if (ascending)
            {
                var total = node.Size;
                var fileCount = node._fileCount;
                var directoryCount = node._subDirectories.Length;

                for (var i = 0; i < node._subDirectories.Length; i++)
                {
                    var subDirectory = node._subDirectories[i];
                    total += subDirectory._totalSize;
                    fileCount += subDirectory.TotalFileCount;
                    directoryCount += subDirectory.TotalDirectoryCount;
                }

                node._totalSize = total;
                node._totalFileCount = fileCount;
                node._totalDirectoryCount = directoryCount;
                continue;
            }

            stack.Push((node, true));

            for (var i = 0; i < node._subDirectories.Length; i++)
            {
                stack.Push((node._subDirectories[i], false));
            }
        }
    }

    /// <summary>
    /// Проверяет, является ли указанный элемент прямым дочерним в этой директории.
    /// </summary>
    internal bool ContainsChild(SpaceBase child)
    {
        return child switch
        {
            DirectorySpace subDir => _dirIndex >= 0 ? subDir.IsAttachedTo(this) : Array.IndexOf(_subDirectories, subDir) >= 0,
            FileSpace file => file.IsAttachedTo(this),
            _ => false,
        };
    }

    internal bool HasMarkedSelfOrChild()
    {
        if (_dirIndex >= 0)
        {
            return ((ScanTreeStore)_nameOrStore).HasMarkedInSubtree(_dirIndex);
        }

        if (IsDeleted)
        {
            return true;
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            if (_subDirectories[i].HasMarkedSelfOrChild())
            {
                return true;
            }
        }

        var files = FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                return true;
            }
        }

        return false;
    }

    internal void CollectMarked(List<SpaceBase> destination)
    {
        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).CollectMarkedInSubtree(_dirIndex, destination);
            return;
        }

        if (IsDeleted)
        {
            destination.Add(this);
            return;
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            _subDirectories[i].CollectMarked(destination);
        }

        var files = FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                destination.Add(GetFile(i));
            }
        }
    }

    internal void RestoreRecursive()
    {
        if (IsDeleted)
        {
            Restore();
        }

        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).RestoreDirectoryRecursive(_dirIndex);
            return;
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            _subDirectories[i].RestoreRecursive();
        }

        var files = FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                GetFile(i).Restore();
            }
        }
    }

    internal bool HasAddedChild()
    {
        if (_dirIndex >= 0)
        {
            return ((ScanTreeStore)_nameOrStore).HasAddedChildInDirectory(_dirIndex);
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            if (_subDirectories[i].State == SpaceState.Added)
            {
                return true;
            }
        }

        var files = FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].State == SpaceState.Added)
            {
                return true;
            }
        }

        return false;
    }

    internal bool HasMarkedChild()
    {
        if (_dirIndex >= 0)
        {
            return ((ScanTreeStore)_nameOrStore).HasMarkedChildInDirectory(_dirIndex);
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            if (_subDirectories[i].HasMarkedSelfOrChild())
            {
                return true;
            }
        }

        var files = FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].IsDeleted)
            {
                return true;
            }
        }

        return false;
    }

    protected override SpaceBase? GetParent()
    {
        if (_dirIndex < 0)
        {
            return base.GetParent();
        }

        var store = (ScanTreeStore)_nameOrStore;
        var parentIndex = store.GetDirectoryRef(_dirIndex).ParentIndex;
        return parentIndex >= 0 ? store.GetOrCreateDirectoryFacade(parentIndex) : store.GetSyntheticParent(_dirIndex);
    }

    protected override void SetParent(SpaceBase? value)
    {
        if (_dirIndex < 0)
        {
            base.SetParent(value);
            return;
        }

        var store = (ScanTreeStore)_nameOrStore;

        if (store.GetDirectoryRef(_dirIndex).ParentIndex < 0)
        {
            store.SetSyntheticParent(_dirIndex, value);
        }
    }

    protected override string GetAbsolutePath()
    {
        return _dirIndex >= 0 ? BuildArenaPath(leafName: null) : base.GetAbsolutePath();
    }

    protected override SpaceState GetState()
    {
        return _dirIndex >= 0 ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).State : base.GetState();
    }

    protected override void SetState(SpaceState value)
    {
        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).State = value;
            return;
        }

        base.SetState(value);
    }

    protected override long GetSize()
    {
        return _dirIndex >= 0 ? ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).Size : base.GetSize();
    }

    protected override void SetSize(long value)
    {
        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).GetDirectoryRef(_dirIndex).Size = value;
            return;
        }

        base.SetSize(value);
    }

    protected override void DeleteInner()
    {
        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).DeleteDirectorySubtree(_dirIndex);
            return;
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            _subDirectories[i].Delete();
        }

        var files = GetWritableFileEntries();

        for (var i = 0; i < files.Length; i++)
        {
            ref var entry = ref files[i];

            if (entry.State != SpaceState.Deleted && entry.State != SpaceState.Error)
            {
                entry.State = SpaceState.Deleted;
            }
        }
    }

    protected override void RestoreInner()
    {
        if (_dirIndex >= 0)
        {
            ((ScanTreeStore)_nameOrStore).RestoreDirectorySubtree(_dirIndex);
            return;
        }

        // TODO: Костыль для восстановления только конкретных фалов, не затрагивая другие
        for (var i = 0; i < _subDirectories.Length; i++)
        {
            if (!_subDirectories[i].IsDeleted)
            {
                return;
            }
        }

        var files = GetWritableFileEntries();

        for (var i = 0; i < files.Length; i++)
        {
            if (!files[i].IsDeleted)
            {
                return;
            }
        }

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            _subDirectories[i].Restore();
        }

        for (var i = 0; i < files.Length; i++)
        {
            ref var entry = ref files[i];

            if (entry.State == SpaceState.Deleted)
            {
                entry.State = SpaceState.Added;
            }
        }
    }

    private DirectorySpace[] BuildArenaSubDirectories()
    {
        var store = (ScanTreeStore)_nameOrStore;

        lock (store.FacadeLock)
        {
            ref readonly var entry = ref store.GetDirectoryRef(_dirIndex);

            if (entry.SubDirCount == 0)
            {
                return [];
            }

            var result = new DirectorySpace[entry.SubDirCount];
            var i = 0;

            for (var child = entry.FirstChildDir; child >= 0; child = store.GetDirectoryRef(child).NextSiblingDir)
            {
                result[i++] = store.GetOrCreateDirectoryFacadeLocked(child);
            }

            return result;
        }
    }

    private string BuildArenaPath(string? leafName)
    {
        var store = (ScanTreeStore)_nameOrStore;
        string[] segments;

        lock (store.FacadeLock)
        {
            var arenaDepth = 0;
            var lastDir = _dirIndex;

            for (var cur = _dirIndex; cur >= 0 && arenaDepth < MaxArenaPathDepth; cur = store.GetDirectoryRef(cur).ParentIndex)
            {
                arenaDepth++;
                lastDir = cur;
            }

            var synthetic = store.GetSyntheticParentLocked(lastDir);
            var syntheticDepth = 0;

            for (var p = synthetic; p is not null && syntheticDepth < MaxArenaPathDepth; p = p.Parent)
            {
                syntheticDepth++;
            }

            var extra = leafName is null ? 0 : 1;
            segments = new string[syntheticDepth + arenaDepth + extra];
            var index = segments.Length;

            if (leafName is not null)
            {
                segments[--index] = leafName;
            }

            var step = 0;

            for (var cur = _dirIndex; cur >= 0 && step < arenaDepth; cur = store.GetDirectoryRef(cur).ParentIndex)
            {
                segments[--index] = store.Names.GetString(store.GetDirectoryRef(cur).NameId);
                step++;
            }

            step = 0;

            for (var p = synthetic; p is not null && step < syntheticDepth; p = p.Parent)
            {
                segments[--index] = p.Name;
                step++;
            }
        }

        return Path.Combine(segments);
    }

    private ScanTreeStore EnsureStore()
    {
        if (Volatile.Read(ref _nameOrStore) is ScanTreeStore existing)
        {
            return existing;
        }

        lock (StoreAttachLock)
        {
            if (_nameOrStore is ScanTreeStore current)
            {
                return current;
            }

            ScanTreeStore? store = null;
            var ancestor = Parent as DirectorySpace;

            while (ancestor is not null)
            {
                if (Volatile.Read(ref ancestor._nameOrStore) is ScanTreeStore ancestorStore)
                {
                    store = ancestorStore;
                    break;
                }

                ancestor = ancestor.Parent as DirectorySpace;
            }

            store ??= new ScanTreeStore();
            NodeTag = store.Names.Intern((string)_nameOrStore);
            Volatile.Write(ref _nameOrStore, store);
            return store;
        }
    }

    private Span<FileEntry> GetWritableFileEntries()
    {
        if (_dirIndex >= 0)
        {
            var arenaStore = (ScanTreeStore)_nameOrStore;
            ref readonly var entry = ref arenaStore.GetDirectoryRef(_dirIndex);
            return entry.FileCount > 0 ? arenaStore.GetWritableSpan(entry.FileStart, entry.FileCount) : Span<FileEntry>.Empty;
        }

        return _fileCount > 0 && _nameOrStore is ScanTreeStore store
            ? store.GetWritableSpan(_fileStart, _fileCount)
            : Span<FileEntry>.Empty;
    }

    /// <summary>
    /// Вычитает размер из TotalSize и продолжает пропагацию вверх по реальным предкам.
    /// </summary>
    private void PropagateRemoval(long size)
    {
        _totalSize -= size;
        Invalidate();

        if (Parent is DirectorySpace parentDir && parentDir.ContainsChild(this))
        {
            parentDir.PropagateRemoval(size);
        }
    }

    private void PropagateAddition(long size)
    {
        _totalSize += size;
        Invalidate();

        if (Parent is DirectorySpace parentDir && parentDir.ContainsChild(this))
        {
            parentDir.PropagateAddition(size);
        }
    }

    /// <summary>
    /// Возвращает максимальный размер среди подкаталогов.
    /// </summary>
    /// <returns>Максимальный размер среди подкаталогов.</returns>
    private long GetMaxSize()
    {
        if (_dirIndex >= 0)
        {
            var store = (ScanTreeStore)_nameOrStore;
            var maxArena = store.GetDirectoryRef(_dirIndex).TotalSize;
            var stackArena = new Stack<int>();
            stackArena.Push(_dirIndex);

            while (stackArena.Count > 0)
            {
                ref readonly var entry = ref store.GetDirectoryRef(stackArena.Pop());

                if (entry.TotalSize > maxArena)
                {
                    maxArena = entry.TotalSize;
                }

                for (var child = entry.FirstChildDir; child >= 0; child = store.GetDirectoryRef(child).NextSiblingDir)
                {
                    stackArena.Push(child);
                }
            }

            return maxArena;
        }

        var max = _totalSize;

        if (_subDirectories.Length == 0)
        {
            return max;
        }

        var stack = new Stack<DirectorySpace>();

        for (var i = 0; i < _subDirectories.Length; i++)
        {
            stack.Push(_subDirectories[i]);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (current._totalSize > max)
            {
                max = current._totalSize;
            }

            var subs = current._subDirectories;

            for (var i = 0; i < subs.Length; i++)
            {
                stack.Push(subs[i]);
            }
        }

        return max;
    }

    // TODO: дети – массивы точного размера, поэтому Add, AddFile и Remove копируют массив целиком, O(n) на вызов.
    // Понадобится добавлять или удалять по одному в каталоге на десятки тысяч детей – ёмкость с запасом и счётчик
    private static bool Without<T>(ref T[] items, T item) where T : SpaceBase
    {
        var index = Array.IndexOf(items, item);

        if (index < 0)
        {
            return false;
        }

        var rest = items.Length == 1 ? [] : new T[items.Length - 1];
        Array.Copy(items, rest, index);
        Array.Copy(items, index + 1, rest, index, rest.Length - index);
        items = rest;
        return true;
    }

    private void Invalidate()
    {
        _totalFileCount = UnknownCount;
        _totalDirectoryCount = UnknownCount;
    }
}
