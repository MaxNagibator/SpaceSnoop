using KeepShell.Services.Modal;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

public sealed partial class BatchCreateProfilesDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly IReadOnlyList<SyncProfile> _existing;

    [ObservableProperty]
    private string _sourceParent = string.Empty;

    [ObservableProperty]
    private string _destParent = string.Empty;

    [ObservableProperty]
    private int _selectedModeIndex;

    [ObservableProperty]
    private bool _mirror;

    [ObservableProperty]
    private string _exclusions = string.Empty;

    public BatchCreateProfilesDialogViewModel(ISettingsStore settings)
    {
        _existing = SyncProfileStore.Load(settings);
        _selectedModeIndex = Math.Clamp(settings.GetInt(SettingsKeys.SyncMode), 0, Modes.Count - 1);
        _mirror = settings.GetBool(SettingsKeys.SyncMirror);

        var exclusions = (settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim();

        if (exclusions.Length == 0)
        {
            exclusions = (settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty).Trim();
        }

        _exclusions = exclusions;
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => "Создать пачку профилей";

    public IReadOnlyList<string> Modes { get; } = ["Слева направо", "Справа налево", "Двусторонний"];

    public ObservableCollection<BatchPairRow> Rows { get; } = [];

    public IReadOnlyList<SyncProfile> CreatedProfiles { get; private set; } = [];

    public int SelectedCount => Rows.Count(row => row.Include);

    public bool HasRows => Rows.Count > 0;

    public bool MirrorApplicable => SelectedModeIndex != 2;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BatchPairRow.Include))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        CreateCommand.NotifyCanExecuteChanged();
    }

    private static void Browse(Action<string> assign)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите каталог" };

        if (dialog.ShowDialog() == true)
        {
            assign(dialog.FolderName);
        }
    }

    [RelayCommand]
    private void BrowseSource()
    {
        Browse(path => SourceParent = path);
    }

    [RelayCommand]
    private void BrowseDest()
    {
        Browse(path => DestParent = path);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        CreatedProfiles = Rows
            .Where(row => row.Include)
            .Select(row => new SyncProfile
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = row.Name,
                Left = row.Left,
                Right = row.Right,
                Mode = SelectedModeIndex,
                Mirror = Mirror,
                Exclusions = Exclusions.Trim(),
            })
            .ToList();

        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    partial void OnSourceParentChanged(string value)
    {
        Rebuild();
    }

    partial void OnDestParentChanged(string value)
    {
        Rebuild();
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(MirrorApplicable));
    }

    private void Rebuild()
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();

        foreach (var row in ProfileBatch.BuildPairs(SourceParent, DestParent, _existing))
        {
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(SelectedCount));
        CreateCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreate()
    {
        return SelectedCount > 0;
    }
}
