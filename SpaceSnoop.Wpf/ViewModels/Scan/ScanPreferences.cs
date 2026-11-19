namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _useMultithreading = AppDefaults.ScanMultithreadingDefault;

    [ObservableProperty]
    private int _maxParallelism = Math.Min(Environment.ProcessorCount * AppDefaults.ScanParallelismPerCore, AppDefaults.ScanParallelismDefaultCeiling);

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

    [ObservableProperty]
    private bool _releaseBeforeRescan = AppDefaults.ScanReleaseBeforeRescanDefault;

    [ObservableProperty]
    private bool _confirmRescan = AppDefaults.ScanConfirmRescanDefault;

    [ObservableProperty]
    private bool _returnMemoryAfterScan = AppDefaults.ScanReturnMemoryAfterScanDefault;

    public int ProcessorCount { get; } = Environment.ProcessorCount;

    public int ParallelismCeiling => ProcessorCount * AppDefaults.ScanParallelismPerCore;

    public int DefaultParallelism => Math.Min(ParallelismCeiling, AppDefaults.ScanParallelismDefaultCeiling);

    public bool DuplicatesEnabled { get; }

    public bool MftNeedsElevation => MftEnabled && !AdminElevation.IsElevated;

    public string ParallelismHint => "Сколько папок читать одновременно: лишние потоки могут увеличить нагрузку без ускорения обхода.";

    public string ParallelismDetails =>
        $"По умолчанию – {DefaultParallelism} потоков, максимум – {ParallelismCeiling} при {ProcessorCount} логических процессорах. "
        + "На большом числе потоков скорость может выйти на плато, а нагрузка на процессор продолжит расти. "
        + "Сохранённое вручную число потоков не меняется; для медленного носителя доступен отдельный режим ограничения.";

    public ScanPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        UseMultithreading = _settings.GetBool(SettingsKeys.ScanMultithreading, AppDefaults.ScanMultithreadingDefault);

        var storedParallelism = _settings.GetInt(SettingsKeys.ScanParallelism, DefaultParallelism);
        MaxParallelism = Math.Clamp(storedParallelism, 1, ParallelismCeiling);

        MediaAware = _settings.GetBool(SettingsKeys.ScanMediaAware, AppDefaults.ScanMediaAwareDefault);
        Intensity = _settings.GetDouble(SettingsKeys.ScanIntensity, AppDefaults.IntensityDefault);
        RevealFiles = _settings.GetBool(SettingsKeys.ScanRevealFiles);
        MftEnabled = _settings.GetBool(SettingsKeys.ScanMftEnabled);
        MftRootOnly = _settings.GetBool(SettingsKeys.ScanMftRootOnly, AppDefaults.ScanMftRootOnlyDefault);
        ReleaseBeforeRescan = _settings.GetBool(SettingsKeys.ScanReleaseBeforeRescan, AppDefaults.ScanReleaseBeforeRescanDefault);
        ConfirmRescan = _settings.GetBool(SettingsKeys.ScanConfirmRescan, AppDefaults.ScanConfirmRescanDefault);
        ReturnMemoryAfterScan = _settings.GetBool(SettingsKeys.ScanReturnMemoryAfterScan);
        _suppressPersist = false;

        if (storedParallelism != MaxParallelism)
        {
            _settings.SetInt(SettingsKeys.ScanParallelism, MaxParallelism);
        }

        DuplicatesEnabled = settings.GetBool(SettingsKeys.ScanDuplicatesEnabled);
    }

    public async Task<int> ResolveParallelismAsync(string path)
    {
        if (!UseMultithreading)
        {
            return 1;
        }

        if (!MediaAware)
        {
            return MaxParallelism;
        }

        return await StorageMedia.LimitParallelismAsync(
                path,
                MaxParallelism,
                TimeSpan.FromMilliseconds(AppDefaults.StorageMediaTimeoutMs))
            .ConfigureAwait(false);
    }

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

    partial void OnReleaseBeforeRescanChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanReleaseBeforeRescan, value);
        }
    }

    partial void OnConfirmRescanChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanConfirmRescan, value);
        }
    }

    partial void OnReturnMemoryAfterScanChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.ScanReturnMemoryAfterScan, value);
        }
    }
}
