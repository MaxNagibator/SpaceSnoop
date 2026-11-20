using KeepShell.Services.Platform;
using KeepShell.Testing;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Schedule;

namespace SpaceSnoop.Wpf.Tests;

[NonParallelizable]
public class ProfileBatchTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_batch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Перекрывающаяся_пара_остаётся_в_списке_с_причиной_и_не_выбирается()
    {
        var source = Make("src", "A");

        var row = ProfileBatch.BuildPairs(source, source, []).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Refusal, Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(row.IsSelectable, Is.False);
            Assert.That(row.Include, Is.False);
        }
    }

    [Test]
    public void Подпапки_источника_дают_пары_с_зеркальным_приёмником()
    {
        var source = Make("src", "A", "B");
        var dest = Path.Combine(_root, "dst");

        var rows = ProfileBatch.BuildPairs(source, dest, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                rows.Select(row => row.Name),
                Is.EquivalentTo(
                    new[]
                    {
                        "A", "B",
                    }));

            Assert.That(rows.Select(static row => row.Right), Is.EquivalentTo(new[] { Path.Combine(dest, "A"), Path.Combine(dest, "B") }));
            Assert.That(rows.All(row => row.Include), Is.True);
        }
    }

    [TestCase(0, "A,B,C")]
    [TestCase(1, "C,B,A")]
    [TestCase(2, "B,C,A")]
    public void Сортировка_пар_по_режиму(int mode, string expected)
    {
        var rows = new List<BatchPairRow>
        {
            new("B", "l", "r", false),
            new("A", "l", "r", true),
            new("C", "l", "r", false),
        };

        var sorted = ProfileBatch.Sort(rows, mode);

        Assert.That(string.Join(",", sorted.Select(row => row.Name)), Is.EqualTo(expected));
    }

    [Test]
    public void Снятие_выбора_сегмента_сортировки_игнорируется_и_восстанавливает_значение()
    {
        var dialog = new BatchCreateProfilesDialogViewModel(new MemorySettings(), new FakeFilePicker(), new FakeUiDispatcher());
        var changed = new List<string>();
        dialog.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        dialog.SelectedSort = 1;
        changed.Clear();
        dialog.SelectedSort = -1;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.SelectedSort, Is.EqualTo(1));
            Assert.That(changed, Does.Contain(nameof(BatchCreateProfilesDialogViewModel.SelectedSort)));
        }
    }

    [Test]
    public void Существующий_профиль_помечает_пару_как_дубль_и_снимает_галочку()
    {
        var source = Make("src", "A");
        var dest = Path.Combine(_root, "dst");
        var existing = new SyncProfile
        {
            Left = Path.Combine(source, "A"),
            Right = Path.Combine(dest, "A"),
        };

        var row = ProfileBatch.BuildPairs(source, dest, [existing]).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.AlreadyExists, Is.True);
            Assert.That(row.Include, Is.False);
        }
    }

    [Test]
    public async Task Создание_пачки_строит_пары_вне_потока_интерфейса_и_отбрасывает_устаревший_результат()
    {
        var previous = BatchCreateProfilesDialogViewModel.PairBuilder;
        var callerThread = Environment.CurrentManagedThreadId;
        var builderThread = 0;

        using var firstEntered = new ManualResetEventSlim();
        using var firstRelease = new ManualResetEventSlim();
        CancellationToken firstToken = default;

        BatchCreateProfilesDialogViewModel.PairBuilder = (source, dest, _, token) =>
        {
            if (source.Contains("first", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref builderThread, Environment.CurrentManagedThreadId);
                firstToken = token;
                firstEntered.Set();
                firstRelease.Wait(TimeSpan.FromSeconds(10));
                return [new("Stale", Path.Combine(source, "Stale"), Path.Combine(dest, "Stale"), false)];
            }

            return [new("Fresh", Path.Combine(source, "Fresh"), Path.Combine(dest, "Fresh"), false)];
        };

        try
        {
            var dialog = new BatchCreateProfilesDialogViewModel(new MemorySettings(), new FakeFilePicker(), new FakeUiDispatcher())
            {
                DestParent = @"D:\Dest",
                SourceParent = @"C:\first",
            };

            var firstRebuild = dialog.PendingRebuild;
            Assert.That(firstEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Volatile.Read(ref builderThread), Is.Not.EqualTo(callerThread));
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.False);
            }

            dialog.SourceParent = @"C:\second";
            var secondRebuild = dialog.PendingRebuild;
            await secondRebuild.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(firstToken.IsCancellationRequested, Is.True);

            firstRelease.Set();
            await firstRebuild.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.Rows.Select(row => row.Name), Is.EqualTo(new[] { "Fresh" }));
                Assert.That(dialog.HasRows, Is.True);
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.True);
            }
        }
        finally
        {
            firstRelease.Set();
            BatchCreateProfilesDialogViewModel.PairBuilder = previous;
        }
    }

    [Test]
    public async Task Отмена_диалога_пачки_отменяет_фоновую_сборку_и_не_заполняет_список()
    {
        var previous = BatchCreateProfilesDialogViewModel.PairBuilder;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationToken observedToken = default;

        BatchCreateProfilesDialogViewModel.PairBuilder = (source, dest, _, token) =>
        {
            observedToken = token;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return [new("Late", Path.Combine(source, "Late"), Path.Combine(dest, "Late"), false)];
        };

        try
        {
            var dialog = new BatchCreateProfilesDialogViewModel(new MemorySettings(), new FakeFilePicker(), new FakeUiDispatcher())
            {
                DestParent = @"D:\Dest",
                SourceParent = @"\\hung-server\share",
            };

            var pending = dialog.PendingRebuild;
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            dialog.CancelCommand.Execute(null);
            Assert.That(observedToken.IsCancellationRequested, Is.True);

            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(dialog.Rows, Is.Empty);
        }
        finally
        {
            release.Set();
            BatchCreateProfilesDialogViewModel.PairBuilder = previous;
        }
    }

    [Test]
    public async Task Быстрая_смена_каталога_не_очищает_прежние_строки_до_готовности_новых()
    {
        var previous = BatchCreateProfilesDialogViewModel.PairBuilder;
        using var secondEntered = new ManualResetEventSlim();
        using var secondRelease = new ManualResetEventSlim();

        BatchCreateProfilesDialogViewModel.PairBuilder = (source, dest, _, _) =>
        {
            if (dest.Contains("second", StringComparison.Ordinal))
            {
                secondEntered.Set();
                secondRelease.Wait(TimeSpan.FromSeconds(10));
                return [new("Second", Path.Combine(source, "Second"), Path.Combine(dest, "Second"), false)];
            }

            return [new("First", Path.Combine(source, "First"), Path.Combine(dest, "First"), false)];
        };

        try
        {
            var dialog = new BatchCreateProfilesDialogViewModel(new MemorySettings(), new FakeFilePicker(), new FakeUiDispatcher())
            {
                SourceParent = @"C:\Source",
                DestParent = @"D:\first",
            };

            await dialog.PendingRebuild.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(dialog.Rows.Select(row => row.Name), Is.EqualTo(new[] { "First" }));

            dialog.DestParent = @"D:\second";
            var pending = dialog.PendingRebuild;
            Assert.That(secondEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.Rows.Select(row => row.Name), Is.EqualTo(new[] { "First" }));
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.False);
            }

            secondRelease.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.Rows.Select(row => row.Name), Is.EqualTo(new[] { "Second" }));
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.True);
            }
        }
        finally
        {
            secondRelease.Set();
            BatchCreateProfilesDialogViewModel.PairBuilder = previous;
        }
    }

    [Test]
    public async Task Ошибка_сборки_пар_не_замораживает_диалог_и_применяет_результат_через_диспетчер()
    {
        var previous = BatchCreateProfilesDialogViewModel.PairBuilder;
        var dispatcher = new TrackingUiDispatcher();

        BatchCreateProfilesDialogViewModel.PairBuilder = (source, dest, _, _) =>
        {
            if (source.Contains("broken", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Сбой чтения каталога");
            }

            return [new("Ok", Path.Combine(source, "Ok"), Path.Combine(dest, "Ok"), false)];
        };

        try
        {
            using var dialog = new BatchCreateProfilesDialogViewModel(new MemorySettings(), new FakeFilePicker(), dispatcher)
            {
                DestParent = @"D:\Dest",
                SourceParent = @"C:\first",
            };

            await dialog.PendingRebuild.WaitAsync(TimeSpan.FromSeconds(5));
            var invokesBeforeError = dispatcher.InvokeCount;
            Assert.That(invokesBeforeError, Is.GreaterThanOrEqualTo(1));
            Assert.That(dialog.CreateCommand.CanExecute(null), Is.True);

            dialog.SourceParent = @"C:\broken";
            await dialog.PendingRebuild.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dispatcher.InvokeCount, Is.GreaterThan(invokesBeforeError));
                Assert.That(dialog.Rows, Is.Empty);
                Assert.That(dialog.HasRows, Is.False);
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.False);
            }

            dialog.SourceParent = @"C:\recovered";
            await dialog.PendingRebuild.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.Rows.Select(row => row.Name), Is.EqualTo(new[] { "Ok" }));
                Assert.That(dialog.CreateCommand.CanExecute(null), Is.True);
            }
        }
        finally
        {
            BatchCreateProfilesDialogViewModel.PairBuilder = previous;
        }
    }

    private string Make(string name, params string[] subdirectories)
    {
        var parent = Path.Combine(_root, name);
        Directory.CreateDirectory(parent);

        foreach (var sub in subdirectories)
        {
            Directory.CreateDirectory(Path.Combine(parent, sub));
        }

        return parent;
    }

    private sealed class TrackingUiDispatcher : IUiDispatcher
    {
        private int _invokeCount;

        public int InvokeCount => Volatile.Read(ref _invokeCount);

        public bool HasAccess => true;

        public void Invoke(Action action)
        {
            Interlocked.Increment(ref _invokeCount);
            action();
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
            return new FakeUiTimer(interval, tick);
        }
    }
}
