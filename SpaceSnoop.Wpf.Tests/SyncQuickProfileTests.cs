using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

public class SyncQuickProfileTests
{
    [Test]
    public void Новый_профиль_сохраняет_текущие_параметры_и_не_включает_автозапуск()
    {
        var settings = new MemorySettings();
        var vm = Create(settings);

        vm.Setup.LeftPath = @"C:\Left";
        vm.Setup.RightPath = @"C:\Right";
        vm.Setup.SelectedModeIndex = 1;
        vm.Setup.Mirror = true;
        vm.Setup.Exclusions = "bin,obj";

        vm.Setup.Profiles.SaveProfileCommand.Execute(null);

        var profile = SyncProfileStore.Load(settings).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Name, Is.EqualTo("Left → Right"));
            Assert.That(profile.Left, Is.EqualTo(@"C:\Left"));
            Assert.That(profile.Right, Is.EqualTo(@"C:\Right"));
            Assert.That(profile.Mode, Is.EqualTo(1));
            Assert.That(profile.Mirror, Is.True);
            Assert.That(profile.Exclusions, Is.EqualTo("bin,obj"));
            Assert.That(profile.Enabled, Is.False);
            Assert.That(vm.Setup.Profiles.SelectedProfile?.Id, Is.EqualTo(profile.Id));
        }
    }

    [Test]
    public void Обновление_профиля_сохраняет_параметры_расписания()
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(settings,
        [
            new()
            {
                Id = "abc",
                Name = "Backup",
                Left = @"C:\OldLeft",
                Right = @"C:\OldRight",
                Mode = 0,
                Mirror = false,
                Exclusions = "old",
                Interval = ScheduleInterval.Hourly,
                Time = "11:00",
                Enabled = true,
            },
        ]);

        var vm = Create(settings);
        vm.Setup.LeftPath = @"C:\NewLeft";
        vm.Setup.RightPath = @"C:\NewRight";
        vm.Setup.SelectedModeIndex = 2;
        vm.Setup.Mirror = true;
        vm.Setup.Exclusions = "new";

        var profileItem = vm.Setup.Profiles.Items.Single(profile => profile.Id == "abc");
        profileItem.RequestUpdateCommand.Execute(null);
        profileItem.ConfirmUpdateCommand.Execute(null);

        var profile = SyncProfileStore.Load(settings).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Id, Is.EqualTo("abc"));
            Assert.That(profile.Name, Is.EqualTo("Backup"));
            Assert.That(profile.Left, Is.EqualTo(@"C:\NewLeft"));
            Assert.That(profile.Right, Is.EqualTo(@"C:\NewRight"));
            Assert.That(profile.Mode, Is.EqualTo(2));
            Assert.That(profile.Mirror, Is.True);
            Assert.That(profile.Exclusions, Is.EqualTo("new"));
            Assert.That(profile.Interval, Is.EqualTo(ScheduleInterval.Hourly));
            Assert.That(profile.Time, Is.EqualTo("11:00"));
            Assert.That(profile.Enabled, Is.True);
        }
    }

    [Test]
    public void Переименование_профиля_меняет_только_имя()
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(settings,
        [
            new()
            {
                Id = "abc",
                Name = "Backup",
                Left = @"C:\Left",
                Right = @"C:\Right",
                Mode = 1,
                Mirror = true,
                Exclusions = "bin",
                Interval = ScheduleInterval.Hourly,
                Time = "11:00",
                Enabled = true,
            },
        ]);

        var vm = Create(settings);
        var profileItem = vm.Setup.Profiles.Items.Single(profile => profile.Id == "abc");
        profileItem.RequestRenameCommand.Execute(null);
        profileItem.EditName = "Docs";
        profileItem.ConfirmRenameCommand.Execute(null);

        var profile = SyncProfileStore.Load(settings).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(profile.Name, Is.EqualTo("Docs"));
            Assert.That(profile.Left, Is.EqualTo(@"C:\Left"));
            Assert.That(profile.Right, Is.EqualTo(@"C:\Right"));
            Assert.That(profile.Mode, Is.EqualTo(1));
            Assert.That(profile.Mirror, Is.True);
            Assert.That(profile.Exclusions, Is.EqualTo("bin"));
            Assert.That(profile.Interval, Is.EqualTo(ScheduleInterval.Hourly));
            Assert.That(profile.Time, Is.EqualTo("11:00"));
            Assert.That(profile.Enabled, Is.True);
        }
    }

    [Test]
    public void Применение_профиля_записывает_текущие_настройки_синхронизации()
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(settings,
        [
            new()
            {
                Id = "abc",
                Name = "Backup",
                Left = @"C:\Left",
                Right = @"C:\Right",
                Mode = 1,
                Mirror = true,
                Exclusions = "bin,obj",
            },
        ]);

        var vm = Create(settings);

        vm.Setup.Profiles.SelectedProfile = vm.Setup.Profiles.Items.Single(profile => profile.Id == "abc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Setup.LeftPath, Is.EqualTo(@"C:\Left"));
            Assert.That(vm.Setup.RightPath, Is.EqualTo(@"C:\Right"));
            Assert.That(vm.Setup.SelectedModeIndex, Is.EqualTo(1));
            Assert.That(vm.Setup.Mirror, Is.True);
            Assert.That(vm.Setup.Exclusions, Is.EqualTo("bin,obj"));
            Assert.That(settings.GetStringValue(SettingsKeys.SyncLeft), Is.EqualTo(@"C:\Left"));
            Assert.That(settings.GetStringValue(SettingsKeys.SyncRight), Is.EqualTo(@"C:\Right"));
            Assert.That(settings.GetStringValue(SettingsKeys.SyncMode), Is.EqualTo("1"));
            Assert.That(settings.GetStringValue(SettingsKeys.SyncMirror), Is.EqualTo("true"));
            Assert.That(settings.GetStringValue(SettingsKeys.SyncExclusions), Is.EqualTo("bin,obj"));
        }
    }

    [Test]
    public void Удаление_профиля_убирает_его_из_быстрого_списка_и_хранилища()
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(settings,
        [
            new()
            {
                Id = "abc",
                Name = "Backup",
                Left = @"C:\Left",
                Right = @"C:\Right",
            },
        ]);

        var vm = Create(settings);
        var profile = vm.Setup.Profiles.Items.Single(profile => profile.Id == "abc");

        profile.RequestDeleteCommand.Execute(null);
        profile.ConfirmDeleteCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Setup.Profiles.Items.Select(profile => profile.Id), Is.EqualTo(new[] { SyncQuickProfilesViewModel.CurrentProfileId }));
            Assert.That(SyncProfileStore.Load(settings), Is.Empty);
            Assert.That(vm.Setup.Profiles.SelectedProfile?.Id, Is.EqualTo(SyncQuickProfilesViewModel.CurrentProfileId));
        }
    }

    private static SyncViewModel Create(ISettingsStore settings)
    {
        var notifier = new ToastNotifier(new(), new(new MemorySettings()));
        return new(settings,
            new NoopDialogs(),
            new(settings),
            new(settings),
            NullLogger<SyncViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            notifier,
            new(NullLogger<PerformanceMonitor>.Instance),
            new FakeFilePicker());
    }

}
