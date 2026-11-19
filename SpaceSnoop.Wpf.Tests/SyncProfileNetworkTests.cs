using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Schedule;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class SyncProfileNetworkTests
{
    [Test]
    public void Включение_зеркала_на_сетевом_приёмнике_показывает_предупреждение_о_необратимости()
    {
        var schedule = CreateSchedule();
        var profile = new SyncProfileViewModel(
            schedule,
            new SyncProfile
            {
                Id = "p1",
                Name = "NAS",
                Left = @"C:\Local",
                Right = @"\\nas\backup",
                Mode = 0,
                Mirror = true,
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.MirrorWarning, Is.True);
            Assert.That(profile.MirrorTargetWithoutRecycleBin, Is.True);
            Assert.That(profile.AllowPermanentNetworkDelete, Is.False);
            Assert.That(profile.MirrorWarningText, Does.Contain("нет корзины"));
            Assert.That(profile.MirrorWarningText, Does.Contain("разрешите безвозвратное удаление"));
        }

        profile.SelectedModeIndex = 1;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.MirrorTargetWithoutRecycleBin, Is.False);
            Assert.That(profile.MirrorWarningText, Does.Contain("в корзину"));
        }

        profile.SelectedModeIndex = 2;
        profile.SelectedWinnerIndex = 1;
        Assert.That(profile.MirrorTargetWithoutRecycleBin, Is.True);

        profile.SelectedWinnerIndex = 2;
        Assert.That(profile.MirrorTargetWithoutRecycleBin, Is.False);
    }

    [Test]
    public async Task Согласие_на_сетевое_удаление_сохраняется_только_после_записи_и_для_этого_приёмника()
    {
        var schedule = CreateSchedule();
        var profile = new SyncProfileViewModel(
            schedule,
            new SyncProfile
            {
                Id = "p1",
                Left = @"C:\Local",
                Right = @"\\nas\backup",
                Mirror = true,
            });
        schedule.Profiles.Add(profile);

        profile.EditCommand.Execute(null);
        profile.AllowPermanentNetworkDelete = true;
        Assert.That(profile.ToModel().PermanentNetworkDeleteTarget, Is.Null);

        await profile.SaveCommand.ExecuteAsync(null);
        var saved = SyncProfileStore.Find(schedule.Settings, profile.Id);
        Assert.That(saved?.PermanentNetworkDeleteTarget, Is.EqualTo(@"\\nas\backup"));

        profile.EditCommand.Execute(null);
        profile.RightPath = @"\\nas\another-backup";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.AllowPermanentNetworkDelete, Is.False);
            Assert.That(profile.ToModel().PermanentNetworkDeleteTarget, Is.Null);
        }
    }

    [Test]
    public async Task Неполный_и_зависший_UNC_отклоняются_при_включении_расписания_без_зависания()
    {
        var previousProbe = PathProbe.DirectoryExists;
        var probeCalls = 0;
        using var release = new ManualResetEventSlim();

        PathProbe.DirectoryExists = path =>
        {
            Interlocked.Increment(ref probeCalls);

            if (path.Contains("hung", StringComparison.Ordinal))
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return false;
            }

            return true;
        };

        try
        {
            var schedule = CreateSchedule();
            var incomplete = new SyncProfileViewModel(
                schedule,
                new SyncProfile
                {
                    Id = "inc",
                    Name = "Неполный",
                    Left = @"\\nas\",
                    Right = @"C:\Target",
                });

            var okIncomplete = await incomplete.ValidateEnableAsync(true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(okIncomplete, Is.False);
                Assert.That(Volatile.Read(ref probeCalls), Is.Zero);
                Assert.That(incomplete.Message, Does.Contain("полный сетевой путь"));
            }

            var hung = new SyncProfileViewModel(
                schedule,
                new SyncProfile
                {
                    Id = "hung",
                    Name = "Зависший",
                    Left = @"\\hung-nas\share",
                    Right = @"C:\Target",
                })
            {
                ProbeTimeoutMs = 80,
            };

            var okHung = await hung.ValidateEnableAsync(true).WaitAsync(TimeSpan.FromSeconds(3));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(okHung, Is.False);
                Assert.That(hung.Message, Does.Contain("время ожидания"));
            }
        }
        finally
        {
            release.Set();
            PathProbe.DirectoryExists = previousProbe;
        }
    }

    [Test]
    public async Task Обзор_отсекает_неполный_UNC_и_прерывается_по_таймауту_на_зависшем_NAS()
    {
        var previousProbe = PathProbe.DirectoryExists;
        var probeCalls = 0;
        using var release = new ManualResetEventSlim();

        PathProbe.DirectoryExists = path =>
        {
            Interlocked.Increment(ref probeCalls);

            if (path.Contains("hung", StringComparison.Ordinal))
            {
                release.Wait(TimeSpan.FromSeconds(10));
                return false;
            }

            return true;
        };

        try
        {
            var incompleteProfile = new SyncProfile
            {
                Id = "inc",
                Name = "Неполный",
                Left = @"\\nas",
                Right = @"C:\Target",
            };

            var incompleteResult = await OverviewPipeline.ClassifyAsync(incompleteProfile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(incompleteResult?.Status, Is.EqualTo(OverviewRunStatus.Unavailable));
                Assert.That(Volatile.Read(ref probeCalls), Is.Zero);
            }

            var hungProfile = new SyncProfile
            {
                Id = "hung",
                Name = "Зависший",
                Left = @"\\hung-nas\share",
                Right = @"C:\Target",
            };

            var hungResult = await OverviewPipeline.ClassifyAsync(hungProfile, timeoutMs: 80, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(hungResult?.Status, Is.EqualTo(OverviewRunStatus.Unavailable));
                Assert.That(hungResult?.Reason, Does.Contain("время ожидания"));
            }
        }
        finally
        {
            release.Set();
            PathProbe.DirectoryExists = previousProbe;
        }
    }

    private static ScheduleViewModel CreateSchedule()
    {
        var settings = new MemorySettings();
        return new(
            settings,
            new NoopDialogs(),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeScheduleRunner(),
            new FakeUiDispatcher(),
            NullLogger<ScheduleViewModel>.Instance);
    }
}
