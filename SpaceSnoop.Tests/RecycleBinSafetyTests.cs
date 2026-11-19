using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;

namespace SpaceSnoop.Tests;

[TestFixture]
public class RecycleBinSafetyTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopRecycle_{Guid.NewGuid():N}");
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

    [TestCase(@"\\127.0.0.1\nonexistent\file.txt")]
    [TestCase(@"\\?\UNC\server\share\file.txt")]
    [TestCase(@"//server/share/file.txt")]
    public void SupportsRecycleBin_возвращает_false_для_UNC(string path)
    {
        Assert.That(RecycleBin.SupportsRecycleBin(path), Is.False);
    }

    [Test]
    public void SupportsRecycleBin_возвращает_false_для_сетевого_диска_и_ссылки_на_UNC()
    {
        var networkDrive = RecycleBin.SupportsRecycleBin(
            @"Z:\folder\file.txt",
            static root => root.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? DriveType.Network : DriveType.Fixed,
            static _ => null);

        var symlinkToUnc = RecycleBin.SupportsRecycleBin(
            @"C:\local\link\file.txt",
            static _ => DriveType.Fixed,
            static _ => @"\\?\UNC\nas\media\file.txt");

        var localFixed = RecycleBin.SupportsRecycleBin(
            @"C:\local\dir\file.txt",
            static _ => DriveType.Fixed,
            static _ => @"\\?\C:\local\dir\file.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(networkDrive, Is.False);
            Assert.That(symlinkToUnc, Is.False);
            Assert.That(localFixed, Is.True);
        }
    }

    [Test]
    public void DeleteSilent_на_UNC_бросает_RecycleBinUnavailableException_до_Shell_API()
    {
        const string UncFile = @"\\127.0.0.1\nonexistent\file.txt";

        var singleEx = Assert.Throws<RecycleBinUnavailableException>(() => RecycleBin.DeleteSilent(UncFile));
        var batchEx = Assert.Throws<RecycleBinUnavailableException>(() => RecycleBin.DeleteSilent([UncFile]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singleEx!.Path, Is.EqualTo(UncFile));
            Assert.That(batchEx!.Path, Is.EqualTo(UncFile));
        }
    }

    [Test]
    public void DeleteSilent_с_инъектированным_запретом_корзины_не_вызывает_удаление_и_оставляет_файл()
    {
        var file = Path.Combine(_tempDir, "keep.txt");
        File.WriteAllText(file, "important");
        var shellCalled = false;

        Assert.Throws<RecycleBinUnavailableException>(
            () => RecycleBin.DeleteSilent(
                [file],
                static _ => false,
                _ => shellCalled = true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shellCalled, Is.False);
            Assert.That(File.Exists(file), Is.True);
        }
    }

    [Test]
    public void SyncEngine_без_AllowPermanentNetworkDelete_бросает_исключение_и_не_удаляет_файл()
    {
        var leftDir = Directory.CreateDirectory(Path.Combine(_tempDir, "left")).FullName;
        var rightDir = Directory.CreateDirectory(Path.Combine(_tempDir, "right")).FullName;
        var rightFile = Path.Combine(rightDir, "target.txt");
        File.WriteAllText(rightFile, "keep-me");

        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(
            new("target.txt", "target.txt")
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 7,
            });

        var comparison = new ComparisonResult(leftDir, rightDir, root);
        var engine = new SyncEngine(
            NullLogger<SyncEngine>.Instance,
            showDeleteUi: false,
            recycleOverwritten: true,
            allowPermanentNetworkDelete: false,
            supportsRecycleBin: static _ => false);

        var exception = Assert.Throws<RecycleBinUnavailableException>(() => engine.Execute(comparison, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Path, Is.EqualTo(rightFile));
            Assert.That(File.Exists(rightFile), Is.True);
        }
    }

    [Test]
    public void ExecuteSyncUseCase_по_умолчанию_запрещает_удаление_на_UNC()
    {
        var leftDir = Directory.CreateDirectory(Path.Combine(_tempDir, "uc-left")).FullName;
        var rightDir = Directory.CreateDirectory(Path.Combine(_tempDir, "uc-right")).FullName;
        var rightFile = Path.Combine(rightDir, "target.txt");
        File.WriteAllText(rightFile, "keep-me");

        var root = new DirectoryComparison("root", string.Empty);
        root.Files.Add(
            new("target.txt", "target.txt")
            {
                Status = ComparisonStatus.RightOnly,
                Action = SyncAction.DeleteRight,
                RightSize = 7,
            });

        var comparison = new ComparisonResult(leftDir, rightDir, root);
        var useCase = new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance, static _ => false);

        var request = new ExecuteSyncRequest(comparison, DeleteUi: SyncDeleteUi.Silent);
        var exception = Assert.Throws<RecycleBinUnavailableException>(() => useCase.Execute(request, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.AllowPermanentNetworkDelete, Is.False);
            Assert.That(exception!.Path, Is.EqualTo(rightFile));
            Assert.That(File.Exists(rightFile), Is.True);
        }
    }

    [Test]
    public void ArchiveService_DeleteDirectoryToRecycleBin_без_флага_на_UNC_бросает_исключение()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_tempDir, "archive-src")).FullName;
        var file = Path.Combine(folder, "a.txt");
        File.WriteAllText(file, "data");

        var service = new ArchiveService(static _ => false);

        Assert.Throws<RecycleBinUnavailableException>(() => service.DeleteDirectoryToRecycleBin(folder, false));
        Assert.Throws<RecycleBinUnavailableException>(
            () => new ArchiveService().DeleteDirectoryToRecycleBin(@"\\127.0.0.1\nonexistent\dir", false));

        Assert.That(File.Exists(file), Is.True);
    }
}
