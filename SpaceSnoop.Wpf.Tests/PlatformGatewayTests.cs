using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Schedule;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PlatformGatewayTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopGateway_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [TestCase(true, "Запущено в фоне – результат появится в истории.")]
    [TestCase(false, "Не удалось запустить")]
    public void Профиль_расписания_сообщает_о_судьбе_фонового_запуска(bool succeeds, string expected)
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(
            settings,
            [
                new()
                {
                    Id = "one",
                    Name = "Один",
                    Left = @"C:\A",
                    Right = @"C:\B",
                },
            ]);

        var shell = new FakeShellLauncher
        {
            Succeeds = succeeds,
        };

        var schedule = new ScheduleViewModel(settings, new NoopDialogs(), new FakeFilePicker(), shell, new FakeScheduleRunner(), new FakeUiDispatcher(), NullLogger<ScheduleViewModel>.Instance);

        var profile = schedule.Profiles[0];
        profile.RunNowCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell.Started, Has.Count.EqualTo(1));
            Assert.That(shell.Started[0], Does.Contain(AppInfo.SyncArgument).And.EndWith("one"));
            Assert.That(profile.Message, Is.EqualTo(expected));
        }
    }

    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void Узел_дерева_показывает_файл_выделенным_а_каталог_открывает(bool directory, bool revealFiles, bool expectReveal)
    {
        var settings = new MemorySettings();
        var preferences = new ScanPreferences(settings)
        {
            RevealFiles = revealFiles,
        };

        var shell = new FakeShellLauncher();
        var factory = new ScanNodeFactory(preferences, new(settings), shell);

        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        space.AddFile(MakeFile("payload", 16));

        var root = factory.CreateRoot(space, new());
        root.EnsureLoaded();

        var node = directory ? root : root.Children.First(static child => !child.IsDirectory);
        node.OpenInExplorerCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                shell.Revealed,
                Is.EqualTo(
                    expectReveal
                        ? new[]
                        {
                            node.Space!.AbsolutePath,
                        }
                        : []));

            Assert.That(
                shell.Opened,
                Is.EqualTo(
                    expectReveal
                        ? []
                        : new[]
                        {
                            node.Space!.AbsolutePath,
                        }));
        }
    }

    private FileInfo MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, $"{name}.bin");
        File.WriteAllBytes(path, new byte[size]);

        return new(path);
    }
}
