using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanSortViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly Action _resort;

    private bool _suppressPersist;

    [ObservableProperty]
    private ScanSortOption? _selectedOption;

    [ObservableProperty]
    private bool _invert = AppDefaults.ScanSortInvertDefault;

    internal ScanSortViewModel(ISettingsStore settings, Action resort)
    {
        _settings = settings;
        _resort = resort;

        Load();
    }

    public ObservableCollection<ScanSortOption> Options { get; } =
    [
        new("По имени", ScanSortField.Name),
        new("По размеру", ScanSortField.Size),
        new("По дате создания", ScanSortField.CreationDate),
        new("По времени последнего доступа", ScanSortField.LastAccessTime),
        new("По количеству файлов", ScanSortField.FileCount),
    ];

    internal ScanSortState State { get; } = new();

    partial void OnSelectedOptionChanged(ScanSortOption? value)
    {
        if (value is null)
        {
            return;
        }

        State.Field = value.Field;
        Persist(() => _settings.SetEnum(SettingsKeys.ScanSortMode, value.Field));
        _resort();
    }

    partial void OnInvertChanged(bool value)
    {
        State.Invert = value;
        Persist(() => _settings.SetBool(SettingsKeys.ScanSortInvert, value));
        _resort();
    }

    private void Load()
    {
        _suppressPersist = true;

        var field = _settings.GetEnum(SettingsKeys.ScanSortMode, AppDefaults.ScanSortModeDefault);
        var invert = _settings.GetBool(SettingsKeys.ScanSortInvert, AppDefaults.ScanSortInvertDefault);

        State.Field = field;
        State.Invert = invert;

        Invert = invert;
        SelectedOption = Options.FirstOrDefault(option => option.Field == field)
                         ?? Options.First(option => option.Field == AppDefaults.ScanSortModeDefault);

        _suppressPersist = false;
    }

    private void Persist(Action write)
    {
        if (!_suppressPersist)
        {
            write();
        }
    }
}
