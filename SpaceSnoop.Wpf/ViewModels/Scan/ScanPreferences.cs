namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _useMultithreading = AppDefaults.ScanMultithreadingDefault;

    [ObservableProperty]
    private int _maxParallelism = Environment.ProcessorCount * AppDefaults.ScanParallelismPerCore;

    [ObservableProperty]
    private bool _mediaAware = AppDefaults.ScanMediaAwareDefault;

    [ObservableProperty]
    private double _intensity = AppDefaults.IntensityDefault;

    [ObservableProperty]
    private bool _revealFiles = AppDefaults.ScanRevealFilesDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MftNeedsElevation))]
    private bool _mftEnabled = AppDefaults.ScanMftEnabledDefault;

    [ObservableProperty]
    private bool _mftRootOnly = AppDefaults.ScanMftRootOnlyDefault;

    public ScanPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        UseMultithreading = _settings.GetBool(SettingsKeys.ScanMultithreading, AppDefaults.ScanMultithreadingDefault);
        MaxParallelism = Math.Clamp(_settings.GetInt(SettingsKeys.ScanParallelism, ParallelismCeiling), 1, ParallelismCeiling);
        MediaAware = _settings.GetBool(SettingsKeys.ScanMediaAware, AppDefaults.ScanMediaAwareDefault);
        Intensity = _settings.GetDouble(SettingsKeys.ScanIntensity, AppDefaults.IntensityDefault);
        RevealFiles = _settings.GetBool(SettingsKeys.ScanRevealFiles, AppDefaults.ScanRevealFilesDefault);
        MftEnabled = _settings.GetBool(SettingsKeys.ScanMftEnabled, AppDefaults.ScanMftEnabledDefault);
        MftRootOnly = _settings.GetBool(SettingsKeys.ScanMftRootOnly, AppDefaults.ScanMftRootOnlyDefault);
        _suppressPersist = false;

        DuplicatesEnabled = settings.GetBool(SettingsKeys.ScanDuplicatesEnabled, AppDefaults.ScanDuplicatesEnabledDefault);
    }

    public int ProcessorCount { get; } = Environment.ProcessorCount;

    public int ParallelismCeiling => ProcessorCount * AppDefaults.ScanParallelismPerCore;

    public bool DuplicatesEnabled { get; }

    public bool MftNeedsElevation => MftEnabled && !AdminElevation.IsElevated;

    public int ResolveParallelism(string path)
    {
        if (!UseMultithreading)
        {
            return 1;
        }

        if (!MediaAware)
        {
            return MaxParallelism;
        }

        var requested = MaxParallelism;
        var probe = Task.Run(() => StorageMedia.LimitParallelism(path, requested));

        return probe.Wait(AppDefaults.StorageMediaTimeoutMs) ? probe.Result : requested;
    }

    public string ParallelismHint =>
        $"Сколько каталогов обходить одновременно. "
        + $"Максимум и значение по умолчанию – вдвое больше числа логических процессоров ({ParallelismCeiling} при {ProcessorCount} ядрах): "
        + $"обход ждёт ответа файловой системы, а не считает, поэтому потоков нужно больше, чем ядер. "
        + $"Меньше потоков – ниже нагрузка и расход памяти.";

    partial void OnUseMultithreadingChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanMultithreading, value);
        }
    }

    partial void OnMaxParallelismChanged(int value)
    {
        if (!_suppressPersist)
        {
            _settings.SetInt(SettingsKeys.ScanParallelism, value);
        }
    }

    partial void OnMediaAwareChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanMediaAware, value);
        }
    }

    partial void OnIntensityChanged(double value)
    {
        if (!_suppressPersist)
        {
            _settings.SetDouble(SettingsKeys.ScanIntensity, value);
        }
    }

    partial void OnRevealFilesChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanRevealFiles, value);
        }
    }

    partial void OnMftEnabledChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanMftEnabled, value);
        }
    }

    partial void OnMftRootOnlyChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanMftRootOnly, value);
        }
    }
}
