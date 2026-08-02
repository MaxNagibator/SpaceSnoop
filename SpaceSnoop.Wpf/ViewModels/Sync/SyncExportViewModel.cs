using KeepShell.Services;
using SpaceSnoop.Core.Export;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncExportViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly ToastNotifier _notifier;
    private readonly IFilePicker _filePicker;
    private readonly SyncSetupViewModel _setup;
    private readonly SyncSessionViewModel _session;
    private readonly SyncGitViewModel _git;
    private readonly Func<ComparisonResult?> _result;
    private readonly Func<SyncReport?> _lastReport;

    internal SyncExportViewModel(
        IDialogService dialogs,
        ILogger logger,
        ToastNotifier notifier,
        IFilePicker filePicker,
        SyncSetupViewModel setup,
        SyncSessionViewModel session,
        SyncGitViewModel git,
        Func<ComparisonResult?> result,
        Func<SyncReport?> lastReport)
    {
        _dialogs = dialogs;
        _logger = logger;
        _notifier = notifier;
        _filePicker = filePicker;
        _setup = setup;
        _session = session;
        _git = git;
        _result = result;
        _lastReport = lastReport;
    }

    internal ComparisonExportModel? BuildExportModel(int entryLimit)
    {
        return CaptureExportBuilder(entryLimit)?.Invoke();
    }

    internal Func<SyncPlanExportModel>? CapturePlanBuilder(int entryLimit)
    {
        if (_result() is not { } result)
        {
            return null;
        }

        var options = new ComparisonExportOptions(_setup.CurrentMode, _setup.CurrentWinner, _setup.Mirror, _setup.Exclusions.Trim());

        return () => SyncPlanExport.Build(result, options, AppInfo.Version, entryLimit);
    }

    internal Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit)
    {
        if (_result() is not { } result)
        {
            return null;
        }

        var options = new ComparisonExportOptions(_setup.CurrentMode, _setup.CurrentWinner, _setup.Mirror, _setup.Exclusions.Trim());

        var git = _git.LeftState is null && _git.RightState is null
            ? null
            : new ComparisonExportGit(_git.LeftState, _git.RightState, _git.GitVerdictText);

        var lastReport = _lastReport();

        var lastSync = lastReport is null
            ? null
            : new ComparisonExportSync(lastReport.CopiedCount, lastReport.DeletedCount, lastReport.Errors, lastReport.Mismatches);

        return () => ComparisonExport.Build(result, options, AppInfo.Version, entryLimit) with
        {
            Git = git,
            LastSync = lastSync,
        };
    }

    private bool CanExport()
    {
        return !_session.IsBusy && _result() is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportComparison()
    {
        if (_result() is null)
        {
            return;
        }

        var path = _filePicker.SaveFile(new FileSaveRequest("Экспорт сравнения", "JSON (*.json)|*.json")
        {
            DefaultExtension = ".json",
            FileName = BuildExportFileName(),
        });

        if (path is null)
        {
            return;
        }

        try
        {
            var model = BuildExportModel(ComparisonExport.DefaultEntryLimit)!;

            File.WriteAllText(path, ComparisonExport.ToJson(model));
            _logger.ComparisonExported(path, model.Entries.Count, model.OmittedEntries);

            var omitted = model.OmittedEntries > 0 ? $", пропущено {model.OmittedEntries:N0}" : string.Empty;
            _session.StatusCaption = $"Сравнение выгружено: {Path.GetFileName(path)}";
            _notifier.Notify($"Сравнение выгружено: записей {model.Entries.Count:N0}{omitted}", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.ComparisonExportFailed(ex, path);
            _dialogs.Error("Экспорт сравнения", ex.Message);
        }
    }

    private string BuildExportFileName()
    {
        var trimmed = _setup.LeftPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var raw = Path.GetFileName(trimmed);
        var name = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        return $"sync-compare-{(name.Length == 0 ? "root" : name)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    }
}
