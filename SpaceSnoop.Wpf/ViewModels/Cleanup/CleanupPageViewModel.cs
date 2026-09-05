using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupPageViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    [ObservableProperty]
    private bool _isDockerActive;

    public CleanupPageViewModel(CleanupViewModel windows, DockerViewModel docker)
    {
        Windows = windows;
        Docker = docker;

        Windows.PropertyChanged += OnSectionChanged;
        Docker.PropertyChanged += OnSectionChanged;
    }

    public CleanupViewModel Windows { get; }

    public DockerViewModel Docker { get; }

    public IReadOnlyList<SegmentOption> Sections { get; } =
    [
        new(PackIconLucideKind.Trash2, "Windows", "Временные файлы, кэши и корзина"),
        new(PackIconLucideKind.Container, "Docker", "Образы, контейнеры, тома и кэш сборки"),
    ];

    public int SelectedSectionIndex
    {
        get => IsDockerActive ? 1 : 0;
        set => IsDockerActive = value == 1;
    }

    public bool IsWindowsActive => !IsDockerActive;

    public string PageTitle => "Очистка";

    public string PageDescription => IsDockerActive ? Docker.PageDescription : Windows.PageDescription;

    public string? RefreshTooltip => IsDockerActive ? Docker.RefreshTooltip : Windows.RefreshTooltip;

    public string? StatusCaption => IsDockerActive ? Docker.StatusCaption : Windows.StatusCaption;

    public bool IsBusy => IsDockerActive ? Docker.IsBusy : Windows.IsBusy;

    public bool IsIndeterminate => IsDockerActive ? Docker.IsIndeterminate : Windows.IsIndeterminate;

    public double ProgressValue => IsDockerActive ? Docker.ProgressValue : Windows.ProgressValue;

    public double ProgressMax => IsDockerActive ? Docker.ProgressMax : Windows.ProgressMax;

    public ICommand? CancelCommand => IsDockerActive ? Docker.CancelCommand : Windows.CancelCommand;

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public async Task EnsureLoadedAsync()
    {
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

    async partial void OnIsDockerActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWindowsActive));
        OnPropertyChanged(nameof(SelectedSectionIndex));
        NotifyProxied();

        await EnsureLoadedAsync();
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        var fromActive = IsDockerActive ? ReferenceEquals(sender, Docker) : ReferenceEquals(sender, Windows);

        if (fromActive)
        {
            OnPropertyChanged(args.PropertyName);
        }
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
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return IsDockerActive ? Docker.RefreshCommand.ExecuteAsync(null) : Windows.RefreshCommand.ExecuteAsync(null);
    }
}
