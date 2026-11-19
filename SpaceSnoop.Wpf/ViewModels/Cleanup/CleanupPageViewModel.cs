using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupPageViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    private readonly IUiTimer _activityTimer;
    private readonly Stopwatch _activityWatch = new();

    [ObservableProperty]
    private int _selectedSectionIndex;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    public CleanupViewModel Windows { get; }

    public DockerViewModel Docker { get; }

    public DeveloperCleanupViewModel Development { get; }

    public IReadOnlyList<SegmentOption> Sections { get; } =
    [
        new(PackIconLucideKind.Trash2, "Windows", "Временные файлы, кэши и корзина"),
        new(PackIconLucideKind.Container, "Docker", "Образы, контейнеры, тома и кэш сборки"),
        new(PackIconLucideKind.Wrench, "Разработка", "Кэши NuGet и артефакты проектов .NET"),
    ];

    public bool IsDockerActive
    {
        get => SelectedSectionIndex == 1;
        set
        {
            if (value || IsDockerActive)
            {
                SelectedSectionIndex = value ? 1 : 0;
            }
        }
    }

    public bool IsWindowsActive => SelectedSectionIndex == 0;

    public bool IsDevelopmentActive => SelectedSectionIndex == 2;

    public IReadOnlyList<IPageStatus> Operations { get; }

    public string PageTitle => "Очистка";

    public string PageDescription => ActiveHeader.PageDescription;

    public string? RefreshTooltip => ActiveRefresh.RefreshTooltip;

    public string? StatusCaption => OperationStatus.StatusCaption;

    public bool IsBusy => OperationStatus.IsBusy;

    public bool IsIndeterminate => OperationStatus.IsIndeterminate;

    public double ProgressValue => OperationStatus.ProgressValue;

    public double ProgressMax => OperationStatus.ProgressMax;

    public ICommand? CancelCommand => OperationStatus.CancelCommand;

    private IPageStatus OperationStatus => ActiveStatus.IsBusy ? ActiveStatus
        : Operations.FirstOrDefault(operation => operation.IsBusy) ?? ActiveStatus;

    private IPageHeader ActiveHeader => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    private IPageStatus ActiveStatus => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    private IPageRefresh ActiveRefresh => SelectedSectionIndex switch { 1 => Docker, 2 => Development, _ => Windows };

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public CleanupPageViewModel(CleanupViewModel windows, DockerViewModel docker, DeveloperCleanupViewModel development, IUiDispatcher dispatcher)
    {
        Windows = windows;
        Docker = docker;
        Development = development;
        Operations = [Windows, Docker, Development];
        _activityTimer = dispatcher.CreateTimer(TimeSpan.FromSeconds(1), UpdateElapsed);

        Windows.PropertyChanged += OnSectionChanged;
        Docker.PropertyChanged += OnSectionChanged;
        Development.PropertyChanged += OnSectionChanged;
    }

    public async Task EnsureLoadedAsync()
    {
        if (IsDevelopmentActive)
        {
            await Development.EnsureLoadedAsync();
            return;
        }

        if (IsDockerActive)
        {
            await Docker.EnsureLoadedAsync();
            return;
        }

        await Windows.EnsureLoadedAsync();
    }

    public void ActivateDocker()
    {
        IsDockerActive = true;
    }

    async partial void OnSelectedSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsWindowsActive));
        OnPropertyChanged(nameof(IsDockerActive));
        OnPropertyChanged(nameof(IsDevelopmentActive));
        NotifyProxied();

        await EnsureLoadedAsync();
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (IsBusy && !_activityWatch.IsRunning)
        {
            _activityWatch.Restart();
            UpdateElapsed();
            _activityTimer.Start();
        }
        else if (!IsBusy && _activityWatch.IsRunning)
        {
            _activityWatch.Stop();
            _activityTimer.Stop();
        }

        NotifyProxied();
    }

    private void UpdateElapsed()
    {
        ElapsedText = $"Время фоновой работы: {_activityWatch.Elapsed:hh\\:mm\\:ss}";
    }

    private void NotifyProxied()
    {
        OnPropertyChanged(nameof(PageDescription));
        OnPropertyChanged(nameof(RefreshTooltip));
        OnPropertyChanged(nameof(StatusCaption));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressMax));
        OnPropertyChanged(nameof(CancelCommand));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefresh()
    {
        return !ActiveStatus.IsBusy && ActiveRefresh.RefreshCommand.CanExecute(null);
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        return ((IAsyncRelayCommand)ActiveRefresh.RefreshCommand).ExecuteAsync(null);
    }
}
