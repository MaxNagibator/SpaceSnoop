namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace : SpaceBase
{
    private readonly List<DirectorySpace> _subDirectories;
    private readonly List<FileSpace> _files;
    private long? _maxTotalSize;
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
    }

    public void FixAbsolutePath(DirectoryInfo directory)
    {
        if (directory.FullName == directory.Root.FullName)
        {
            return;
        }

        var parent = Directory.GetParent(directory.FullName);

        if (parent == null)
        {
            return;
        }

        Parent = new DirectorySpace(parent.FullName, null, parent.CreationTime, parent.LastAccessTime);
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
        if (All.Any(x => x.IsDeleted == false))
        {
            return;
        }

        foreach (var space in All)
        {
            space.Restore();
        }
    }

    /// <summary>
    /// Возвращает максимальный размер среди подкаталогов.
    /// </summary>
    /// <returns>Максимальный размер среди подкаталогов.</returns>
    private long GetMaxSize()
    {
        return _subDirectories.Select(subDirectory => subDirectory.MaxTotalSize)
            .Prepend(_totalSize)
            .Max();
    }
}
