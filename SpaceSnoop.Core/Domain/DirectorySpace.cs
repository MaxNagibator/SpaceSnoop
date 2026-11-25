namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace : SpaceBase
{
    private const int UnknownCount = -1;
    private static readonly Lock StoreAttachLock = new();

    private DirectorySpace[] _subDirectories = [];
    private object _nameOrStore;
    private long _totalSize;
    private int _totalFileCount = UnknownCount;
    private int _totalDirectoryCount = UnknownCount;
    private int _fileStart;
    private int _fileCount;

    public override string Name => Volatile.Read(ref _nameOrStore) is ScanTreeStore store ? store.Names.GetString(NodeTag) : (string)_nameOrStore;

    /// <summary>
    /// Общий размер всех файлов в директории, включая подкаталоги.
    /// </summary>
    public override long TotalSize => _totalSize;

    /// <summary>
    /// Общий размер всех файлов в директории в виде строки с суффиксом размера.
    /// </summary>
    public string TotalSizeText => SizeFormatter.Format(_totalSize);

    /// <summary>
    /// Список файлов.
    /// </summary>
    public IReadOnlyList<FileSpace> Files
    {
        get
        {
            if (Volatile.Read(ref _fileCount) == 0 || _nameOrStore is not ScanTreeStore store)
            {
                return Array.Empty<FileSpace>();
            }

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
    public IReadOnlyList<DirectorySpace> SubDirectories => _subDirectories;

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

    internal int FileCount => _fileCount;

    internal ReadOnlySpan<FileEntry> FileEntries =>
        _fileCount > 0 && _nameOrStore is ScanTreeStore store
            ? store.GetSpan(_fileStart, _fileCount)
            : ReadOnlySpan<FileEntry>.Empty;

    internal DirectorySpace[] SubDirectoriesArray => _subDirectories;

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
                if (Volatile.Read(ref _nameOrStore) is not ScanTreeStore store
                    || !store.RemoveFile(this, _fileStart, ref _fileCount, file, out removedTotalSize))
                {
                    return;
                }

                Size -= removedTotalSize;
                break;

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

        Parent = new DirectorySpace(parent.FullName, null, parent.CreationTime, parent.LastAccessTime);
    }

    internal FileSpace GetFile(int index)
    {
        if (Volatile.Read(ref _nameOrStore) is not ScanTreeStore store)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        lock (store.FacadeLock)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _fileCount);

            return store.GetOrCreateFacadeLocked(this, index);
        }
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
        if (_nameOrStore is ScanTreeStore store)
        {
            return store.Names.Equals(NodeTag, value, comparison);
        }

        return ((string)_nameOrStore).AsSpan().Equals(value, comparison);
    }

    internal ref FileEntry GetFileEntryRef(int index)
    {
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
            DirectorySpace subDir => Array.IndexOf(_subDirectories, subDir) >= 0,
            FileSpace file => file.IsAttachedTo(this),
            _ => false,
        };
    }

    protected override void DeleteInner()
    {
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
