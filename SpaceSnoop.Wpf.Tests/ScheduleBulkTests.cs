using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Schedule;

namespace SpaceSnoop.Wpf.Tests;

public class ScheduleBulkTests
{
    [Test]
    public void Пакетная_смена_направления_трогает_только_выбранные_профили()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[0].IsSelected = true;
        vm.Profiles[2].IsSelected = true;
        vm.Bulk.ModeIndex = 1;

        var stored = SyncProfileStore.Load(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored[0].Mode, Is.EqualTo(1));
            Assert.That(stored[1].Mode, Is.Zero);
            Assert.That(stored[2].Mode, Is.EqualTo(1));
        }
    }

    [Test]
    public void Пакетная_смена_победителя_сохраняется_в_профилях()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Bulk.SelectAllCommand.Execute(null);
        vm.Bulk.WinnerIndex = 2;

        Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Winner),
            Is.All.EqualTo(SyncWinner.Right));
    }

    [Test]
    public void Зеркало_пропускает_двусторонние_профили_с_победителем_по_дате()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Bulk.SelectAllCommand.Execute(null);
        vm.Profiles[1].SelectedModeIndex = 2;
        vm.Bulk.MirrorOnCommand.Execute(null);

        var stored = SyncProfileStore.Load(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored[0].Mirror, Is.True);
            Assert.That(stored[1].Mirror, Is.False);
            Assert.That(stored[2].Mirror, Is.True);
            Assert.That(vm.Bulk.Message, Does.Contain("пропущено 1"));
        }
    }

    [Test]
    public void Расширение_выделения_сбрасывает_панель_и_позволяет_применить_тот_же_режим_повторно()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[0].IsSelected = true;
        vm.Bulk.ModeIndex = 1;
        vm.Profiles[1].IsSelected = true;

        Assert.That(vm.Bulk.ModeIndex, Is.EqualTo(-1));

        vm.Bulk.ModeIndex = 1;

        Assert.That(SyncProfileStore.Load(settings)[1].Mode, Is.EqualTo(1));
    }

    [Test]
    public void Зеркало_без_подходящих_профилей_объясняет_причину()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[1].IsSelected = true;
        vm.Profiles[1].SelectedModeIndex = 2;
        vm.Bulk.MirrorOnCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Bulk.Message, Does.StartWith("Зеркало неприменимо"));
            Assert.That(SyncProfileStore.Load(settings)[1].Mirror, Is.False);
        }
    }

    [Test]
    public void Пустые_исключения_очищают_поле_у_выбранных()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Bulk.SelectAllCommand.Execute(null);
        vm.Bulk.Exclusions = "   ";
        vm.Bulk.ApplyExclusionsCommand.Execute(null);

        Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Exclusions), Is.All.Empty);
    }

    [TestCase("25:00")]
    [TestCase("не время")]
    public async Task Некорректное_время_не_меняет_расписание(string time)
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Bulk.SelectAllCommand.Execute(null);
        vm.Bulk.IntervalIndex = 0;
        vm.Bulk.Time = time;
        await vm.Bulk.ApplyScheduleCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Time), Is.All.EqualTo("03:00"));
            Assert.That(vm.Bulk.Message, Does.Contain("ЧЧ:ММ"));
        }
    }

    [Test]
    public async Task Расписание_применяется_ко_всем_выбранным()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[1].IsSelected = true;
        vm.Bulk.IntervalIndex = 1;
        vm.Bulk.Time = "07:30";
        await vm.Bulk.ApplyScheduleCommand.ExecuteAsync(null);

        var stored = SyncProfileStore.Load(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored[1].Interval, Is.EqualTo(ScheduleInterval.Hourly));
            Assert.That(stored[1].Time, Is.EqualTo("07:30"));
            Assert.That(stored[0].Interval, Is.EqualTo(ScheduleInterval.Daily));
            Assert.That(stored[0].Time, Is.EqualTo("03:00"));
        }
    }

    [Test]
    public void Снятие_выделения_сбрасывает_панель()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Bulk.SelectAllCommand.Execute(null);
        vm.Bulk.ModeIndex = 2;
        vm.Bulk.ClearSelectionCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Bulk.HasSelection, Is.False);
            Assert.That(vm.Bulk.ModeIndex, Is.EqualTo(-1));
            Assert.That(vm.Bulk.Message, Is.Empty);
        }
    }

    [Test]
    public async Task Пакетное_включение_переписывает_задачи_и_снимает_признак_работы()
    {
        using var dirs = new TempProfileDirectories();
        var settings = dirs.Seed();
        var scheduler = new FakeScheduleRunner();
        var vm = Create(settings, scheduler);
        var seenRunning = false;

        scheduler.OnCall = _ => seenRunning |= vm.Bulk.IsRunning;

        vm.Bulk.SelectAllCommand.Execute(null);
        await vm.Bulk.EnableCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Applied, Has.Count.EqualTo(3));
            Assert.That(scheduler.Applied, Is.All.Matches<ScheduleRequest>(request => request.Enabled));
            Assert.That(seenRunning, Is.True, "во время прогона признак «идёт» должен быть выставлен");
            Assert.That(vm.Bulk.IsRunning, Is.False);
            Assert.That(vm.Bulk.Message, Is.EqualTo("Включено: 3 из 3"));
        }
    }

    [Test]
    public async Task Отмена_останавливает_пакетное_включение_между_вызовами_планировщика()
    {
        using var dirs = new TempProfileDirectories();
        var settings = dirs.Seed();
        var scheduler = new FakeScheduleRunner();
        var vm = Create(settings, scheduler);

        scheduler.OnCall = call =>
        {
            if (call == 2)
            {
                vm.Bulk.CancelRunCommand.Execute(null);
            }
        };

        vm.Bulk.SelectAllCommand.Execute(null);
        await vm.Bulk.EnableCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Applied, Has.Count.EqualTo(2));
            Assert.That(vm.Bulk.IsRunning, Is.False);
            Assert.That(vm.Bulk.Message, Does.Contain("Включено: 2 из 3").And.Contains("дальше отменено"));
        }
    }

    [Test]
    public async Task Пакетное_удаление_снимает_задачи_через_планировщик_и_убирает_профили()
    {
        var settings = Seed();
        var scheduler = new FakeScheduleRunner();
        var vm = Create(settings, scheduler, true);

        vm.Profiles[0].IsSelected = true;
        vm.Profiles[2].IsSelected = true;
        await vm.Bulk.DeleteCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Removed, Has.Count.EqualTo(2));
            Assert.That(vm.Profiles.Select(profile => profile.Id), Is.EqualTo(new[] { "two" }));
            Assert.That(SyncProfileStore.Load(settings), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Отмена_пакетного_удаления_оставляет_профили_чьи_задачи_ещё_не_сняты()
    {
        var settings = Seed();
        var scheduler = new FakeScheduleRunner();
        var vm = Create(settings, scheduler, true);

        scheduler.OnCall = call =>
        {
            if (call == 1)
            {
                vm.Bulk.CancelRunCommand.Execute(null);
            }
        };

        vm.Bulk.SelectAllCommand.Execute(null);
        await vm.Bulk.DeleteCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scheduler.Removed, Has.Count.EqualTo(1));
            Assert.That(vm.Profiles, Has.Count.EqualTo(2));
            Assert.That(vm.Bulk.Message, Does.Contain("Удалено профилей: 1 из 3"));
        }
    }

    private sealed class TempProfileDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSchedule_{Guid.NewGuid():N}");

        public MemorySettings Seed()
        {
            var settings = new MemorySettings();

            SyncProfileStore.Save(settings, Enumerable.Range(0, 3).Select(index => new SyncProfile
            {
                Id = $"p{index}",
                Name = $"Профиль {index}",
                Left = Create($"left{index}"),
                Right = Create($"right{index}"),
            }));

            return settings;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string Create(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);

            return path;
        }
    }

    private static MemorySettings Seed()
    {
        var settings = new MemorySettings();

        SyncProfileStore.Save(settings,
        [
            new() { Id = "one", Name = "Один", Left = @"C:\A", Right = @"C:\B", Exclusions = "bin" },
            new() { Id = "two", Name = "Два", Left = @"C:\C", Right = @"C:\D", Exclusions = "obj" },
            new() { Id = "three", Name = "Три", Left = @"C:\E", Right = @"C:\F", Exclusions = ".git" },
        ]);

        return settings;
    }

    private static ScheduleViewModel Create(ISettingsStore settings, IScheduleRunner? scheduler = null, bool confirmDialogs = false)
    {
        return new(
            settings,
            new NoopDialogs(confirmDialogs),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            scheduler ?? new FakeScheduleRunner(),
            NullLogger<ScheduleViewModel>.Instance);
    }
}
