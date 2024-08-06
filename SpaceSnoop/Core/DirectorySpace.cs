namespace SpaceSnoop.Core;

/// <summary>
///     Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace
{
    private readonly List<DirectorySpace> _subDirectories;
    private readonly SizeFormatter _sizeFormatter;
    private long? _maxTotalSize;

    /// <summary>
    ///     Инициализирует новый экземпляр класса DirectorySpace.
    /// </summary>
    /// <param name="name">Название директории.</param>
    /// <param name="path">Полный путь до директории</param>
    /// <param name="creationDate">Дата создания директории.</param>
    /// <param name="lastAccessTime">Время последнего доступа к директории.</param>
    public DirectorySpace(string name, string path, DateTime creationDate, DateTime lastAccessTime)
    {
        _sizeFormatter = new SizeFormatter();
        _subDirectories = [];

        Name = name;
        Path = path;
        CreationDate = creationDate;
        LastAccessTime = lastAccessTime;
    }

    /// <summary>
    ///     Название директории.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Полный путь до директории.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Дата создания директории.
    /// </summary>
    public DateTime CreationDate { get; }

    /// <summary>
    ///     Время последнего доступа к директории.
    /// </summary>
    public DateTime LastAccessTime { get; }

    /// <summary>
    ///     Общий размер всех файлов в директории, включая подкаталоги.
    /// </summary>
    public long TotalSize { get; private set; }

    /// <summary>
    ///     Общий размер всех файлов в директории в виде строки с суффиксом размера.
    /// </summary>
    public string TotalSizeText => _sizeFormatter.Format(TotalSize);

    /// <summary>
    ///     Размер файлов в директории, исключая подкаталоги.
    /// </summary>
    public long Size { get; private set; }

    /// <summary>
    ///     Размер файлов в директории, исключая подкаталоги, в виде строки с суффиксом размера.
    /// </summary>
    public string SizeText => _sizeFormatter.Format(Size);

    /// <summary>
    ///     Список подкаталогов.
    /// </summary>
    public IReadOnlyList<DirectorySpace> SubDirectories => _subDirectories;

    /// <summary>
    ///     Максимальный размер среди подкаталогов.
    /// </summary>
    public long MaxTotalSize => _maxTotalSize ??= GetMaxSize();

    /// <summary>
    ///     Добавляет подкаталог в список подкаталогов и обновляет общий размер директории.
    /// </summary>
    /// <param name="subDirectory">Подкаталог, который нужно добавить.</param>
    public void Add(DirectorySpace subDirectory)
    {
        TotalSize += subDirectory.TotalSize;
        _subDirectories.Add(subDirectory);
        _maxTotalSize = null;
    }

    /// <summary>
    ///     Добавляет файлы в директорию и обновляет размер директории.
    /// </summary>
    /// <param name="files">Список файлов, которые нужно добавить в директорию.</param>
    public void AddFiles(IEnumerable<FileInfo> files)
    {
        foreach (FileInfo file in files)
        {
            Size += file.Length;
        }

        TotalSize = Size;
        _maxTotalSize = null;
    }

    /// <summary>
    ///     Возвращает строку, представляющую информацию о директории для tooltip.
    /// </summary>
    /// <returns>Информация о директории в формате tooltip.</returns>
    public string GetTooltipText()
    {
        return $"""
                Название: {Name}
                Путь: {Path} 
                Дата создания: {CreationDate} 
                Последний доступ: {LastAccessTime}
                Общий размер: {TotalSizeText}
                Размер файлов в директории, исключая подкаталоги: {SizeText}
                """;
    }

    /// <summary>
    ///     Возвращает строковое представление директории.
    /// </summary>
    /// <returns>Строковое представление директории.</returns>
    public override string ToString()
    {
        return $"{Name} [{SizeText}] {TotalSizeText}";
    }

    /// <summary>
    ///     Возвращает максимальный размер среди подкаталогов.
    /// </summary>
    /// <returns>Максимальный размер среди подкаталогов.</returns>
    private long GetMaxSize()
    {
        return _subDirectories.Select(subDirectory => subDirectory.MaxTotalSize)
            .Prepend(TotalSize)
            .Max();
    }
}
