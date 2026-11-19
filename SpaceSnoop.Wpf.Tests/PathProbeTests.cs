using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class PathProbeTests
{
    [TestCase("", PathProbeStatus.Empty)]
    [TestCase("   ", PathProbeStatus.Empty)]
    [TestCase(@"\\", PathProbeStatus.IncompleteUnc)]
    [TestCase(@"\\server", PathProbeStatus.IncompleteUnc)]
    [TestCase(@"\\server\", PathProbeStatus.IncompleteUnc)]
    [TestCase("//server/", PathProbeStatus.IncompleteUnc)]
    public async Task Пустой_и_неполный_UNC_возвращаются_мгновенно_без_вызова_файловой_системы(string path, PathProbeStatus expected)
    {
        var calls = 0;
        var previous = PathProbe.DirectoryExists;
        PathProbe.DirectoryExists = _ =>
        {
            Interlocked.Increment(ref calls);
            return true;
        };

        try
        {
            var result = await PathProbe.CheckAsync(path);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Status, Is.EqualTo(expected));
                Assert.That(Volatile.Read(ref calls), Is.Zero);
            }
        }
        finally
        {
            PathProbe.DirectoryExists = previous;
        }
    }

    [Test]
    public async Task Зависший_корень_ограничивает_очередь_одним_активным_вызовом_и_завершается_по_таймауту()
    {
        var active = 0;
        var maxActive = 0;
        var calls = 0;
        using var release = new ManualResetEventSlim();

        bool HungProbe(string _)
        {
            Interlocked.Increment(ref calls);
            var current = Interlocked.Increment(ref active);

            while (true)
            {
                var seenMax = Volatile.Read(ref maxActive);

                if (current <= seenMax || Interlocked.CompareExchange(ref maxActive, current, seenMax) == seenMax)
                {
                    break;
                }
            }

            try
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return false;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        try
        {
            var tasks = Enumerable.Range(0, 20)
                .Select(i => PathProbe.CheckAsync($@"\\hung-nas\share\dir{i}", HungProbe, timeoutMs: 80))
                .ToArray();

            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Volatile.Read(ref maxActive), Is.EqualTo(1));
                Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
                Assert.That(results.All(static r => r.Status == PathProbeStatus.TimedOut), Is.True);
            }
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public async Task Отмена_токеном_мгновенно_возвращает_Cancelled()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource();

        bool HungProbe(string _)
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return true;
        }

        try
        {
            var task = PathProbe.CheckAsync(@"\\slow-nas\share\folder", HungProbe, timeoutMs: 5000, cts.Token);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            await cts.CancelAsync();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.That(result.Status, Is.EqualTo(PathProbeStatus.Cancelled));
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public async Task Конкурирующие_запросы_на_один_корень_выполняются_последовательно_без_вытеснения_и_пропускают_отменённые()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var firstRelease = new ManualResetEventSlim();
        using var cts2 = new CancellationTokenSource();
        var visited = new List<string>();
        var root = "m2-nas-" + Guid.NewGuid().ToString("N")[..6];

        bool SequencedProbe(string path)
        {
            lock (visited)
            {
                visited.Add(path);
            }

            if (path.Contains("step1", StringComparison.Ordinal))
            {
                firstEntered.Set();
                firstRelease.Wait(TimeSpan.FromSeconds(10));
                return true;
            }

            return !path.Contains("missing", StringComparison.Ordinal);
        }

        try
        {
            var task1 = PathProbe.CheckAsync($@"\\{root}\share\step1", SequencedProbe, timeoutMs: 5000);
            Assert.That(firstEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var task2 = PathProbe.CheckAsync($@"\\{root}\share\step2", SequencedProbe, timeoutMs: 5000, cts2.Token);
            var task3 = PathProbe.CheckAsync($@"\\{root}\share\step3-missing", SequencedProbe, timeoutMs: 5000);
            await cts2.CancelAsync();

            var cancelledResult = await task2.WaitAsync(TimeSpan.FromSeconds(2));
            var task4 = PathProbe.CheckAsync($@"\\{root}\share\step4", SequencedProbe, timeoutMs: 5000);

            firstRelease.Set();

            var result1 = await task1.WaitAsync(TimeSpan.FromSeconds(5));
            var result3 = await task3.WaitAsync(TimeSpan.FromSeconds(5));
            var result4 = await task4.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cancelledResult.Status, Is.EqualTo(PathProbeStatus.Cancelled));
                Assert.That(result1.Status, Is.EqualTo(PathProbeStatus.Exists));
                Assert.That(result3.Status, Is.EqualTo(PathProbeStatus.Missing));
                Assert.That(result4.Status, Is.EqualTo(PathProbeStatus.Exists));
                Assert.That(
                    visited,
                    Is.EqualTo(
                        new[]
                        {
                            $@"\\{root}\share\step1",
                            $@"\\{root}\share\step3-missing",
                            $@"\\{root}\share\step4",
                        }));
            }
        }
        finally
        {
            firstRelease.Set();
        }
    }

    [Test]
    public async Task Конкурирующие_потребители_на_одном_зависшем_корне_завершаются_по_таймауту_без_зацикливания()
    {
        var previousProbe = PathProbe.DirectoryExists;
        var previousScanProbe = ScanTargetProbe.DirectoryExists;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var root = "hung-nas-" + Guid.NewGuid().ToString("N")[..6];

        bool HungProbe(string _)
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return false;
        }

        PathProbe.DirectoryExists = HungProbe;
        ScanTargetProbe.DirectoryExists = HungProbe;

        try
        {
            var profile = new SyncProfile
            {
                Id = "p",
                Name = "P",
                Left = $@"\\{root}\share\left",
                Right = $@"\\{root}\share\right",
            };

            var validationTask = SyncProfileValidation.RefuseSchedulingAsync(
                () => ($@"\\{root}\share\val-left", $@"\\{root}\share\val-right", false),
                timeoutMs: 80,
                CancellationToken.None);
            var overviewTask = OverviewPipeline.ClassifyAsync(profile, timeoutMs: 80, CancellationToken.None);
            var scanTask = ScanTargetProbe.CheckAsync($@"\\{root}\share\scan", timeoutMs: 80, CancellationToken.None);

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            await Task.WhenAll(validationTask, overviewTask, scanTask).WaitAsync(TimeSpan.FromSeconds(5));

            var refusal = await validationTask;
            var overviewResult = await overviewTask;
            var scanResult = await scanTask;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
                Assert.That(refusal, Does.Contain("время ожидания"));
                Assert.That(overviewResult?.Status, Is.EqualTo(OverviewRunStatus.Unavailable));
                Assert.That(overviewResult?.Reason, Does.Contain("время ожидания"));
                Assert.That(scanResult.Status, Is.EqualTo(PathProbeStatus.TimedOut));
            }
        }
        finally
        {
            release.Set();
            PathProbe.DirectoryExists = previousProbe;
            ScanTargetProbe.DirectoryExists = previousScanProbe;
        }
    }
}
