using KeepShell.Services.Platform;
using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class SyncSetupPathProbeTests
{
    [Test]
    public async Task Смена_левого_пути_проверяет_каталог_вне_потока_интерфейса_и_отбрасывает_устаревший_ответ()
    {
        var previous = SyncSetupViewModel.DirectoryExists;
        var callerThread = Environment.CurrentManagedThreadId;
        var probeThread = 0;

        using var hungEntered = new ManualResetEventSlim();
        using var hungRelease = new ManualResetEventSlim();

        SyncSetupViewModel.DirectoryExists = path =>
        {
            if (path.Contains("hung", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref probeThread, Environment.CurrentManagedThreadId);
                hungEntered.Set();
                hungRelease.Wait(TimeSpan.FromSeconds(10));
                return false;
            }

            return true;
        };

        try
        {
            var dispatcher = new TrackingUiDispatcher();
            var setup = CreateSetup(dispatcher);
            setup.LeftPath = @"\\hung-server\share\old";
            var firstCheck = setup.PendingLeftCheck;

            Assert.That(hungEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref probeThread), Is.Not.EqualTo(callerThread));

            setup.LeftPath = @"C:\Existing";
            await setup.PendingLeftCheck.WaitAsync(TimeSpan.FromSeconds(5));
            var invokesAfterSecond = dispatcher.InvokeCount;

            hungRelease.Set();
            await firstCheck.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(setup.LeftPathInvalid, Is.False);
                Assert.That(invokesAfterSecond, Is.GreaterThanOrEqualTo(1));
                Assert.That(dispatcher.InvokeCount, Is.EqualTo(invokesAfterSecond));
            }
        }
        finally
        {
            hungRelease.Set();
            SyncSetupViewModel.DirectoryExists = previous;
        }
    }

    [Test]
    public async Task Пустой_путь_сбрасывает_ошибку_без_обращения_к_файловой_системе()
    {
        var previous = SyncSetupViewModel.DirectoryExists;
        var calls = 0;

        SyncSetupViewModel.DirectoryExists = _ =>
        {
            Interlocked.Increment(ref calls);
            return false;
        };

        try
        {
            var setup = CreateSetup();
            setup.LeftPath = @"C:\Missing";
            await setup.PendingLeftCheck.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(setup.LeftPathInvalid, Is.True);

            var beforeClear = Volatile.Read(ref calls);
            setup.LeftPath = "   ";

            using (Assert.EnterMultipleScope())
            {
                Assert.That(setup.LeftPathInvalid, Is.False);
                Assert.That(Volatile.Read(ref calls), Is.EqualTo(beforeClear));
            }
        }
        finally
        {
            SyncSetupViewModel.DirectoryExists = previous;
        }
    }

    [Test]
    public async Task Проверка_правого_пути_выполняется_асинхронно_и_отбрасывает_устаревший_ответ()
    {
        var previous = SyncSetupViewModel.DirectoryExists;
        var callerThread = Environment.CurrentManagedThreadId;
        var probeThread = 0;

        using var hungEntered = new ManualResetEventSlim();
        using var hungRelease = new ManualResetEventSlim();

        SyncSetupViewModel.DirectoryExists = path =>
        {
            if (path.Contains("hung", StringComparison.Ordinal))
            {
                Interlocked.Exchange(ref probeThread, Environment.CurrentManagedThreadId);
                hungEntered.Set();
                hungRelease.Wait(TimeSpan.FromSeconds(10));
                return true;
            }

            return false;
        };

        try
        {
            var setup = CreateSetup();
            setup.RightPath = @"\\hung-server\share\old";
            var firstCheck = setup.PendingRightCheck;

            Assert.That(hungEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref probeThread), Is.Not.EqualTo(callerThread));

            setup.RightPath = @"C:\Missing";
            await setup.PendingRightCheck.WaitAsync(TimeSpan.FromSeconds(5));

            hungRelease.Set();
            await firstCheck.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(setup.RightPathInvalid, Is.True);
        }
        finally
        {
            hungRelease.Set();
            SyncSetupViewModel.DirectoryExists = previous;
        }
    }

    private static SyncSetupViewModel CreateSetup(IUiDispatcher? dispatcher = null)
    {
        var settings = new MemorySettings();
        return new(
            settings,
            new NoopDialogs(),
            new OperationPreferences(settings),
            new FakeFilePicker(),
            static () => true,
            static _ => { },
            dispatcher ?? new FakeUiDispatcher());
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
