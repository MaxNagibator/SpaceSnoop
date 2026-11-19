using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DeveloperCleanupViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus
{
    private readonly NuGetCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<DeveloperCleanupViewModel> _logger;
    private readonly CleanupOperation _operation = new();
    private NuGetCleanupPlan? _plan;
    private bool _loaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectRecommendedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    private bool _isNuGetBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNuGetActive))]
    [NotifyPropertyChangedFor(nameof(IsProjectsActive))]
    [NotifyPropertyChangedFor(nameof(IsWorkloadsActive))]
    [NotifyPropertyChangedFor(nameof(IsDotNetCleanActive))]
    [NotifyPropertyChangedFor(nameof(RefreshTooltip))]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private int _selectedAreaIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public ProjectCleanupViewModel Projects { get; }
    public WorkloadCleanupViewModel Workloads { get; }
    public DotNetCleanViewModel DotNetClean { get; }

    public IReadOnlyList<SegmentOption> Areas { get; } =
    [
        new(PackIconLucideKind.Package, "Кэши NuGet", "Кэши текущего пользователя"),
        new(PackIconLucideKind.Folder, "Проекты", "bin/obj, результаты тестов и бенчмарков"),
        new(PackIconLucideKind.HardDrive, "Workloads", "Очистка хранилища workload"),
        new(PackIconLucideKind.Terminal, "dotnet clean", "Очистка доверенного проекта"),
    ];

    public bool IsNuGetActive => SelectedAreaIndex == 0;
    public bool IsProjectsActive => SelectedAreaIndex == 1;
    public bool IsWorkloadsActive => SelectedAreaIndex == 2;
    public bool IsDotNetCleanActive => SelectedAreaIndex == 3;
    public bool IsBusy => IsNuGetBusy || Projects.IsBusy || Workloads.IsBusy || DotNetClean.IsBusy;
    public ObservableCollection<NuGetCacheViewModel> Caches { get; } = [];
    public string PageTitle => "Разработка";
    public string PageDescription => "Кэши NuGet, артефакты проектов, workloads и доверенная очистка проекта .NET.";

    public string? RefreshTooltip => IsProjectsActive ? "Повторить поиск в выбранном каталоге проектов"
        : IsWorkloadsActive ? "Определить активный SDK и workloads заново"
        : IsDotNetCleanActive ? "Проверить SDK в каталоге доверенного проекта" : "Определить пути NuGet и измерить кэши заново";

    public string? StatusCaption => IsNuGetBusy ? $"NuGet: {StatusText}"
        : Projects.IsBusy ? $"Проекты: {Projects.StatusText}"
        : Workloads.IsBusy ? $"Workloads: {Workloads.StatusText}"
        : DotNetClean.IsBusy ? $"dotnet clean: {DotNetClean.StatusText}"
        : IsProjectsActive ? Projects.StatusText
        : IsWorkloadsActive ? Workloads.StatusText
        : IsDotNetCleanActive ? DotNetClean.StatusText : StatusText;

    public bool IsIdle => !IsBusy;
    public bool IsIndeterminate => true;
    public double ProgressValue => 0;
    public double ProgressMax => 1;

    public ICommand? CancelCommand => Projects.IsBusy ? Projects.CancelCommand
        : Workloads.IsBusy ? Workloads.CancelCommand
        : DotNetClean.IsBusy ? DotNetClean.CancelCommand
        : _operation.CanCancel ? CancelOperationCommand : null;

    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool HasCaches => Caches.Count > 0;
    public string TotalText => SizeFormatter.Format(Caches.Sum(x => x.Bytes));
    public string SelectedText => SizeFormatter.Format(Caches.Where(x => x.IsSelected && x.CanSelect).Sum(x => x.Bytes));
    public string SelectionText => $"Категорий: {Caches.Count(x => x.IsSelected && x.CanSelect)} из {Caches.Count}";
    public bool HasSelectedBytes => Caches.Any(x => x.IsSelected && x.CanSelect && x.Bytes > 0);
    public bool CanClean => !IsBusy && _plan is not null && Caches.Any(x => x.IsSelected && x.CanSelect);
    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public DeveloperCleanupViewModel(
        NuGetCleanupService service,
        IDialogService dialogs,
        IUiDispatcher uiDispatcher,
        ILogger<DeveloperCleanupViewModel> logger,
        ProjectCleanupViewModel projects,
        WorkloadCleanupViewModel workloads,
        DotNetCleanViewModel dotNetClean)
    {
        _service = service;
        _dialogs = dialogs;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        Projects = projects;
        Workloads = workloads;
        DotNetClean = dotNetClean;
        Projects.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(StatusCaption));
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CancelCommand));
            RefreshCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            SelectRecommendedCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
            workloads.IsHostBusy = IsNuGetBusy || Projects.IsBusy || DotNetClean.IsBusy;
            DotNetClean.IsHostBusy = IsNuGetBusy || Projects.IsBusy || Workloads.IsBusy;
        };

        Workloads.PropertyChanged += (_, _) =>
        {
            Projects.IsHostBusy = IsNuGetBusy || Workloads.IsBusy || DotNetClean.IsBusy;
            DotNetClean.IsHostBusy = IsNuGetBusy || Projects.IsBusy || Workloads.IsBusy;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CancelCommand));
            OnPropertyChanged(nameof(StatusCaption));
            RefreshCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            SelectRecommendedCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
        };

        DotNetClean.PropertyChanged += (_, _) =>
        {
            Projects.IsHostBusy = IsNuGetBusy || Workloads.IsBusy || DotNetClean.IsBusy;
            Workloads.IsHostBusy = IsNuGetBusy || Projects.IsBusy || DotNetClean.IsBusy;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(CanClean));
            OnPropertyChanged(nameof(CancelCommand));
            OnPropertyChanged(nameof(StatusCaption));
            RefreshCommand.NotifyCanExecuteChanged();
            CleanCommand.NotifyCanExecuteChanged();
            SelectRecommendedCommand.NotifyCanExecuteChanged();
            ClearSelectionCommand.NotifyCanExecuteChanged();
        };
    }

    public Task EnsureLoadedAsync()
    {
        if (_loaded || IsBusy || !IsNuGetActive)
        {
            return Task.CompletedTask;
        }

        return ScanNuGetAsync();
    }

    partial void OnSelectedAreaIndexChanged(int value)
    {
        if (value == 0)
        {
            _ = EnsureLoadedAsync();
        }
    }

    partial void OnIsNuGetBusyChanged(bool value)
    {
        Projects.IsHostBusy = value || Workloads.IsBusy || DotNetClean.IsBusy;
        Workloads.IsHostBusy = value || Projects.IsBusy || DotNetClean.IsBusy;
        DotNetClean.IsHostBusy = value || Projects.IsBusy || Workloads.IsBusy;
    }

    private bool CanRefresh()
    {
        return IsIdle
            && (!IsProjectsActive || Projects.FindCommand.CanExecute(null))
            && (!IsDotNetCleanActive || DotNetClean.InspectCommand.CanExecute(null));
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsProjectsActive)
        {
            await Projects.FindCommand.ExecuteAsync(null);
            return;
        }

        if (IsWorkloadsActive)
        {
            await Workloads.InspectCommand.ExecuteAsync(null);
            return;
        }

        if (IsDotNetCleanActive)
        {
            await DotNetClean.InspectCommand.ExecuteAsync(null);
            return;
        }

        await ScanNuGetAsync();
    }

    private async Task ScanNuGetAsync()
    {
        _operation.Begin(value => IsNuGetBusy = value);
        ResultText = null;
        try
        {
            await ScanAsync(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Замер отменён. Для очистки повторите сканирование.";
        }
        catch (Exception exception)
        {
            _logger.DeveloperCleanupFailed(exception.Unwrap());
            StatusText = exception.Unwrap().Message;
        }
        finally
        {
            _operation.End();
        }
    }

    private async Task ScanAsync(CancellationToken token)
    {
        _plan = null;
        ClearRows();
        StatusText = "Определяю пути и измеряю NuGet…";
        var plan = await _service.ScanAsync(token);
        _uiDispatcher.Invoke(() =>
        {
            _loaded = true;
            _plan = plan;
            foreach (var scan in plan.Caches)
            {
                var row = new NuGetCacheViewModel(scan);
                row.PropertyChanged += OnSelectionChanged;
                Caches.Add(row);
            }

            var excludedPackages = plan.Caches.Sum(x => x.ExcludedPackages);
            StatusText = plan.Caches.Any(x => x.Errors.Count > 0)
                ? "Замер завершён. Категории с ошибками недоступны для очистки."
                : "Замер завершён. Выберите кэши для очистки.";
            if (excludedPackages > 0)
            {
                StatusText += $" Исключено пакетов: {excludedPackages}. Их файлы не будут удалены.";
            }

            NotifySelection();
        });
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        if (!CanClean || _plan is null)
        {
            return;
        }

        var plan = _plan;
        var selected = Caches.Where(x => x.IsSelected && x.CanSelect).ToArray();
        var selectedKinds = selected.Select(x => x.Kind).ToArray();
        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine("Перед очисткой остановите restore, сборки и операции NuGet в IDE."),
            new ConfirmTextLine("Будут удалены только файлы из показанного замера. Новые файлы останутся."),
        };

        foreach (var row in selected)
        {
            lines.Add(new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText));
            lines.Add(new ConfirmTextLine(row.Path));
            if (row.Kind == NuGetCacheKind.GlobalPackages)
            {
                lines.Add(new ConfirmTextLine(row.Description, ConfirmTextTone.Strong));
            }
        }

        var confirmation = new ConfirmDialogViewModel(
            "Очистка NuGet",
            PackIconLucideKind.Trash2,
            lines,
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("Удалить безвозвратно", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Файлы удаляются безвозвратно, мимо корзины. Восстановить их можно только повторной загрузкой из доступного источника.",
        };

        _operation.Begin(value => IsNuGetBusy = value);
        try
        {
            var confirmationTask = _operation.ConfirmAsync(_dialogs, confirmation);
            OnPropertyChanged(nameof(CancelCommand));
            var confirmed = await confirmationTask;
            OnPropertyChanged(nameof(CancelCommand));
            if (!confirmed)
            {
                return;
            }

            if (!ReferenceEquals(_plan, plan)
                || !selectedKinds.SequenceEqual(Caches.Where(x => x.IsSelected && x.CanSelect).Select(x => x.Kind)))
            {
                StatusText = "Выбор или план изменились. Повторите замер.";
                return;
            }

            _plan = null;
            StatusText = "Удаляю выбранные файлы NuGet…";
            var result = await _service.CleanAsync(plan, selected.Select(x => x.Kind).ToArray(), null, _operation.Token);
            ResultText =
                $"{CleanupOutcomeText.For(result)}. Удалено файлов: {result.Deleted:N0}, данных: {SizeFormatter.Format(result.FreedBytes)}. Пропущено: {result.Skipped:N0}."
                + (result.Errors.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, result.Errors));

            _logger.DeveloperCleanupFinished(result.Deleted, result.FreedBytes, result.Skipped, result.Cancelled);
            _operation.RestartCancellation();
            try
            {
                await ScanAsync(_operation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Повторный замер отменён. Результат удаления сохранён.";
            }
            catch (Exception exception)
            {
                _logger.DeveloperCleanupFailed(exception.Unwrap());
                StatusText = $"Повторный замер не выполнен: {exception.Unwrap().Message}";
            }
        }
        catch (Exception exception)
        {
            _plan = null;
            _logger.DeveloperCleanupFailed(exception.Unwrap());
            StatusText = exception.Unwrap().Message;
        }
        finally
        {
            _operation.End();
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _operation.Cancel();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void SelectRecommended()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var row in Caches)
        {
            row.IsSelected = row.CanSelect && row.IsRecommended;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearSelection()
    {
        if (IsBusy)
        {
            return;
        }

        foreach (var row in Caches)
        {
            row.IsSelected = false;
        }
    }

    private void ClearRows()
    {
        _uiDispatcher.Invoke(() =>
        {
            foreach (var row in Caches)
            {
                row.PropertyChanged -= OnSelectionChanged;
            }

            Caches.Clear();
            NotifySelection();
        });
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(NuGetCacheViewModel.IsSelected))
        {
            NotifySelection();
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasCaches));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelectedBytes));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }
}
