using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Testing;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class NetworkStage3OperationsTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-stage3-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        for (var attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                Directory.Delete(_root, true);
                return;
            }
            catch (IOException) when (attempt < 14)
            {
                Thread.Sleep(30);
            }
            catch (UnauthorizedAccessException) when (attempt < 14)
            {
                Thread.Sleep(30);
            }
        }
    }

    [Test]
    public async Task Архивация_на_сети_с_удалением_оригинала_по_умолчанию_оставляет_оригинал_и_удаляет_после_проверок_при_согласии()
    {
        var source = Path.Combine(_root, "folder");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "data.txt"), "payload");

        var previousProbe = ArchiveProgressDialogViewModel.SupportsRecycleBin;
        ArchiveProgressDialogViewModel.SupportsRecycleBin = _ => false;

        try
        {
            var target1 = Path.Combine(_root, "folder.zip");
            var deletedCalls = 0;
            var request1 = new ArchiveRequest(source, target1, 1, 7, DeleteOriginal: true, System.IO.Compression.CompressionLevel.Optimal, Interactive: true);
            var dialog1 = new ArchiveProgressDialogViewModel(
                request1,
                new(),
                new FakeUiDispatcher(),
                NullLogger<ArchiveProgressDialogViewModel>.Instance,
                (_, _) => deletedCalls++);

            Assert.Multiple(() =>
            {
                Assert.That(dialog1.NeedsPermanentOriginalConfirm, Is.True);
                Assert.That(dialog1.ConfirmPermanentOriginalDelete, Is.False);
                Assert.That(dialog1.FateText, Does.Contain("Оригинал останется на месте"));
            });

            await dialog1.StartCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(target1), Is.True);
                Assert.That(Directory.Exists(source), Is.True);
                Assert.That(dialog1.OriginalDeleted, Is.False);
                Assert.That(deletedCalls, Is.Zero);
            });

            var target2 = Path.Combine(_root, "folder2.zip");
            var request2 = new ArchiveRequest(source, target2, 1, 7, DeleteOriginal: true, System.IO.Compression.CompressionLevel.Optimal, Interactive: true);
            var dialog2 = new ArchiveProgressDialogViewModel(
                request2,
                new(),
                new FakeUiDispatcher(),
                NullLogger<ArchiveProgressDialogViewModel>.Instance,
                (_, _) => deletedCalls++)
            {
                ConfirmPermanentOriginalDelete = true,
            };

            Assert.That(dialog2.FateText, Does.Contain("удалён безвозвратно"));

            await dialog2.StartCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(target2), Is.True);
                Assert.That(dialog2.OriginalDeleted, Is.True);
                Assert.That(Directory.Exists(source), Is.False);
                Assert.That(deletedCalls, Is.Zero);
            });
        }
        finally
        {
            ArchiveProgressDialogViewModel.SupportsRecycleBin = previousProbe;
        }
    }

    [Test]
    public async Task Подтверждение_синхронизации_на_сети_предупреждает_об_отсутствии_корзины_и_разрешает_удаление()
    {
        var left = Path.Combine(_root, "left");
        var right = Path.Combine(_root, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        await File.WriteAllTextAsync(Path.Combine(right, "extra.txt"), "remove");

        var previousProbe = SyncPlanNarrative.SupportsRecycleBin;
        SyncPlanNarrative.SupportsRecycleBin = path => !string.Equals(path, right, StringComparison.OrdinalIgnoreCase);

        try
        {
            var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
            var comparison = compare.Execute(new(left, right, string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, Mirror: true), CancellationToken.None);
            var ledger = new SyncLedgerViewModel(
                new(new MemorySettings(), new NoopDialogs(), NullLogger.Instance),
                () => new(PackIconLucideKind.ArrowRight, "слева направо", false));
            ledger.Update(comparison, false);

            var confirm = (await ledger.BuildSyncConfirmationAsync(comparison, new("Сверить хеши", ConfirmChoiceKind.Secondary), recycleOverwritten: true))?.Dialog;
            Assert.That(confirm, Is.Not.Null);

            var text = ConfirmDialogViewModel.AsText(confirm!.Lines);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(confirm.Warning, Does.Contain("На сетевом хранилище нет корзины Windows"));
                Assert.That(confirm.Summary, Does.Not.Contain("в корзину"));
                Assert.That(text, Does.Not.Contain("в корзину"));
                Assert.That(text, Does.Contain("безвозвратно"));
            }

            var sync = new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance, path => !string.Equals(path, right, StringComparison.OrdinalIgnoreCase));
            var report = sync.Execute(new(comparison, SyncConflictPolicy.None, SyncDeleteUi.Silent, VerifyAfterSync: false, RecycleOverwritten: false, AllowPermanentNetworkDelete: true), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Errors, Is.Empty);
                Assert.That(File.Exists(Path.Combine(right, "extra.txt")), Is.False);
            });
        }
        finally
        {
            SyncPlanNarrative.SupportsRecycleBin = previousProbe;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Обзор_предупреждает_об_удалении_на_сетевом_приёмнике_даже_при_устаревшем_плане(bool batchRun)
    {
        var left = Path.Combine(_root, "ov-left");
        var right = Path.Combine(_root, "ov-right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        await File.WriteAllTextAsync(Path.Combine(left, "keep.txt"), "source");

        var previousProbe = SyncPlanNarrative.SupportsRecycleBin;
        SyncPlanNarrative.SupportsRecycleBin = path => !string.Equals(path, right, StringComparison.OrdinalIgnoreCase);

        try
        {
            var settings = new MemorySettings();
            var profile = new SyncProfile
            {
                Id = "p1",
                Name = "Сетевой бэкап",
                Left = left,
                Right = right,
                Mode = 0,
                Mirror = true,
                Winner = SyncWinner.Newest,
                Exclusions = string.Empty,
                Enabled = false,
                Interval = ScheduleInterval.Daily,
                Time = "03:00",
            };
            SyncProfileStore.Save(settings, [profile]);

            ConfirmDialogViewModel? captured = null;
            var dialogs = new NoopDialogs(show: modal =>
            {
                captured = modal as ConfirmDialogViewModel;
                return true;
            });

            var rows = new OverviewRowsViewModel(settings, static () => false, static () => false, static () => { });
            var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
            var comparison = compare.Execute(new(left, right, string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, true), CancellationToken.None);
            rows.Rows.Add(new(profile, static (_, _) => { }, static () => { }) { Comparison = comparison });
            await File.WriteAllTextAsync(Path.Combine(right, "old.txt"), "gone");

            using var monitor = TestDiagnostics.Monitor();
            var batch = new OverviewBatchViewModel(
                settings,
                dialogs,
                new(new KeepShell.ViewModels.ToastHostViewModel(new FakeUiDispatcher()), new(settings)),
                NullLogger.Instance,
                compare,
                new(NullLogger<SyncEngine>.Instance, path => !string.Equals(path, right, StringComparison.OrdinalIgnoreCase)),
                rows);

            if (batchRun)
            {
                await batch.SyncAllCommand.ExecuteAsync(null);
            }
            else
            {
                await batch.SyncRowCommand.ExecuteAsync(rows.Rows[0]);
            }

            Assert.That(captured, Is.Not.Null);
            var text = ConfirmDialogViewModel.AsText(captured!.Lines);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured.Warning, Does.Contain("На сетевом хранилище нет корзины Windows"));
                Assert.That(text, Does.Not.Contain("в корзину"));
                Assert.That(rows.Rows[0].Status, Is.EqualTo(OverviewRunStatus.Synced));
                Assert.That(File.Exists(Path.Combine(right, "old.txt")), Is.False);
            }
        }
        finally
        {
            SyncPlanNarrative.SupportsRecycleBin = previousProbe;
        }
    }

    [Test]
    public async Task MCP_отклоняет_архивацию_с_удалением_без_корзины_и_неполный_UNC_в_open_scan()
    {
        var settings = new MemorySettings();
        var preferences = new McpPreferences(settings)
        {
            AllowMutations = true,
        };
        var scanPreferences = new ScanPreferences(settings);
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        using var networkDrives = new NetworkDriveRestore();

        var bridge = new McpBridge(
            settings,
            new SyncAutomationDouble(),
            new ScanAutomationDouble(),
            preferences,
            scanPreferences,
            new(new(), new(), scanPreferences, NullLogger<ScanRunner>.Instance),
            new(),
            new(),
            new(),
            new CleanupAutomationDouble(),
            new(new(), new(settings)),
            monitor,
            operations,
            new(NullLogger<DirectoryComparer>.Instance),
            new FakeAppNavigator(),
            networkDrives,
            NullLogger<McpBridge>.Instance);

        var openException = await Assert.ThrowsAsync<McpException>(() => bridge.Scan.OpenScanAsync(@"\\", false, CancellationToken.None));
        Assert.That(openException!.Message, Does.Contain(@"\\сервер\шара"));

        var archivePlanException = await Assert.ThrowsAsync<McpException>(() => bridge.Scan.ArchiveDirectoryAsync(@"\\nas\media\sub", deleteOriginal: true, dryRun: true, CancellationToken.None));
        Assert.That(archivePlanException!.Message, Does.Contain("не поддерживает корзину"));

        var archiveRunException = await Assert.ThrowsAsync<McpException>(() => bridge.Scan.ArchiveDirectoryAsync(@"\\nas\media\sub", deleteOriginal: true, dryRun: false, CancellationToken.None));
        Assert.That(archiveRunException!.Message, Does.Contain("не поддерживает корзину"));
    }

    [Test]
    public async Task MCP_sync_current_отклоняет_план_с_сетевым_удалением_до_мутаций()
    {
        var settings = new MemorySettings();
        var preferences = new McpPreferences(settings)
        {
            AllowMutations = true,
        };
        var scanPreferences = new ScanPreferences(settings);
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        using var networkDrives = new NetworkDriveRestore();
        var syncDouble = new SyncAutomationDouble
        {
            HasComparison = true,
            PlanFreshness = SyncPlanFreshness.Fresh,
            HasPermanentNetworkDelete = true,
        };

        var bridge = new McpBridge(
            settings,
            syncDouble,
            new ScanAutomationDouble(),
            preferences,
            scanPreferences,
            new(new(), new(), scanPreferences, NullLogger<ScanRunner>.Instance),
            new(),
            new(),
            new(),
            new CleanupAutomationDouble(),
            new(new(), new(settings)),
            monitor,
            operations,
            new(NullLogger<DirectoryComparer>.Instance),
            new FakeAppNavigator(),
            networkDrives,
            NullLogger<McpBridge>.Instance);

        var exception = await Assert.ThrowsAsync<McpException>(() => bridge.Sync.SyncCurrentAsync(dryRun: false, entryLimit: 50, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("на сетевом хранилище нет корзины Windows"));
            Assert.That(syncDouble.SyncCalls, Is.Zero);
        }
    }

    [Test]
    public void HeadlessSync_разрешает_сетевое_удаление_только_с_согласием_для_текущего_приёмника()
    {
        var mirrorOptions = new HeadlessSync.RunOptions("p", @"C:\a", @"\\nas\b", SyncMode.LeftToRight, Mirror: true, SyncWinner.Newest, string.Empty);
        var consent = mirrorOptions with { PermanentNetworkDeleteTarget = mirrorOptions.Right };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete(null, consent), Is.False);
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete("p1", mirrorOptions), Is.False);
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete("p1", consent with { Mirror = false }), Is.False);
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete("p1", consent with { Right = @"\\nas\other" }), Is.False);
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete("p1", consent with { Mode = SyncMode.RightToLeft }), Is.False);
            Assert.That(HeadlessSync.ShouldAllowPermanentNetworkDelete("p1", consent), Is.True);
        }
    }

    [Test]
    public void Предупреждение_плана_проверяет_корзину_на_вложенных_путях_локального_корня()
    {
        var left = Path.Combine(_root, "local-left");
        var right = Path.Combine(_root, "local-right");
        var nestedDir = Path.Combine(right, "mnt");
        var nestedFile = Path.Combine(nestedDir, "remote.txt");

        var rootComparison = new DirectoryComparison("root", string.Empty);
        var dirComparison = new DirectoryComparison("mnt", "mnt");
        dirComparison.Files.Add(
            new("remote.txt", Path.Combine("mnt", "remote.txt"))
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 10,
            });
        rootComparison.SubDirectories.Add(dirComparison);

        var result = new ComparisonResult(left, right, rootComparison);
        var previousProbe = SyncPlanNarrative.SupportsRecycleBin;
        SyncPlanNarrative.SupportsRecycleBin = path => !string.Equals(path, nestedFile, StringComparison.OrdinalIgnoreCase);

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SyncPlanNarrative.HasNetworkDeleteOrOverwrite(result, recycleOverwritten: false), Is.True);
                Assert.That(SyncPlanNarrative.HasUnblockedNetworkDelete(result), Is.True);
            }
        }
        finally
        {
            SyncPlanNarrative.SupportsRecycleBin = previousProbe;
        }
    }

    [Test]
    public void HasPermanentNetworkDeleteAsync_проверяет_корзину_вне_потока_вызова()
    {
        var left = Path.Combine(_root, "auto-left");
        var right = Path.Combine(_root, "auto-right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        File.WriteAllText(Path.Combine(right, "old.txt"), "gone");

        var probeThread = 0;
        var probeIsPool = false;
        var previousProbe = SyncPlanNarrative.SupportsRecycleBin;

        SyncPlanNarrative.SupportsRecycleBin = path =>
        {
            Interlocked.Exchange(ref probeThread, Environment.CurrentManagedThreadId);
            Volatile.Write(ref probeIsPool, Thread.CurrentThread.IsThreadPoolThread);
            return !string.Equals(path, right, StringComparison.OrdinalIgnoreCase);
        };

        try
        {
            var settings = new MemorySettings();
            var shell = new ShellPreferences(settings);
            var page = new SyncViewModel(
                settings,
                new NoopDialogs(),
                new(settings),
                new(settings),
                NullLogger<SyncViewModel>.Instance,
                new(NullLogger<DirectoryComparer>.Instance),
                new(NullLogger<SyncEngine>.Instance),
                new(new(), shell),
                new(TestDiagnostics.Monitor()),
                new FakeFilePicker(),
                new FakeUiDispatcher(),
                new FakeAppNavigator());

            var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
            var comparison = compare.Execute(new(left, right, string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, Mirror: true), CancellationToken.None);
            page.Setup.Mirror = true;
            page.Operations.AdoptComparison(comparison);

            var callerThread = 0;
            var hasNetworkDelete = false;
            var caller = new Thread(() =>
            {
                callerThread = Environment.CurrentManagedThreadId;
                hasNetworkDelete = ((ISyncAutomation)page).HasPermanentNetworkDeleteAsync(CancellationToken.None).GetAwaiter().GetResult();
            });
            caller.Start();
            caller.Join();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hasNetworkDelete, Is.True);
                Assert.That(callerThread, Is.Not.Zero);
                Assert.That(Volatile.Read(ref probeThread), Is.Not.Zero);
                Assert.That(Volatile.Read(ref probeThread), Is.Not.EqualTo(callerThread));
                Assert.That(Volatile.Read(ref probeIsPool), Is.True);
            }
        }
        finally
        {
            SyncPlanNarrative.SupportsRecycleBin = previousProbe;
        }
    }

    [Test]
    public void HasNetworkDeleteOrOverwrite_кэширует_проверку_каталога_при_множестве_файлов()
    {
        var left = Path.Combine(_root, "cache-left");
        var right = Path.Combine(_root, "cache-right");
        var subdir = Path.Combine(right, "nested");
        var rootComparison = new DirectoryComparison("root", string.Empty);
        var dirComparison = new DirectoryComparison("nested", "nested");

        for (var i = 0; i < 25; i++)
        {
            var name = $"f{i}.txt";
            dirComparison.Files.Add(
                new(name, Path.Combine("nested", name))
                {
                    Status = ComparisonStatus.RightOnly,
                    Action = SyncAction.DeleteRight,
                    RightSize = 10,
                });
        }

        rootComparison.SubDirectories.Add(dirComparison);
        var result = new ComparisonResult(left, right, rootComparison);
        var dirCalls = 0;
        var previousProbe = SyncPlanNarrative.SupportsRecycleBin;

        SyncPlanNarrative.SupportsRecycleBin = path =>
        {
            if (string.Equals(path, subdir, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref dirCalls);
            }

            return true;
        };

        try
        {
            var hasDelete = SyncPlanNarrative.HasNetworkDeleteOrOverwrite(result, recycleOverwritten: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hasDelete, Is.False);
                Assert.That(Volatile.Read(ref dirCalls), Is.EqualTo(1));
            }
        }
        finally
        {
            SyncPlanNarrative.SupportsRecycleBin = previousProbe;
        }
    }
}
