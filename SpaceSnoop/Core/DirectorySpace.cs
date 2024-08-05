namespace SpaceSnoop.Core;

/// <summary>
///     Представляет директорию с информацией о размере файлов в ней.
/// </summary>
public class DirectorySpace
{
    private readonly List<DirectorySpace> _subDirectories;
    private readonly SizeFormatter _sizeFormatter;

    /// <summary>
    ///     Инициализирует новый экземпляр класса DirectorySpace.
    /// </summary>
    /// <param name="name">Название директории.</param>
    public DirectorySpace(string name)
    {
        _sizeFormatter = new SizeFormatter();

        Name = name;
        _subDirectories = [];
    }

    /// <summary>
    ///     Имя директории.
    /// </summary>
    public string Name { get; }

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
    ///     Добавляет подкаталог в список подкаталогов и обновляет общий размер директории.
    /// </summary>
    /// <param name="subDirectory">Подкаталог, который нужно добавить.</param>
    public void Add(DirectorySpace subDirectory)
    {
        TotalSize += subDirectory.TotalSize;
        _subDirectories.Add(subDirectory);
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
    }

    public override string ToString()
    {
        return $"{Name} [{SizeText}] {TotalSizeText}";
    }
}
