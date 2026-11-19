using Microsoft.Extensions.Logging;
using SpaceSnoop.Core;
using System.Collections.Concurrent;

namespace SpaceSnoop.Tests;

[TestFixture]
public class NetworkDrivesTests
{
    private const int BadNetPath = 53;

    [Test]
    public async Task Без_повышения_ни_реестр_ни_сеть_не_трогаются()
    {
        var touched = false;

        var results = await NetworkDrives.RestoreAsync(
            false,
            () =>
            {
                touched = true;
                return [Drive("Z:")];
            },
            () =>
            {
                touched = true;
                return [];
            },
            _ =>
            {
                touched = true;
                return 0;
            },
            new CapturingLogger());

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.Empty);
            Assert.That(touched, Is.False);
        });
    }

    [Test]
    public void Буква_занятая_в_сеансе_не_выбирается_без_учёта_регистра()
    {
        PersistentDrive[] persistent = [Drive("Z:"), Drive("Y:"), Drive("X:")];

        var missing = NetworkDrives.SelectMissing(persistent, [@"C:\", @"z:\", @"X:\"]);

        Assert.That(
            missing.Select(drive => drive.LocalName),
            Is.EqualTo(
                new[]
                {
                    "Y:",
                }));
    }

    [Test]
    public async Task Быстрая_буква_отдаётся_наблюдателю_до_завершения_медленной()
    {
        using var restore = new NetworkDriveRestore();
        using var slowEntered = new ManualResetEventSlim();
        using var releaseSlow = new ManualResetEventSlim();

        restore.Start(
            true,
            () => [Drive("Z:", @"\\slow\share"), Drive("Y:", @"\\fast\share")],
            () => [@"C:\"],
            drive =>
            {
                if (drive.LocalName == "Z:")
                {
                    slowEntered.Set();
                    releaseSlow.Wait(TimeSpan.FromSeconds(10));
                }

                return 0;
            },
            new CapturingLogger());

        await using var enumerator = restore.WatchAsync().GetAsyncEnumerator();

        var firstReady = await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var first = enumerator.Current;
        var secondTask = enumerator.MoveNextAsync().AsTask();

        Assert.That(slowEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstReady, Is.True);
            Assert.That(first.Drive.LocalName, Is.EqualTo("Y:"));
            Assert.That(first.Succeeded, Is.True);
            Assert.That(secondTask.IsCompleted, Is.False);
        }

        releaseSlow.Set();
        var secondReady = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondReady, Is.True);
            Assert.That(enumerator.Current.Drive.LocalName, Is.EqualTo("Z:"));
            Assert.That(await enumerator.MoveNextAsync(), Is.False);
        }
    }

    [TestCase("ZZ", @"\\srv\share")]
    [TestCase("1", @"\\srv\share")]
    [TestCase("", @"\\srv\share")]
    [TestCase("Z", "")]
    [TestCase("Z", null)]
    public void Ключ_не_буквы_или_без_пути_пропускается(string keyName, string? remotePath)
    {
        var drive = NetworkDrives.Parse(keyName, name => name == "RemotePath" ? remotePath : null);

        Assert.That(drive, Is.Null);
    }

    [Test]
    public async Task Ожидание_пути_ждёт_только_свою_букву_и_уважает_таймаут()
    {
        using var restore = new NetworkDriveRestore();
        using var targetEntered = new ManualResetEventSlim();
        using var releaseTarget = new ManualResetEventSlim();
        using var hungRelease = new ManualResetEventSlim();

        restore.Start(
            true,
            () => [Drive("Z:", @"\\target\share"), Drive("X:", @"\\hung\share")],
            () => [@"C:\"],
            drive =>
            {
                if (drive.LocalName == "Z:")
                {
                    targetEntered.Set();
                    releaseTarget.Wait(TimeSpan.FromSeconds(10));
                    return 0;
                }

                hungRelease.Wait(TimeSpan.FromSeconds(10));
                return BadNetPath;
            },
            new CapturingLogger());

        Assert.That(targetEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        await restore.WaitForPathAsync(@"C:\Windows", TimeSpan.FromSeconds(5));

        var waiting = restore.WaitForPathAsync(@"z:\projects\demo", TimeSpan.FromSeconds(5));
        Assert.That(waiting.IsCompleted, Is.False);

        releaseTarget.Set();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));

        await restore.WaitForPathAsync(@"X:\backup", TimeSpan.FromMilliseconds(40)).WaitAsync(TimeSpan.FromSeconds(2));
        hungRelease.Set();
    }

    [Test]
    public async Task Отказ_одной_буквы_пишется_строкой_и_не_мешает_остальным()
    {
        var logger = new CapturingLogger();
        var connected = new ConcurrentBag<string>();

        var results = await NetworkDrives.RestoreAsync(
            true,
            () => [Drive("Z:", @"\\down\share"), Drive("Y:"), Drive("C:"), Drive("X:")],
            () => [@"C:\"],
            drive =>
            {
                connected.Add(drive.LocalName);
                return drive.LocalName == "Z:" ? BadNetPath : 0;
            },
            logger);

        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);

        Assert.Multiple(() =>
        {
            Assert.That(
                connected,
                Is.EquivalentTo(
                    new[]
                    {
                        "Z:", "Y:", "X:",
                    }));

            Assert.That(
                results.Where(result => result.Succeeded).Select(result => result.Drive.LocalName),
                Is.EquivalentTo(
                    new[]
                    {
                        "Y:", "X:",
                    }));

            Assert.That(failure.Message, Does.Contain("Z:").And.Contain(@"\\down\share").And.Contain(BadNetPath.ToString()));
            Assert.That(logger.Entries.Count(entry => entry.Level == LogLevel.Information), Is.EqualTo(2));
        });
    }

    [TestCase("z", @"\\srv\share", "DOMAIN\\user", "Microsoft Windows Network", "Z:", "DOMAIN\\user", "Microsoft Windows Network")]
    [TestCase("Y", @"\\srv\share", "", null, "Y:", null, null)]
    [TestCase("Y", @"\\srv\share", null, "  ", "Y:", null, null)]
    public void Подключение_читается_из_ключа_буквы(
        string keyName,
        string remotePath,
        string? userName,
        string? providerName,
        string expectedLocalName,
        string? expectedUserName,
        string? expectedProviderName)
    {
        var values = new Dictionary<string, string?>
        {
            ["RemotePath"] = remotePath,
            ["UserName"] = userName,
            ["ProviderName"] = providerName,
        };

        var drive = NetworkDrives.Parse(keyName, name => values.GetValueOrDefault(name));

        Assert.That(drive, Is.EqualTo(new PersistentDrive(expectedLocalName, remotePath, expectedUserName, expectedProviderName)));
    }

    private static PersistentDrive Drive(string localName, string remotePath = @"\\srv\share")
    {
        return new(localName, remotePath, null, null);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue(new(logLevel, formatter(state, exception)));
        }
    }
}
