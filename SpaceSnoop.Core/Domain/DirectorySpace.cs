namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace : SpaceBase
{
    private readonly List<DirectorySpace> _subDirectories;
    private readonly List<FileSpace> _files;
    private long? _maxTotalSize;
    private int? _totalFileCount;
    private int? _totalDirectoryCount;
    private long _totalSize;

    /// <summary>
    /// Инициализирует новый экземпляр класса DirectorySpace.
    /// </summary>
    /// <param name="name">Название директории.</param>
    /// <param name="parent">Родительская директория.</param>
    /// <param name="creationDate">Дата создания директории.</param>
    /// <param name="lastAccessTime">Время последнего доступа к директории.</param>
    public DirectorySpace(string name, SpaceBase? parent, DateTime creationDate, DateTime lastAccessTime)
        : base(name, parent, creationDate, lastAccessTime)
    {
        _subDirectories = [];
        _files = [];
    }

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
    public IReadOnlyList<FileSpace> Files => _files;

    /// <summary>
    /// Список подкаталогов.
    /// </summary>
    public IReadOnlyList<DirectorySpace> SubDirectories => _subDirectories;

    /// <summary>
    /// Максимальный размер среди подкаталогов.
    /// </summary>
    public long MaxTotalSize => _maxTotalSize ??= GetMaxSize();

    /// <summary>
    /// Количество всех файлов в директории, включая подкаталоги.
    /// </summary>
    public int TotalFileCount => _totalFileCount ??= Files.Count + _subDirectories.Sum(x => x.TotalFileCount);

    /// <summary>
    /// Количество всех подкаталогов (включая вложенные).
    /// </summary>
    public int TotalDirectoryCount => _totalDirectoryCount ??= _subDirectories.Count + _subDirectories.Sum(x => x.TotalDirectoryCount);

    private IEnumerable<SpaceBase> All => _subDirectories.AsEnumerable<SpaceBase>().Concat(Files);

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
        _subDirectories.Add(subDirectory);
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;
    }

    /// <summary>
    /// Добавляет файлы в директорию и обновляет размер директории.
    /// </summary>
    /// <param name="files">Список файлов, которые нужно добавить в директорию.</param>
    public void AddFiles(Span<FileInfo> files)
    {
        for (var i = 0; i < files.Length; i++)
        {
            _files.Add(FileSpace.Create(files[i], this));
            Size += files[i].Length;
        }

        _totalSize = Size;
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;
    }

    public FileSpace AddFile(FileInfo file)
    {
        var space = FileSpace.Create(file, this);
        _files.Add(space);
        Size += file.Length;
        PropagateAddition(file.Length);
        return space;
    }

    internal void AddScannedFile(in ScanEntry entry)
    {
        _files.Add(FileSpace.Create(entry, this));
        Size += entry.Length;
    }

    internal void AddScannedDirectory(DirectorySpace subDirectory)
    {
        _subDirectories.Add(subDirectory);
    }

    internal void SealFiles()
    {
        _totalSize = Size;
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;
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

                foreach (var subDirectory in node._subDirectories)
                {
                    total += subDirectory._totalSize;
                }

                node._totalSize = total;
                node._maxTotalSize = null;
                node._totalFileCount = null;
                node._totalDirectoryCount = null;
                continue;
            }

            stack.Push((node, true));

            foreach (var subDirectory in node._subDirectories)
            {
                stack.Push((subDirectory, false));
            }
        }
    }

    /// <summary>
    /// Удаляет дочерний элемент из этой директории и вычитает его размер из TotalSize
    /// вверх по цепочке Parent. Пропагация останавливается на синтетическом родителе,
    /// созданном FixAbsolutePath (он не содержит этого узла в своих коллекциях).
    /// </summary>
    /// <param name="child">Дочерний элемент, который нужно удалить.</param>
    public void Remove(SpaceBase child)
    {
        bool removed;

        switch (child)
        {
            case DirectorySpace subDir:
                removed = _subDirectories.Remove(subDir);
                break;

            case FileSpace file:
                removed = _files.Remove(file);

                if (removed)
                {
                    Size -= file.Size;
                }

                break;

            default:
                return;
        }

        if (!removed)
        {
            return;
        }

        _totalSize -= child.TotalSize;
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;

        if (Parent is DirectorySpace parentDir && parentDir.ContainsChild(this))
        {
            parentDir.PropagateRemoval(child.TotalSize);
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

    /// <summary>
    /// Проверяет, является ли указанный элемент прямым дочерним в этой директории.
    /// </summary>
    internal bool ContainsChild(SpaceBase child)
    {
        return child switch
        {
            DirectorySpace subDir => _subDirectories.Contains(subDir),
            FileSpace file => _files.Contains(file),
            _ => false,
        };
    }

    protected override void DeleteInner()
    {
        foreach (var space in All)
        {
            space.Delete();
        }
    }

    protected override void RestoreInner()
    {
        // TODO: Костыль для восстановления только конкретных фалов, не затрагивая другие
        if (All.Any(x => !x.IsDeleted))
        {
            return;
        }

        foreach (var space in All)
        {
            space.Restore();
        }
    }

    /// <summary>
    /// Вычитает размер из TotalSize и продолжает пропагацию вверх по реальным предкам.
    /// </summary>
    private void PropagateRemoval(long size)
    {
        _totalSize -= size;
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;

        if (Parent is DirectorySpace parentDir && parentDir.ContainsChild(this))
        {
            parentDir.PropagateRemoval(size);
        }
    }

    private void PropagateAddition(long size)
    {
        _totalSize += size;
        _maxTotalSize = null;
        _totalFileCount = null;
        _totalDirectoryCount = null;

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
        return _subDirectories.Select(x => x.MaxTotalSize)
            .Prepend(_totalSize)
            .Max();
    }
}
