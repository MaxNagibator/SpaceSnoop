using KeepShell.Services.Modal;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Bootstrap.Gallery;

public static class GalleryStates
{
    public const string Idle = "idle";
    public const string Busy = "busy";
    public const string Done = "done";
    public const string Picker = "picker";
    public const string Welcome = "welcome";
    public const string Advice = "advice";
    public const string Development = "development";
    public const string DevelopmentProjects = "development-projects";
    public const string DevelopmentWorkloads = "development-workloads";
    public const string DevelopmentMsBuild = "development-msbuild";

    private const int ScanParallelism = 8;
    private const int ScanBranchTotal = 18;
    private const int ScanBranchDone = 11;
    private const int ScanDirectories = 12_480;
    private const int ScanFiles = 384_512;
    private const double ScanFraction = 0.85;
    private const long ScanBytesFallback = 41_500_000_000;

    private const int ScanElapsedSeconds = 47;

    private const int CompareCompleted = 4_812;
    private const int CompareElapsedSeconds = 6;

    private const int BatchIndex = 1;
    private const int ComparedLeftOnly = 12;
    private const int ComparedRightOnly = 3;
    private const int ComparedModified = 27;
    private const int ComparedIdentical = 1_284;
    private const long ComparedElapsedMs = 2_140;

    private const int DeleteDone = 2;
    private const int ArchivePacked = 184;
    private const int ArchiveEntries = 312;
    private const long ArchiveBytes = 1_840_000_000;
    private const long ArchiveCompressed = 1_120_000_000;
    private const string ArchiveCurrent = @"media\raw\DSC_0431.arw";

    private static readonly CleanupMeasurement[] CleanupMeasured =
    [
        new(2_412_000_000, 18_240, [], CleanupAvailability.Available),
        new(640_000_000, 1_312, [], CleanupAvailability.Available),
        new(4_900_000_000, 96, [], CleanupAvailability.Available),
    ];

    private static readonly string[] BusyPages =
        [SectionKey.Scan, SectionKey.Sync, SectionKey.Overview, SectionKey.Cleanup, SectionKey.Docker];

    private static readonly string[] OperationDialogs = [GalleryDialogs.Delete, GalleryDialogs.Archive];

    public static IReadOnlyList<string> All { get; } = [Idle, Busy, Done, Picker, Welcome, Advice, Development, DevelopmentProjects, DevelopmentWorkloads, DevelopmentMsBuild];

    public static string? Match(string state)
    {
        return All.FirstOrDefault(known => string.Equals(known, state, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsIdle(string state)
    {
        return string.Equals(state, Idle, StringComparison.Ordinal);
    }

    public static bool SupportsPage(string page, string state)
    {
        return state switch
        {
            Busy => BusyPages.Contains(page, StringComparer.Ordinal),
            Done => false,
            Development or DevelopmentProjects or DevelopmentWorkloads or DevelopmentMsBuild => string.Equals(page, SectionKey.Cleanup, StringComparison.Ordinal),
            Picker or Welcome or Advice => string.Equals(page, SectionKey.Scan, StringComparison.Ordinal),
            _ => true,
        };
    }

    public static bool SupportsDialog(string dialog, string state)
    {
        return state switch
        {
            Picker or Welcome or Advice or Development or DevelopmentProjects or DevelopmentWorkloads or DevelopmentMsBuild => false,
            Idle => true,
            _ => OperationDialogs.Contains(dialog, StringComparer.Ordinal),
        };
    }

    public static bool SupportsTip(string state)
    {
        return IsIdle(state);
    }

    public static async Task ApplyPageAsync(string page, string state, IServiceProvider services, GalleryFixture fixture)
    {
        if (state is Development or DevelopmentProjects or DevelopmentWorkloads or DevelopmentMsBuild)
        {
            await SettleCleanupAsync(services).ConfigureAwait(true);
            services.GetRequiredService<DeveloperCleanupViewModel>().ShowForAutomation();
            if (state == DevelopmentProjects)
            {
                var development = services.GetRequiredService<DeveloperCleanupViewModel>();
                development.Projects.ShowForAutomation();
                development.SelectedAreaIndex = 1;
            }

            if (state == DevelopmentWorkloads)
            {
                var development = services.GetRequiredService<DeveloperCleanupViewModel>();
                development.Workloads.ShowForAutomation();
                development.SelectedAreaIndex = 2;
            }

            if (state == DevelopmentMsBuild)
            {
                var development = services.GetRequiredService<DeveloperCleanupViewModel>();
                development.DotNetClean.ShowForAutomation();
                development.SelectedAreaIndex = 3;
            }

            services.GetRequiredService<CleanupPageViewModel>().SelectedSectionIndex = 2;
            return;
        }

        if (IsIdle(state))
        {
            switch (page)
            {
                case SectionKey.Cleanup:
                    services.GetRequiredService<CleanupPageViewModel>().SelectedSectionIndex = 0;
                    (await SettleCleanupAsync(services).ConfigureAwait(true)).ResetForAutomation();
                    break;

                case SectionKey.Docker:
                    var docker = services.GetRequiredService<DockerViewModel>();
                    await docker.EnsureLoadedAsync().ConfigureAwait(true);
                    await QuiesceAsync(() => docker.IsBusy, "опрос Docker").ConfigureAwait(true);
                    break;

                case SectionKey.Chat:
                    await SettleChatAsync(services).ConfigureAwait(true);
                    break;
            }

            return;
        }

        switch (page)
        {
            case SectionKey.Scan when state == Picker:
                ApplyPicker(services);
                break;

            case SectionKey.Scan when state == Welcome:
                ApplyWelcome(services);
                break;

            case SectionKey.Scan when state == Advice:
                services.GetRequiredService<ScanViewModel>().Tips.ShowForAutomation(0);
                break;

            case SectionKey.Scan:
                await ApplyScanAsync(services, fixture).ConfigureAwait(true);
                break;

            case SectionKey.Sync:
                ApplySync(services);
                break;

            case SectionKey.Overview:
                ApplyOverview(services);
                break;

            case SectionKey.Cleanup:
                await ApplyCleanupAsync(services).ConfigureAwait(true);
                break;

            case SectionKey.Docker:
                await ApplyDockerAsync(services).ConfigureAwait(true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(page), page, "У этой страницы нет занятого состояния.");
        }
    }

    public static void ResetPage(string page, string state, IServiceProvider services)
    {
        if (state is Development or DevelopmentProjects or DevelopmentWorkloads or DevelopmentMsBuild)
        {
            services.GetRequiredService<DeveloperCleanupViewModel>().SelectedAreaIndex = 0;
            services.GetRequiredService<ProjectCleanupViewModel>().ResetForAutomation();
            services.GetRequiredService<WorkloadCleanupViewModel>().ResetForAutomation();
            services.GetRequiredService<DotNetCleanViewModel>().ResetForAutomation();
            ResetCleanup(services);
            services.GetRequiredService<CleanupPageViewModel>().SelectedSectionIndex = 0;
            return;
        }

        if (IsIdle(state))
        {
            return;
        }

        switch (page)
        {
            case SectionKey.Scan when state == Picker:
                ResetPicker(services);
                break;

            case SectionKey.Scan when state == Welcome:
                ResetWelcome(services);
                break;

            case SectionKey.Scan when state == Advice:
                services.GetRequiredService<ScanViewModel>().Tips.DismissCommand.Execute(null);
                break;

            case SectionKey.Scan:
                ResetScan(services);
                break;

            case SectionKey.Sync:
                services.GetRequiredService<SyncViewModel>().Session.ClearBusyForAutomation();
                break;

            case SectionKey.Overview:
                ResetOverview(services);
                break;

            case SectionKey.Cleanup:
                ResetCleanup(services);
                break;

            case SectionKey.Docker:
                ResetDocker(services);
                break;
        }
    }

    public static void ApplyDialog(IDialogViewModel? dialog, string state)
    {
        if (IsIdle(state))
        {
            return;
        }

        switch (dialog)
        {
            case DeleteProgressDialogViewModel delete when state == Busy:
                delete.ShowRunningForAutomation(DeleteDone);
                break;

            case DeleteProgressDialogViewModel delete:
                delete.ShowFinishedForAutomation();
                break;

            case ArchiveProgressDialogViewModel archive when state == Busy:
                archive.ShowRunningForAutomation(ArchivePacked, ArchiveEntries, ArchiveBytes, ArchiveCurrent);
                break;

            case ArchiveProgressDialogViewModel archive:
                archive.ShowFinishedForAutomation(ArchiveEntries, ArchiveBytes, ArchiveCompressed);
                break;

            default:
                throw new InvalidOperationException($"У диалога «{dialog?.GetType().Name}» нет подставляемого состояния.");
        }
    }

    public static void ResetDialog(IDialogViewModel? dialog)
    {
        switch (dialog)
        {
            case DeleteProgressDialogViewModel delete:
                delete.ClearForAutomation();
                break;

            case ArchiveProgressDialogViewModel archive:
                archive.ClearForAutomation();
                break;
        }
    }

    internal static async Task<CleanupViewModel> SettleCleanupAsync(IServiceProvider services)
    {
        var cleanup = services.GetRequiredService<CleanupViewModel>();

        await Dispatcher.Yield(DispatcherPriority.Loaded);

        cleanup.CancelMeasureCommand.Execute(null);
        await QuiesceAsync(() => cleanup.IsBusy, "замер очистки").ConfigureAwait(true);

        return cleanup;
    }

    internal static async Task QuiesceAsync(Func<bool> busy, string operation)
    {
        for (var attempt = 0; attempt < AppDefaults.GalleryBusyAttempts && busy(); attempt++)
        {
            await Task.Delay(AppDefaults.GalleryBusyPollMs).ConfigureAwait(true);
        }

        if (busy())
        {
            var seconds = AppDefaults.GalleryBusyAttempts * AppDefaults.GalleryBusyPollMs / 1000d;

            throw new TimeoutException($"Собственный {operation} не кончился за {seconds:0.#} с – подстановка легла бы поверх него.");
        }
    }

    private static async Task ApplyScanAsync(IServiceProvider services, GalleryFixture fixture)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        var target = Path.Combine(fixture.Left, GalleryFixtures.MediaFolder);

        scan.ScanTargetPath = target;
        scan.SelectedNode = null;
        scan.HasResult = false;
        scan.IsScanning = true;

        var volume = new DirectoryInfo(Directory.GetDirectoryRoot(target));
        var occupied = await Task.Run(() => ScanProgressViewModel.EstimateTotalBytes(volume)).ConfigureAwait(true);
        var progress = scan.Progress.Begin(volume, target, ScanParallelism, occupied);

        scan.Progress.ShiftStartForAutomation(TimeSpan.FromSeconds(ScanElapsedSeconds));
        progress.SetTopLevelTotal(ScanBranchTotal);

        for (var branch = 0; branch < ScanBranchDone; branch++)
        {
            progress.CompleteTopLevel();
        }

        for (var directory = 0; directory < ScanDirectories; directory++)
        {
            progress.EnterDirectory(target);
        }

        progress.AddFiles(ScanFiles, occupied is > 0 ? (long)(occupied.Value * ScanFraction) : ScanBytesFallback);
    }

    private static void ResetScan(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();

        scan.Progress.Finish();
        scan.IsScanning = false;
        scan.HasResult = scan.Roots.Count > 0;
        scan.SelectedNode = scan.Roots.FirstOrDefault();
    }

    private static void ApplyPicker(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();

        var root = scan.Roots.FirstOrDefault()
            ?? throw new InvalidOperationException("Дерево сканирования пусто – помечать нечего.");

        root.EnsureLoaded();
        root.MarkContentsDeletedCommand.Execute(null);

        if (!scan.OpenPickerCommand.CanExecute(null))
        {
            throw new InvalidOperationException("Выбор цели не открывается – у скана нет результата.");
        }

        scan.OpenPickerCommand.Execute(null);
    }

    private static void ApplyWelcome(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();
        scan.FirstRun.ShowForAutomation(isElevated: false);
        scan.HasResult = false;
        scan.IsPickerOpen = false;

        if (!string.IsNullOrEmpty(scan.ScanTargetPath))
        {
            scan.Drives.RemoveDrive(scan.ScanTargetPath);
        }

        scan.SelectedDrive = scan.Drives.Volumes.FirstOrDefault()?.Path ?? string.Empty;
    }

    private static void ResetWelcome(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();

        if (!string.IsNullOrEmpty(scan.ScanTargetPath))
        {
            scan.SelectPathForAutomation(scan.ScanTargetPath);
        }

        scan.HasResult = scan.Roots.Count > 0;
        scan.FirstRun.ShowForAutomation(AdminElevation.IsElevated);
        scan.FirstRun.IsVisible = false;
    }

    private static void ResetPicker(IServiceProvider services)
    {
        var scan = services.GetRequiredService<ScanViewModel>();

        if (scan.ClosePickerCommand.CanExecute(null))
        {
            scan.ClosePickerCommand.Execute(null);
        }

        scan.Roots.FirstOrDefault()?.UnmarkContentsCommand.Execute(null);
    }

    private static void ApplySync(IServiceProvider services)
    {
        var sync = services.GetRequiredService<SyncViewModel>();

        sync.Session.ShowBusyForAutomation(
            SyncOperationsViewModel.CompareCaption,
            new(CompareCompleted, CurrentFile(sync)),
            TimeSpan.FromSeconds(CompareElapsedSeconds));
    }

    private static string CurrentFile(SyncViewModel sync)
    {
        var tree = sync.Rows.FlatView;
        sync.Rows.FlatView = true;

        try
        {
            return sync.Rows.Rows.FirstOrDefault(static row => row.IsFile)?.RelativePath ?? string.Empty;
        }
        finally
        {
            sync.Rows.FlatView = tree;
        }
    }

    private static void ApplyOverview(IServiceProvider services)
    {
        var overview = services.GetRequiredService<OverviewViewModel>();
        var targets = overview.Rows.Rows.Where(static row => row.IncludeInBatch).ToList();

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("На странице «Все профили» нет профилей пакета – прогон изображать не на чем.");
        }

        var running = Math.Min(BatchIndex, targets.Count - 1);

        for (var index = 0; index < targets.Count; index++)
        {
            var row = targets[index];

            row.Status = index switch
            {
                _ when index < running => OverviewRunStatus.Compared,
                _ when index == running => OverviewRunStatus.Comparing,
                _ => OverviewRunStatus.None,
            };

            if (row.Status == OverviewRunStatus.Compared)
            {
                MarkCompared(row);
            }
        }

        overview.Batch.ShowBusyForAutomation(running, targets.Count, targets[running].Name);
    }

    private static void MarkCompared(OverviewRowViewModel row)
    {
        row.LeftOnlyCount = ComparedLeftOnly;
        row.RightOnlyCount = ComparedRightOnly;
        row.ModifiedCount = ComparedModified;
        row.IdenticalCount = ComparedIdentical;
        row.ElapsedMs = ComparedElapsedMs;
    }

    private static void ResetOverview(IServiceProvider services)
    {
        var overview = services.GetRequiredService<OverviewViewModel>();

        foreach (var row in overview.Rows.Rows)
        {
            row.Status = OverviewRunStatus.None;
        }

        overview.Batch.ClearBusyForAutomation();
    }

    private static async Task ApplyCleanupAsync(IServiceProvider services)
    {
        services.GetRequiredService<CleanupPageViewModel>().SelectedSectionIndex = 0;
        var cleanup = await SettleCleanupAsync(services).ConfigureAwait(true);

        cleanup.ShowBusyForAutomation(CleanupMeasured);
    }

    private static async Task SettleChatAsync(IServiceProvider services)
    {
        var gates = services.GetRequiredService<ChatViewModel>().Gates;

        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await QuiesceAsync(() => gates.IsDetectingCli, "поиск CLI агента").ConfigureAwait(true);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);

        Keyboard.ClearFocus();
    }

    private static void ResetCleanup(IServiceProvider services)
    {
        services.GetRequiredService<CleanupViewModel>().ResetForAutomation();
    }

    private static async Task ApplyDockerAsync(IServiceProvider services)
    {
        await SettleCleanupAsync(services).ConfigureAwait(true);
        var docker = services.GetRequiredService<DockerViewModel>();

        await docker.EnsureLoadedAsync().ConfigureAwait(true);
        await QuiesceAsync(() => docker.IsBusy, "опрос Docker").ConfigureAwait(true);

        docker.ShowBusyForAutomation();
    }

    private static void ResetDocker(IServiceProvider services)
    {
        var docker = services.GetRequiredService<DockerViewModel>();

        docker.ClearBusyForAutomation();
        docker.StatusText = null;
        ResetCleanup(services);
        services.GetRequiredService<CleanupPageViewModel>().SelectedSectionIndex = 0;
    }
}
