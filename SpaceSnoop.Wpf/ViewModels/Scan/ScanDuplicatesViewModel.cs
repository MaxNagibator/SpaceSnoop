using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanDuplicatesViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly DuplicateProgressDialogFactory _dialogFactory;
    private readonly ISettingsStore _settings;
    private readonly ScanPreferences _preferences;
    private readonly ToastNotifier _notifier;
    private readonly Func<DirectorySpace?> _root;
    private readonly Func<IReadOnlyList<SpaceBase>, bool, int> _mark;
    private readonly Func<bool> _isScanning;

    private bool _suppressPersist;
    private DuplicateReport? _report;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInitialEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowNothingFound))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NothingFoundHeading))]
    [NotifyPropertyChangedFor(nameof(NothingFoundDescription))]
    [NotifyPropertyChangedFor(nameof(NothingFoundActionText))]
    [NotifyPropertyChangedFor(nameof(NothingFoundCommand))]
    private bool _cleanedUp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNothingFound))]
    private bool _hasGroups;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReclaim))]
    private string _reclaimText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private int _minSizeMb = AppDefaults.ScanDuplicatesMinSizeMbDefault;

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public bool HasNotice => NoticeText.Length > 0;

    public bool HasReclaim => ReclaimText.Length > 0;

    public bool ShowInitialEmpty => !HasResult;

    public bool ShowNothingFound => HasResult && !HasGroups;

    public string NothingFoundHeading => CleanedUp
        ? HiddenGroups > 0 ? "Показанные дубликаты разобраны" : "Дубликатов не осталось"
        : "Дубликатов не найдено";

    public string NothingFoundDescription => CleanedUp
        ? HiddenGroups > 0
            ? $"Не попали в список групп: {HiddenGroups:N0}. Запустите поиск заново – они появятся."
            : "Все найденные копии удалены или упакованы. Порог можно опустить – тогда в поиск попадут и мелкие файлы."
        : "Ни один файл выше порога размера не совпал с другим побайтно. Порог можно опустить – тогда в поиск попадут и мелкие файлы, но групп станет заметно больше.";

    public string NothingFoundActionText => RepeatShowsHidden ? "Искать заново" : "Искать от 0 МБ";

    public IAsyncRelayCommand NothingFoundCommand => RepeatShowsHidden ? FindCommand : LowerThresholdCommand;

    private bool RepeatShowsHidden => CleanedUp && HiddenGroups > 0;

    private int HiddenGroups => _report?.OmittedGroups ?? 0;

    internal ScanDuplicatesViewModel(
        IDialogService dialogs,
        DuplicateProgressDialogFactory dialogFactory,
        ISettingsStore settings,
        ScanPreferences preferences,
        ToastNotifier notifier,
        Func<DirectorySpace?> root,
        Func<IReadOnlyList<SpaceBase>, bool, int> mark,
        Func<bool> isScanning)
    {
        _dialogs = dialogs;
        _dialogFactory = dialogFactory;
        _settings = settings;
        _preferences = preferences;
        _notifier = notifier;
        _root = root;
        _mark = mark;
        _isScanning = isScanning;

        LoadSettings();
    }

    // TODO: связь «состояние страницы – CanExecute» держится вызовом из ScanViewModel, а не типом: Func<bool> прячет её от компилятора, и забытая проводка обнаруживается только нажатием кнопки. Апгрейд – когда появится восьмая дочерняя модель с таким CanExecute: отдавать источник состояния и подписываться вместо Func.
    internal void NotifyScanStateChanged()
    {
        FindCommand.NotifyCanExecuteChanged();
        LowerThresholdCommand.NotifyCanExecuteChanged();
    }

    internal void Clear()
    {
        _report = null;
        Groups.Clear();
        CleanedUp = false;
        HasResult = false;
        HasGroups = false;
        SummaryText = string.Empty;
        ReclaimText = string.Empty;
        NoticeText = string.Empty;
    }

    internal static string DescribeReclaim(DuplicateReport report)
    {
        return report.Groups.Count == 0 ? string.Empty : $"вернёт {SizeFormatter.Format(report.ReclaimableBytes)}";
    }

    internal static string DescribeReport(DuplicateReport report)
    {
        if (report.Groups.Count == 0)
        {
            return $"Дубликатов не найдено · проверено файлов: {report.Examined:N0}";
        }

        return $"Групп: {report.Groups.Count:N0} · проверено файлов: {report.Examined:N0}";
    }

    internal static string DescribeLimits(DuplicateReport report)
    {
        var parts = new List<string>();

        if (report.OmittedGroups > 0)
        {
            parts.Add($"показаны крупнейшие, ещё {report.OmittedGroups:N0} групп не выведено – поднимите порог размера");
        }

        if (report.UnreadableDirectories > 0)
        {
            parts.Add($"искали не везде: каталогов без доступа при сканировании – {report.UnreadableDirectories:N0}");
        }

        if (report.Errors.Count > 0)
        {
            parts.Add($"не удалось прочитать файлов: {report.Errors.Count:N0}");
        }

        return string.Join(" · ", parts);
    }

    internal void Apply(DuplicateReport report)
    {
        var root = _root()?.AbsolutePath;

        _report = report;
        Groups.Clear();

        foreach (var group in report.Groups)
        {
            Groups.Add(new(group, root, _mark));
        }

        SummaryText = DescribeReport(report);
        ReclaimText = DescribeReclaim(report);
        NoticeText = DescribeLimits(report);
        CleanedUp = false;
        HasGroups = Groups.Count > 0;
        HasResult = true;

        _notifier.Notify(HasReclaim ? $"{SummaryText} · {ReclaimText}" : SummaryText);
    }

    internal void RemoveDeleted(IReadOnlySet<SpaceBase> deleted)
    {
        if (_report is not { } report || Groups.Count == 0)
        {
            return;
        }

        var root = _root()?.AbsolutePath;
        var reclaimable = report.ReclaimableBytes;
        var changed = false;

        for (var index = Groups.Count - 1; index >= 0; index--)
        {
            var group = Groups[index].Group;
            var next = group.Without(space => IsGone(space, deleted));

            if (ReferenceEquals(next, group))
            {
                continue;
            }

            changed = true;
            reclaimable -= group.ReclaimableBytes - (next?.ReclaimableBytes ?? 0);

            if (next is null)
            {
                Groups.RemoveAt(index);
            }
            else
            {
                Groups[index] = new(next, root, _mark);
            }
        }

        if (!changed)
        {
            return;
        }

        _report = report with
        {
            Groups = [.. Groups.Select(static x => x.Group)],
            ReclaimableBytes = Math.Max(0, reclaimable),
        };

        SummaryText = Groups.Count switch
        {
            > 0 => DescribeReport(_report.Value),
            _ when report.OmittedGroups > 0 => $"Показанные группы разобраны · ещё не выведено: {report.OmittedGroups:N0}",
            _ => $"Дубликатов не осталось · проверено файлов: {report.Examined:N0}",
        };

        ReclaimText = DescribeReclaim(_report.Value);
        CleanedUp = Groups.Count == 0;
        HasGroups = Groups.Count > 0;
    }

    internal static bool IsGone(SpaceBase space, IReadOnlySet<SpaceBase> deleted)
    {
        for (var current = space; current is not null; current = current.Parent)
        {
            if (deleted.Contains(current))
            {
                return true;
            }
        }

        return false;
    }

    partial void OnMinSizeMbChanged(int value)
    {
        var clamped = Math.Clamp(value, AppDefaults.ScanDuplicatesMinSizeMbMin, AppDefaults.ScanDuplicatesMinSizeMbMax);

        if (clamped != value)
        {
            MinSizeMb = clamped;
            return;
        }

        LowerThresholdCommand.NotifyCanExecuteChanged();

        if (!_suppressPersist)
        {
            _settings.SetInt(SettingsKeys.ScanDuplicatesMinSize, clamped);
        }
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        MinSizeMb = Math.Clamp(
            _settings.GetInt(SettingsKeys.ScanDuplicatesMinSize, AppDefaults.ScanDuplicatesMinSizeMbDefault),
            AppDefaults.ScanDuplicatesMinSizeMbMin,
            AppDefaults.ScanDuplicatesMinSizeMbMax);

        _suppressPersist = false;
    }

    private bool CanFind()
    {
        return !_isScanning() && _root() is not null;
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindAsync()
    {
        if (_root() is not { } root)
        {
            return;
        }

        var path = root.AbsolutePath;
        var parallelism = await _preferences.ResolveParallelismAsync(path);

        var options = DuplicateOptions.Default with
        {
            MinSize = Math.Max(1, (long)MinSizeMb * 1024 * 1024),
            MaxParallelism = parallelism,
        };

        var dialog = _dialogFactory.Create(new(root, options));

        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            await dialog.StopAsync();
        }

        if (dialog.Report is not { } report || !ReferenceEquals(_root(), root))
        {
            return;
        }

        Apply(report);
    }

    private bool CanLowerThreshold()
    {
        return MinSizeMb > AppDefaults.ScanDuplicatesMinSizeMbMin && CanFind();
    }

    [RelayCommand(CanExecute = nameof(CanLowerThreshold))]
    private async Task LowerThresholdAsync()
    {
        MinSizeMb = AppDefaults.ScanDuplicatesMinSizeMbMin;

        await FindAsync();
    }
}
