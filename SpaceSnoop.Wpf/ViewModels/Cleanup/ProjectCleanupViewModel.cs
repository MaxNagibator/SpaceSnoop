using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectCleanupViewModel : ObservableObject
{
    private readonly ProjectCleanupService _service;
    private readonly IDialogService _dialogs;
    private readonly IFilePicker _picker;
    private readonly IShellLauncher _shell;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsStore _settings;
    private readonly ILogger<ProjectCleanupViewModel> _logger;
    private readonly CleanupOperation _operation = new();
    private ProjectCleanupPlan? _plan;
    private long _inputVersion;
    private bool _restoring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearRootsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCustomOutputsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBuildArtifactsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRootCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCustomOutputCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearRootsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCustomOutputsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectBuildArtifactsCommand))]
    private bool _isHostBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _rootPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenSelectionText))]
    [NotifyPropertyChangedFor(nameof(HasHiddenSelection))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _largestFirst = true;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    public ObservableCollection<string> AdditionalRoots { get; } = [];
    public ObservableCollection<string> CustomOutputs { get; } = [];
    public ObservableCollection<ProjectArtifactViewModel> Artifacts { get; } = [];
    public ICollectionView RowsView { get; }
    public bool IsIdle => !IsBusy && !IsHostBusy;
    public bool HasArtifacts => Artifacts.Count > 0;
    public bool HasResult => !string.IsNullOrEmpty(ResultText);
    public bool CanClean => IsIdle && _plan is not null && Artifacts.Any(x => x.IsSelected && x.CanSelect);
    public string TotalText => SizeFormatter.Format(Artifacts.Sum(x => x.Bytes));
    public string SelectedText => SizeFormatter.Format(Artifacts.Where(x => x.IsSelected && x.CanSelect).Sum(x => x.Bytes));
    public string SelectionText => $"Выбрано строк: {Artifacts.Count(x => x.IsSelected && x.CanSelect)} из {Artifacts.Count}";
    public bool HasSelectedBytes => Artifacts.Any(x => x.IsSelected && x.CanSelect && x.Bytes > 0);

    public string HiddenSelectionText => Artifacts.Any(x => x.IsSelected && x.CanSelect && !FilterArtifact(x))
        ? "Есть выбранные скрытые строки – они тоже попадут в подтверждение."
        : string.Empty;

    public bool HasHiddenSelection => HiddenSelectionText.Length > 0;

    public ProjectCleanupViewModel(
        ProjectCleanupService service,
        IDialogService dialogs,
        IFilePicker picker,
        IShellLauncher shell,
        IUiDispatcher dispatcher,
        ISettingsStore settings,
        ILogger<ProjectCleanupViewModel> logger)
    {
        _service = service;
        _dialogs = dialogs;
        _picker = picker;
        _shell = shell;
        _dispatcher = dispatcher;
        _settings = settings;
        _logger = logger;
        RowsView = CollectionViewSource.GetDefaultView(Artifacts);
        RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectArtifactViewModel.ProjectDirectory)));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectBytes), ListSortDirection.Descending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectDirectory), ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Bytes), ListSortDirection.Descending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Path), ListSortDirection.Ascending));
        RowsView.Filter = FilterArtifact;
        AdditionalRoots.CollectionChanged += (_, _) => InputChanged();
        CustomOutputs.CollectionChanged += (_, _) => InputChanged();
        RestoreInputs();
    }

    partial void OnSearchTextChanged(string value)
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(HiddenSelectionText));
        OnPropertyChanged(nameof(HasHiddenSelection));
    }

    partial void OnLargestFirstChanged(bool value)
    {
        RowsView.SortDescriptions.Clear();
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectBytes), value ? ListSortDirection.Descending : ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.ProjectDirectory), ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Bytes), value ? ListSortDirection.Descending : ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new(nameof(ProjectArtifactViewModel.Path), ListSortDirection.Ascending));
    }

    private bool FilterArtifact(object item)
    {
        if (item is not ProjectArtifactViewModel row)
        {
            return false;
        }

        var query = SearchText.Trim();
        return query.Length == 0
            || row.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.ProjectDirectory.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Contents.Any(file => file.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanFind()
    {
        return IsIdle && (ScopeRoots().Length + CustomOutputs.Count) > 0;
    }

    [RelayCommand(CanExecute = nameof(CanFind))]
    private async Task FindAsync()
    {
        if (!CanFind())
        {
            return;
        }

        _operation.Begin(value => IsBusy = value);
        ResultText = null;
        try
        {
            await ScanAsync(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Поиск отменён. Для очистки повторите поиск.";
        }
        catch (Exception exception)
        {
            _logger.ProjectCleanupFailed(exception.Unwrap());
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
        var roots = ScopeRoots();
        var outputs = CustomOutputs.ToArray();
        var version = _inputVersion;
        StatusText = "Ищу проекты и измеряю артефакты…";
        var plan = await _service.ScanAsync(roots, outputs, token);
        _dispatcher.Invoke(() =>
        {
            if (version != _inputVersion || !SameInputs(roots, outputs))
            {
                StatusText = "Пути изменились во время поиска. Повторите поиск.";
                return;
            }

            _plan = plan;
            var totals = plan.Artifacts.GroupBy(x => x.ProjectDirectory, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(row => row.Bytes), StringComparer.OrdinalIgnoreCase);

            foreach (var scan in plan.Artifacts)
            {
                var row = new ProjectArtifactViewModel(scan, totals.GetValueOrDefault(scan.ProjectDirectory));
                row.PropertyChanged += OnSelectionChanged;
                Artifacts.Add(row);
            }

            RowsView.Refresh();
            StatusText = plan.Artifacts.Count == 0
                ? "В выбранных путях проектные артефакты не найдены."
                : "Поиск завершён. Проверьте содержимое и выберите каталоги. Отчёты и нестандартные результаты выбираются отдельно.";

            if (plan.Errors.Count > 0)
            {
                StatusText += Environment.NewLine + "Часть каталогов не прочитана:" + Environment.NewLine + string.Join(Environment.NewLine, plan.Errors);
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
        var version = _inputVersion;
        var roots = ScopeRoots();
        var outputs = CustomOutputs.ToArray();
        var selected = Artifacts.Where(x => x.IsSelected && x.CanSelect).ToArray();
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine("Остановите сборки, тесты и бенчмарки.", ConfirmTextTone.Strong),
            new ConfirmTextLine("В найденных каталогах могут лежать ваши файлы. Проверьте выбранные пути и содержимое."),
            new ConfirmTextLine("В корзину попадут только просмотренные неизменившиеся файлы. Новые файлы и пустые каталоги останутся."),
            new ConfirmTextLine(
                "Файлы попадут в корзину в папках SpaceSnoop. Для восстановления верните такую папку из корзины. Исходные пути записаны в файле SpaceSnoop-restore.json внутри неё; автоматическое восстановление не гарантируется."),
            new ConfirmTextLine("Результаты тестов и бенчмарков могут понадобиться для проверки работы."),
            new ConfirmTextLine("Повторная сборка может потребовать загрузки пакетов. Убедитесь, что их источники доступны."),
        };

        foreach (var row in selected)
        {
            lines.Add(new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText));
            lines.Add(new ConfirmTextLine(row.Path));
        }

        var confirmation = new ConfirmDialogViewModel(
            "Очистка проектов",
            PackIconLucideKind.Trash2,
            lines,
            [new("Отмена", ConfirmChoiceKind.Dismissive), new("В корзину", ConfirmChoiceKind.Destructive)])
        {
            Warning = "Корзина продолжает занимать место на диске. Перед очисткой проверьте выбранные файлы.",
        };

        _operation.Begin(value => IsBusy = value);
        try
        {
            if (!await _operation.ConfirmAsync(_dialogs, confirmation))
            {
                return;
            }

            var currentIds = Artifacts.Where(x => x.IsSelected && x.CanSelect).Select(x => x.Id).ToArray();
            if (!ReferenceEquals(_plan, plan)
                || version != _inputVersion
                || !SameInputs(roots, outputs)
                || !selectedIds.SequenceEqual(currentIds, StringComparer.Ordinal))
            {
                StatusText = "Пути, выбор или план изменились. Повторите поиск.";
                return;
            }

            _plan = null;
            StatusText = "Отправляю выбранные файлы в корзину…";
            var report = await _service.CleanAsync(plan, selectedIds, null, _operation.Token);
            var outcome = CleanupOutcomeText.For(report);

            ResultText = $"{outcome}. В корзине файлов: {report.Deleted:N0}, данных: {SizeFormatter.Format(report.FreedBytes)}. Пропущено: {report.Skipped:N0}."
                + (report.Errors.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, report.Errors));

            _logger.ProjectCleanupFinished(report.Deleted, report.FreedBytes, report.Skipped, report.Cancelled);
            _operation.RestartCancellation();
            try
            {
                await ScanAsync(_operation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Повторный поиск отменён. Результат очистки сохранён.";
            }
            catch (Exception exception)
            {
                _logger.ProjectCleanupFailed(exception.Unwrap());
                StatusText = $"Повторный поиск не выполнен: {exception.Unwrap().Message}";
            }
        }
        catch (Exception exception)
        {
            _plan = null;
            _logger.ProjectCleanupFailed(exception.Unwrap());
            StatusText = exception.Unwrap().Message;
        }
        finally
        {
            _operation.End();
            OnPropertyChanged(nameof(CanClean));
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _operation.Cancel();
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void SelectBuildArtifacts()
    {
        if (!IsIdle)
        {
            return;
        }

        foreach (var row in Artifacts.Where(row => row.CanSelect
            && FilterArtifact(row)
            && row.Kind is ProjectArtifactKind.Bin or ProjectArtifactKind.Obj))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void ClearSelection()
    {
        if (!IsIdle)
        {
            return;
        }

        foreach (var row in Artifacts)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void OpenFolder(ProjectArtifactViewModel? row)
    {
        if (row is not null)
        {
            _shell.Open(row.Path);
        }
    }

    private void ClearRows()
    {
        _dispatcher.Invoke(() =>
        {
            foreach (var row in Artifacts)
            {
                row.PropertyChanged -= OnSelectionChanged;
            }

            Artifacts.Clear();
            RowsView.Refresh();
            NotifySelection();
        });
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ProjectArtifactViewModel.IsSelected))
        {
            NotifySelection();
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasArtifacts));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelectedBytes));
        OnPropertyChanged(nameof(HiddenSelectionText));
        OnPropertyChanged(nameof(HasHiddenSelection));
        OnPropertyChanged(nameof(CanClean));
        CleanCommand.NotifyCanExecuteChanged();
    }
}
