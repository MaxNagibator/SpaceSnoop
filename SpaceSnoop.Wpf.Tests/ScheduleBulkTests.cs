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
        vm.BulkModeIndex = 1;

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

        vm.SelectAllProfilesCommand.Execute(null);
        vm.BulkWinnerIndex = 2;

        Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Winner),
            Is.All.EqualTo(SyncWinner.Right));
    }

    [Test]
    public void Зеркало_пропускает_двусторонние_профили_с_победителем_по_дате()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.SelectAllProfilesCommand.Execute(null);
        vm.Profiles[1].SelectedModeIndex = 2;
        vm.BulkMirrorOnCommand.Execute(null);

        var stored = SyncProfileStore.Load(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored[0].Mirror, Is.True);
            Assert.That(stored[1].Mirror, Is.False);
            Assert.That(stored[2].Mirror, Is.True);
            Assert.That(vm.BulkMessage, Does.Contain("пропущено 1"));
        }
    }

    [Test]
    public void Расширение_выделения_сбрасывает_панель_и_позволяет_применить_тот_же_режим_повторно()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[0].IsSelected = true;
        vm.BulkModeIndex = 1;
        vm.Profiles[1].IsSelected = true;

        Assert.That(vm.BulkModeIndex, Is.EqualTo(-1));

        vm.BulkModeIndex = 1;

        Assert.That(SyncProfileStore.Load(settings)[1].Mode, Is.EqualTo(1));
    }

    [Test]
    public void Зеркало_без_подходящих_профилей_объясняет_причину()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[1].IsSelected = true;
        vm.Profiles[1].SelectedModeIndex = 2;
        vm.BulkMirrorOnCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.BulkMessage, Does.StartWith("Зеркало неприменимо"));
            Assert.That(SyncProfileStore.Load(settings)[1].Mirror, Is.False);
        }
    }

    [Test]
    public void Пустые_исключения_очищают_поле_у_выбранных()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.SelectAllProfilesCommand.Execute(null);
        vm.BulkExclusions = "   ";
        vm.BulkApplyExclusionsCommand.Execute(null);

        Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Exclusions), Is.All.Empty);
    }

    [TestCase("25:00")]
    [TestCase("не время")]
    public void Некорректное_время_не_меняет_расписание(string time)
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.SelectAllProfilesCommand.Execute(null);
        vm.BulkIntervalIndex = 0;
        vm.BulkTime = time;
        vm.BulkApplyScheduleCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncProfileStore.Load(settings).Select(profile => profile.Time), Is.All.EqualTo("03:00"));
            Assert.That(vm.BulkMessage, Does.Contain("ЧЧ:ММ"));
        }
    }

    [Test]
    public void Расписание_применяется_ко_всем_выбранным()
    {
        var settings = Seed();
        var vm = Create(settings);

        vm.Profiles[1].IsSelected = true;
        vm.BulkIntervalIndex = 1;
        vm.BulkTime = "07:30";
        vm.BulkApplyScheduleCommand.Execute(null);

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

        vm.SelectAllProfilesCommand.Execute(null);
        vm.BulkModeIndex = 2;
        vm.ClearSelectionCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.HasSelection, Is.False);
            Assert.That(vm.BulkModeIndex, Is.EqualTo(-1));
            Assert.That(vm.BulkMessage, Is.Empty);
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

    private static ScheduleViewModel Create(ISettingsStore settings)
    {
        return new(settings, new NoopDialogs(), NullLogger<ScheduleViewModel>.Instance);
    }
}
