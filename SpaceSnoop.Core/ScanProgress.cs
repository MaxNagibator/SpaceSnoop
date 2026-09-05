namespace SpaceSnoop.Core;

/// <summary>
/// Потокобезопасный приёмник прогресса сканирования диска. <see cref="DiskSpaceCalculator" />
/// обновляет счётчики по мере обхода дерева (в том числе из нескольких потоков в многопоточном
/// режиме), а UI периодически снимает неблокирующий согласованный срез через <see cref="CreateSnapshot" />.
/// </summary>
/// <remarks>
/// Прогресс намеренно отделён от <see cref="Domain.DirectorySpace" />: счётчики обновляются «на лету»,
/// тогда как итоговые <c>TotalFileCount</c>/<c>TotalDirectoryCount</c> вычисляются обходом готового дерева.
/// </remarks>
public sealed class ScanProgress
{
    private long _directoriesScanned;
    private long _directoriesFailed;
    private long _filesScanned;
    private long _bytesScanned;
    private int _topLevelTotal;
    private int _topLevelCompleted;
    private volatile string _currentPath = string.Empty;

    /// <summary>Каталогов пройдено (включая корень).</summary>
    public long DirectoriesScanned => Interlocked.Read(ref _directoriesScanned);

    /// <summary>Каталогов пропущено из-за ошибки обхода (нет доступа, каталог исчез, слишком длинный путь).</summary>
    public long DirectoriesFailed => Interlocked.Read(ref _directoriesFailed);

    /// <summary>Файлов учтено.</summary>
    public long FilesScanned => Interlocked.Read(ref _filesScanned);

    /// <summary>Просуммированный объём учтённых файлов в байтах.</summary>
    public long BytesScanned => Interlocked.Read(ref _bytesScanned);

    /// <summary>
    /// Отмечает вход в каталог: увеличивает счётчик пройденных каталогов и запоминает текущий путь.
    /// </summary>
    /// <param name="fullName">Полный путь обрабатываемого каталога.</param>
    public void EnterDirectory(string fullName)
    {
        Interlocked.Increment(ref _directoriesScanned);
        _currentPath = fullName;
    }

    /// <summary>Отмечает текущую фазу обхода, не трогая счётчики.</summary>
    /// <param name="stage">Строка для индикатора текущего пути.</param>
    public void Announce(string stage)
    {
        _currentPath = stage;
    }

    /// <summary>
    /// Отмечает каталог, содержимое которого прочитать не удалось: обход продолжается,
    /// но поддерево в итог не попадёт.
    /// </summary>
    public void FailDirectory()
    {
        Interlocked.Increment(ref _directoriesFailed);
    }

    /// <summary>
    /// Учитывает файлы одного каталога одним атомарным шагом.
    /// </summary>
    /// <param name="count">Число файлов в каталоге.</param>
    /// <param name="bytes">Суммарный объём этих файлов в байтах.</param>
    public void AddFiles(int count, long bytes)
    {
        if (count <= 0)
        {
            return;
        }

        Interlocked.Add(ref _filesScanned, count);
        Interlocked.Add(ref _bytesScanned, bytes);
    }

    /// <summary>
    /// Задаёт число подкаталогов верхнего уровня – знаменатель детерминированного прогресса.
    /// Вызывается один раз, когда корневой каталог перечислил непосредственные подкаталоги.
    /// </summary>
    /// <param name="total">Число подкаталогов первого уровня.</param>
    public void SetTopLevelTotal(int total)
    {
        Interlocked.Exchange(ref _topLevelTotal, total);
    }

    /// <summary>Отмечает полное завершение одного подкаталога верхнего уровня.</summary>
    public void CompleteTopLevel()
    {
        Interlocked.Increment(ref _topLevelCompleted);
    }

    /// <summary>
    /// Обнуляет счётчики: обход, начатый и брошенный на полпути, не должен добавлять
    /// свои числа к тому, который его заменяет.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _directoriesScanned, 0);
        Interlocked.Exchange(ref _directoriesFailed, 0);
        Interlocked.Exchange(ref _filesScanned, 0);
        Interlocked.Exchange(ref _bytesScanned, 0);
        Interlocked.Exchange(ref _topLevelTotal, 0);
        Interlocked.Exchange(ref _topLevelCompleted, 0);
        _currentPath = string.Empty;
    }

    /// <summary>
    /// Снимает текущий срез прогресса. Поля читаются независимо, поэтому могут быть слегка
    /// рассогласованы между собой – для индикатора прогресса это допустимо.
    /// </summary>
    public ScanProgressSnapshot CreateSnapshot()
    {
        return new(Interlocked.Read(ref _directoriesScanned),
            Interlocked.Read(ref _directoriesFailed),
            Interlocked.Read(ref _filesScanned),
            Interlocked.Read(ref _bytesScanned),
            Volatile.Read(ref _topLevelTotal),
            Volatile.Read(ref _topLevelCompleted),
            _currentPath);
    }
}

/// <summary>
/// Неизменяемый срез состояния сканирования на момент вызова <see cref="ScanProgress.CreateSnapshot" />.
/// </summary>
/// <param name="DirectoriesScanned">Каталогов пройдено.</param>
/// <param name="DirectoriesFailed">Каталогов пропущено из-за ошибки обхода.</param>
/// <param name="FilesScanned">Файлов учтено.</param>
/// <param name="BytesScanned">Объём учтённых файлов в байтах.</param>
/// <param name="TopLevelTotal">Число подкаталогов верхнего уровня (0 – ещё не известно).</param>
/// <param name="TopLevelCompleted">Сколько подкаталогов верхнего уровня уже завершено.</param>
/// <param name="CurrentPath">Последний каталог, в который вошёл обход.</param>
public readonly record struct ScanProgressSnapshot(
    long DirectoriesScanned,
    long DirectoriesFailed,
    long FilesScanned,
    long BytesScanned,
    int TopLevelTotal,
    int TopLevelCompleted,
    string CurrentPath)
{
    /// <summary>
    /// Детерминированная доля выполнения в диапазоне [0..1] по завершённым подкаталогам верхнего
    /// уровня; <c>null</c>, пока число веток неизвестно (тогда прогресс показывается неопределённым).
    /// </summary>
    public double? Fraction => TopLevelTotal > 0 ? Math.Clamp((double)TopLevelCompleted / TopLevelTotal, 0d, 1d) : null;
}
