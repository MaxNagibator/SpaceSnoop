using KeepShell.Services;
using SpaceSnoop.Core.Duplicates;
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

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private int _minSizeMb = AppDefaults.ScanDuplicatesMinSizeMbDefault;

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

        _suppressPersist = true;
        MinSizeMb = Math.Clamp(
            settings.GetInt(SettingsKeys.ScanDuplicatesMinSize, AppDefaults.ScanDuplicatesMinSizeMbDefault),
            AppDefaults.ScanDuplicatesMinSizeMbMin,
            AppDefaults.ScanDuplicatesMinSizeMbMax);
        _suppressPersist = false;
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public bool HasNotice => NoticeText.Length > 0;

    internal void Clear()
    {
        Groups.Clear();
        HasResult = false;
        SummaryText = string.Empty;
        NoticeText = string.Empty;
    }

    internal static string DescribeReport(DuplicateReport report)
    {
        if (report.Groups.Count == 0)
        {
            return $"Дубликатов не найдено · проверено файлов: {report.Examined:N0}";
        }

        return $"Групп: {report.Groups.Count:N0} · вернёт {SizeFormatter.Format(report.ReclaimableBytes)} · проверено файлов: {report.Examined:N0}";
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

    partial void OnMinSizeMbChanged(int value)
    {
        var clamped = Math.Clamp(value, AppDefaults.ScanDuplicatesMinSizeMbMin, AppDefaults.ScanDuplicatesMinSizeMbMax);

        if (clamped != value)
        {
            MinSizeMb = clamped;
            return;
        }

        if (!_suppressPersist)
        {
            _settings.SetInt(SettingsKeys.ScanDuplicatesMinSize, clamped);
        }
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

        var options = DuplicateOptions.Default with
        {
            MinSize = Math.Max(1, (long)MinSizeMb * 1024 * 1024),
            MaxParallelism = _preferences.UseMultithreading ? _preferences.MaxParallelism : 1,
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

        if (dialog.Report is not { } report)
        {
            return;
        }

        Apply(report);
    }

    internal void Apply(DuplicateReport report)
    {
        Groups.Clear();

        foreach (var group in report.Groups)
        {
            Groups.Add(new(group, _mark));
        }

        SummaryText = DescribeReport(report);
        NoticeText = DescribeLimits(report);
        HasResult = true;

        _notifier.Notify(SummaryText);
    }
}
