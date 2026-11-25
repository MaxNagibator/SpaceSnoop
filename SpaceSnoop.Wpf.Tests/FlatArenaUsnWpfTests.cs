using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class FlatArenaUsnWpfTests
{
    private string _directory = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private BindingErrorSink _sink = null!;
    private MainWindow _window = null!;
    private ScanViewModel _scan = null!;
    private ScanNodeFactory _factory = null!;
    private ToastHostViewModel _toasts = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        _directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopFlatArenaUsn", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetBool(SettingsKeys.WelcomePending, false);
        settings.SetValue(SettingsKeys.EnableToastNotifications, "true");

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_directory, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _scan = _services.GetRequiredService<ScanViewModel>();
        _factory = _services.GetRequiredService<ScanNodeFactory>();
        _toasts = _services.GetRequiredService<ToastHostViewModel>();
        _window = _services.GetRequiredService<MainWindow>();
        _window.Width = 1280;
        _window.Height = 800;
        _host = VisualTestHost.Show(_window);
        _ = _services.GetRequiredService<ShellViewModel>().TryNavigate(SectionKey.Scan);
        _host.Settle();
        _sink = BindingErrorSink.Attach();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _sink?.Dispose();
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _scan.Roots.Clear();
        _scan.SelectSingle(null);
        _factory.Visibility.Clear();
        _scan.Marks.RecountMarked();
        _toasts.Toasts.Clear();
        _scan.HasResult = true;
        _scan.IsPickerOpen = false;
        _scan.SelectedViewIndex = 0;
        _scan.Preferences.ReturnMemoryAfterScan = false;
        _host.Settle();
        _sink.Clear();
    }

    [Test]
    public void Аренные_пометки_обходят_дерево_без_материализации_фасадов_и_снимаются_рекурсивно()
    {
        var (store, root, _) = BuildFlatArena(Path.Combine(_directory, Guid.NewGuid().ToString("N")), nestedFileDeleted: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.MaterializedDirectoryFacadeCount, Is.EqualTo(1));
            Assert.That(ScanTreeEditor.HasMarkedSelfOrChild(root), Is.True);
            Assert.That(store.MaterializedDirectoryFacadeCount, Is.EqualTo(1));
        }

        ScanNodeViewModel.RestoreRecursive(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.MaterializedDirectoryFacadeCount, Is.EqualTo(1));
            Assert.That(ScanTreeEditor.HasMarkedSelfOrChild(root), Is.False);
        }

        var (markedStore, markedRoot, _) = BuildFlatArena(Path.Combine(_directory, Guid.NewGuid().ToString("N")), nestedFileDeleted: true);
        var rootNode = _factory.CreateRoot(markedRoot, new());
        _factory.MarksPresent = true;
        var countAfterRootNode = markedStore.MaterializedDirectoryFacadeCount;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(countAfterRootNode, Is.LessThan(4));
            Assert.That(rootNode.CanMarkContentsDeleted, Is.True);
            Assert.That(rootNode.CanUnmarkContents, Is.True);
            Assert.That(markedStore.MaterializedDirectoryFacadeCount, Is.EqualTo(countAfterRootNode));
        }

        var collected = ScanTreeEditor.CollectMarked(markedRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collected, Has.Count.EqualTo(1));
            Assert.That(collected[0].Name, Is.EqualTo("deep.bin"));
            Assert.That(collected[0].TotalSize, Is.EqualTo(64));
        }

        ScanNodeViewModel.RestoreRecursive(markedRoot);
        Assert.That(rootNode.CanUnmarkContents, Is.False);

        GC.KeepAlive(store);
        GC.KeepAlive(markedStore);
    }

    [Test]
    public void Пакетная_пометка_и_откат_работают_на_плоской_арене_после_сборки_мусора()
    {
        var (storeWeak, rootWeak, branchFileWeak, undo) = CreateFlatArenaAndMarkBranch(useApplyScanResult: false);

        ForceFullGc();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(storeWeak.TryGetTarget(out _), Is.True);
            Assert.That(rootWeak.TryGetTarget(out _), Is.True);
            Assert.That(branchFileWeak.TryGetTarget(out _), Is.True);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));
            Assert.That(CountUndoPins(rootWeak), Is.EqualTo(1));
        }

        ExecuteUndoOnFlatArena(undo, rootWeak);
        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        GC.KeepAlive(undo);
    }

    [Test]
    public void Освобождение_корня_на_плоской_арене_позволяет_собрать_корень_и_ScanTreeStore()
    {
        var (storeWeakA, rootWeakA, _, undoA) = CreateFlatArenaAndMarkBranch(useApplyScanResult: true);
        DropRootsAndSelection();
        ForceFullGc();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootWeakA.TryGetTarget(out _), Is.False);
            Assert.That(storeWeakA.TryGetTarget(out _), Is.False);
        }

        GC.KeepAlive(undoA);

        var (storeWeakB, rootWeakB, _, undoB) = CreateFlatArenaAndMarkBranch(useApplyScanResult: false);
        ExecuteUndoOnFlatArena(undoB, rootWeakB);
        DropRootsAndSelection();
        ForceFullGc();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootWeakB.TryGetTarget(out _), Is.False);
            Assert.That(storeWeakB.TryGetTarget(out _), Is.False);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }

        GC.KeepAlive(undoB);
    }

    [Test]
    public void TransferMarks_на_том_же_экземпляре_корня_сохраняет_пометки_USN_обновления()
    {
        var (store, fresh, branchIdx, nestedIdx) = BuildFlatArenaWithIndices(Path.Combine(_directory, Guid.NewGuid().ToString("N")));

        MarkDirectoryAndNestedFileInHelper(store, branchIdx, nestedIdx);
        var previousRoot = _factory.CreateRoot(fresh, new());

        var (transferred, lost) = ScanTreeEditor.TransferMarks([previousRoot], fresh, marksPresent: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lost, Is.Zero);
            Assert.That(transferred, Has.Count.EqualTo(1));
            Assert.That(transferred[0].Name, Is.EqualTo("branch"));
            Assert.That(transferred[0].IsDeleted, Is.True);
        }

        ScanNodeViewModel.RestoreRecursive(fresh);
        fresh.Delete();

        var (rootTransferred, rootLost) = ScanTreeEditor.TransferMarks([previousRoot, previousRoot], fresh, marksPresent: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootLost, Is.Zero);
            Assert.That(rootTransferred, Is.EqualTo(new SpaceBase[] { fresh }));
        }

        GC.KeepAlive(store);
    }

    [Test]
    public void TransferMarks_учитывает_UsnLostMarks_при_внешнем_удалении_помеченного_файла_на_том_же_корне()
    {
        var (store, fresh, branchIdx, nestedIdx) = BuildFlatArenaWithIndices(Path.Combine(_directory, Guid.NewGuid().ToString("N")));
        MarkNestedFileInHelper(store, nestedIdx);
        var previousRoot = _factory.CreateRoot(fresh, new());

        var entries = new MftEntry[32];
        entries[5] = new MftEntry { Present = true, IsDirectory = true, HasName = true, Sequence = 1 };
        entries[20] = new MftEntry { Present = true, IsDirectory = true, HasName = true, Sequence = 1, Parent = 5, ParentSequence = 1 };
        entries[21] = new MftEntry { Present = true, IsDirectory = true, HasName = true, Sequence = 1, Parent = 20, ParentSequence = 1 };
        entries[22] = new MftEntry { Present = true, HasName = true, Sequence = 1, Parent = 21, ParentSequence = 1, Size = 64, SizeKnown = true };

        var table = new MftTable(entries, new MftStatistics(), [], store);
        var capture = new MftUsnMaterializeCapture(entries.Length);
        capture.FrnLocation[5] = 0;
        capture.FrnSequence[5] = 1;
        capture.FrnLocation[20] = branchIdx;
        capture.FrnSequence[20] = 1;
        capture.FrnLocation[21] = nestedIdx;
        capture.FrnSequence[21] = 1;
        capture.FrnLocation[22] = ~nestedIdx;
        capture.FrnNameId[22] = store.GetDirectoryRef(nestedIdx).FileCount > 0
            ? fresh.SubDirectories.Single(static d => d.Name == "branch").SubDirectories.Single().FileEntries[0].NameId
            : 0;
        capture.FrnSequence[22] = 1;

        var usnIndex = new MftUsnVolumeIndex(store, new MftUsnJournalInfo(1UL, 0, 100, 0, 1u), table, capture);
        var source = new SingleDeleteUsnSource(deleteFrn: (1UL << MftLayout.ReferenceMaskBits) | 22u, journalNextUsn: 200);

        var ok = usnIndex.TryRefresh(source, out var summary, CancellationToken.None);
        Assert.That(ok, Is.True);
        Assert.That(summary.RemovedMarkedCount, Is.EqualTo(1));

        var (transferred, lost) = ScanTreeEditor.TransferMarks([previousRoot], fresh, marksPresent: true, summary.RemovedMarkedCount);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lost, Is.EqualTo(1));
            Assert.That(transferred, Is.Empty);
        }

        GC.KeepAlive(store);
    }

    [Test]
    public async Task ScanNotes_пробрасывает_UsedUsn_а_USN_обновление_не_планирует_Gen2_сборку()
    {
        var runner = new ScanRunner(new(), new(), new(new MemorySettings()), NullLogger<ScanRunner>.Instance);
        var traversalNotes = ScanNotes.ForTraversal(2);
        var mftNotes = new ScanNotes(true, 4, 0, 0, 0, 0, 0);
        var usnNotes = new ScanNotes(true, 4, 0, 0, 0, 0, 0, UsedUsn: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(traversalNotes.UsedUsn, Is.False);
            Assert.That(mftNotes.UsedUsn, Is.False);
            Assert.That(usnNotes.UsedUsn, Is.True);
            Assert.That(runner.HasUsnCacheFor(_directory), Is.False);
            Assert.That(runner.HasUsnCacheFor(@"\\server\share\dir"), Is.False);
        }

        runner.InvalidateUsnCache(_directory);
        runner.InvalidateUsnCache();

        var large = new DirectorySpace(Path.Combine(_directory, "large"), null, DateTime.Today, DateTime.Today);
        large.SetAggregatedTotals(1_000_000, (int)AppDefaults.ScanMemoryReturnMinNodes, 1);

        var usnTask = _scan.ScheduleReturnScanMemory(large, usedMft: true, usedUsn: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(usnTask.IsCompleted, Is.True);
            Assert.That(await usnTask, Is.EqualTo(ScanMemoryReturnOutcome.Skipped));
        }

        var fullMftTask = _scan.ScheduleReturnScanMemory(large, usedMft: true, usedUsn: false);
        Assert.That(fullMftTask.IsCompleted, Is.False);
        _scan.ApplyScanResult(BuildFlatArena(Path.Combine(_directory, "cancel")).Root, TimeSpan.FromSeconds(1), null, usnNotes);
        Assert.That(await fullMftTask.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(ScanMemoryReturnOutcome.Cancelled));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MarkNestedFileInHelper(ScanTreeStore store, int nestedIdx)
    {
        store.GetOrCreateDirectoryFacade(nestedIdx).GetFile(0).Delete();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MarkDirectoryAndNestedFileInHelper(ScanTreeStore store, int branchIdx, int nestedIdx)
    {
        store.GetOrCreateDirectoryFacade(branchIdx).Delete();
        store.GetOrCreateDirectoryFacade(nestedIdx).GetFile(0).Delete();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (
        WeakReference<ScanTreeStore> StoreWeak,
        WeakReference<DirectorySpace> RootWeak,
        WeakReference<SpaceBase> NestedFileWeak,
        ToastViewModel Undo) CreateFlatArenaAndMarkBranch(bool useApplyScanResult = true)
    {
        var caseDir = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var (store, root, _, nestedIdx) = BuildFlatArenaWithIndices(caseDir);

        MarkNestedFileInHelper(store, nestedIdx);
        var nestedFileWeak = new WeakReference<SpaceBase>(store.GetOrCreateDirectoryFacade(nestedIdx).GetFile(0));

        if (useApplyScanResult)
        {
            _scan.ApplyScanResult(root, TimeSpan.FromSeconds(1), null, new ScanNotes(true, 1, 0, 0, 0, 0, 0, UsedUsn: true));
        }
        else
        {
            var node = _factory.CreateRoot(root, new(), null);
            node.IsExpanded = true;
            _scan.Roots.Add(node);
        }

        _scan.Marks.RecountMarked();
        _host.Settle();

        var rootNode = _scan.Roots[0];
        var branchNode = rootNode.Children.Single(static child => child.Name == "branch");
        _scan.SelectSingle(branchNode);
        _host.Settle();

        _scan.MarkSelectedDeleted();
        _host.Settle();

        _scan.SelectSingle(null);
        _host.Settle();

        var undo = _toasts.Toasts.Single(static toast => toast.HasAction && toast.ActionText == "Отменить");
        return (new(store), new(root), nestedFileWeak, undo);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ExecuteUndoOnFlatArena(ToastViewModel undo, WeakReference<DirectorySpace> rootWeak)
    {
        undo.ActionCommand!.Execute(undo);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CountUndoPins(rootWeak), Is.EqualTo(0));
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));
            var root = (DirectorySpace)_scan.Roots[0].Space!;
            var branch = root.SubDirectories.Single(static d => d.Name == "branch");
            var nested = branch.SubDirectories.Single(static d => d.Name == "nested");
            Assert.That(branch.IsDeleted, Is.False);
            Assert.That(nested.FileEntries[0].State, Is.EqualTo(SpaceState.Deleted));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DropRootsAndSelection()
    {
        _scan.SelectSingle(null);
        _scan.Roots.Clear();
        _scan.Marks.RecountMarked();
        _toasts.Toasts.Clear();
        _host.Settle();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int CountUndoPins(WeakReference<DirectorySpace> rootWeak)
    {
        return rootWeak.TryGetTarget(out var root) ? _scan.UndoFacadePinCount(root) : 0;
    }

    private static (ScanTreeStore Store, DirectorySpace Root, int NestedIdx) BuildFlatArena(
        string rootPath,
        bool nestedFileDeleted = false)
    {
        var (store, root, _, nestedIdx) = BuildFlatArenaWithIndices(rootPath, nestedFileDeleted);
        return (store, root, nestedIdx);
    }

    private static (ScanTreeStore Store, DirectorySpace Root, int BranchIdx, int NestedIdx) BuildFlatArenaWithIndices(
        string rootPath,
        bool nestedFileDeleted = false)
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var rootIdx = store.AllocateDirectorySlot();
        var branchIdx = store.AllocateDirectorySlot();
        var nestedIdx = store.AllocateDirectorySlot();
        var otherIdx = store.AllocateDirectorySlot();

        writer.WriteFiles(
            [
                new FileEntry
                {
                    Size = 64,
                    NameId = store.Names.Intern("deep.bin"),
                    State = nestedFileDeleted ? SpaceState.Deleted : SpaceState.Added,
                },
            ],
            out var nestedStart);
        writer.WriteFiles(
            [new FileEntry { Size = 128, NameId = store.Names.Intern("other.bin"), State = SpaceState.Added }],
            out var otherStart);

        store.GetDirectoryRef(rootIdx) = new()
        {
            NameId = store.Names.Intern(rootPath),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = 192,
            TotalFileCount = 2,
            TotalDirCount = 3,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(branchIdx) = new()
        {
            NameId = store.Names.Intern("branch"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = 64,
            TotalFileCount = 1,
            TotalDirCount = 1,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(nestedIdx) = new()
        {
            NameId = store.Names.Intern("nested"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            FileStart = nestedStart,
            FileCount = 1,
            Size = 64,
            TotalSize = 64,
            TotalFileCount = 1,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(otherIdx) = new()
        {
            NameId = store.Names.Intern("other"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            FileStart = otherStart,
            FileCount = 1,
            Size = 128,
            TotalSize = 128,
            TotalFileCount = 1,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };

        store.LinkChildDirectory(branchIdx, nestedIdx);
        store.LinkChildDirectory(rootIdx, otherIdx);
        store.LinkChildDirectory(rootIdx, branchIdx);
        store.Freeze();

        return (store, store.GetOrCreateDirectoryFacade(rootIdx), branchIdx, nestedIdx);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullGc()
    {
#pragma warning disable S1215
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
#pragma warning restore S1215
    }

    private sealed class SingleDeleteUsnSource(ulong deleteFrn, long journalNextUsn) : IMftUsnSource
    {
        private bool _sent;

        public bool TryQueryJournal(out MftUsnJournalInfo info, out MftUsnErrorKind error, out int win32Error)
        {
            info = new(1UL, 0, journalNextUsn, 0, 1u);
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryReadDelta(
            ulong journalId,
            long startUsn,
            int maxEvents,
            List<MftUsnEvent> destination,
            out long nextUsn,
            out MftUsnErrorKind error,
            out int win32Error,
            CancellationToken cancel)
        {
            if (!_sent)
            {
                _sent = true;
                destination.Add(new MftUsnEvent(deleteFrn, 0x8000_0200u));
            }

            nextUsn = journalNextUsn;
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryFetchFileRecord(int recordNumber, out MftUsnRecord record, out MftUsnErrorKind error, out int win32Error)
        {
            record = default;
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryQueryLiveFileSize(ulong frn, out long size)
        {
            size = 0;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
