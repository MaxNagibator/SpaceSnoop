using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DirectorySpaceRemoveTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopRemove_{Guid.NewGuid():N}");
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

    [Test]
    public void AddFile_ToSubdir_UpdatesSubdirectoryAndRootTotalSize()
    {
        var (root, sub1) = BuildTree(_tempDir);
        var archivePath = Path.Combine(_tempDir, "sub1.zip");
        File.WriteAllBytes(archivePath, new byte[75]);

        sub1.AddFile(new(archivePath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sub1.TotalSize,
                Is.EqualTo(175),
                "TotalSize подкаталога увеличился на размер файла");

            Assert.That(
                root.TotalSize,
                Is.EqualTo(425),
                "TotalSize корня увеличился на размер файла");

            Assert.That(
                sub1.Files.Select(file => file.Name),
                Does.Contain("sub1.zip"),
                "Файл добавлен в коллекцию Files");
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Remove_FileAtAnyPosition_KeepsOtherFilesInOrder(int index)
    {
        var parentDir = new DirectoryInfo(_tempDir);
        var parent = new DirectorySpace(parentDir.Name, null, parentDir.CreationTime, parentDir.LastAccessTime);
        var files = new[]
            {
                "a.bin", "b.bin", "c.bin",
            }.Select(x =>
            {
                var path = Path.Combine(_tempDir, x);
                File.WriteAllBytes(path, new byte[10]);
                return new FileInfo(path);
            })
            .ToArray();

        parent.AddFiles(files.AsSpan());
        var expected = parent.Files.Where((_, i) => i != index).ToArray();

        parent.Remove(parent.Files[index]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parent.Files, Is.EqualTo(expected));
            Assert.That(parent.TotalFileCount, Is.EqualTo(2));
            Assert.That(parent.TotalSize, Is.EqualTo(20));
        }
    }

    [Test]
    public void Remove_FileFromSubdir_UpdatesSubdirectoryAndRootTotalSize()
    {
        var (root, sub1) = BuildTree(_tempDir);
        var file1 = sub1.Files.Single();

        sub1.Remove(file1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sub1.TotalSize,
                Is.Zero,
                "TotalSize подкаталога уменьшился на размер файла");

            Assert.That(
                root.TotalSize,
                Is.EqualTo(250),
                "TotalSize корня уменьшился на размер файла");

            Assert.That(
                sub1.Files,
                Does.Not.Contain(file1),
                "Файл удалён из коллекции Files");
        }
    }

    [Test]
    public void Remove_StopsAtSyntheticParent_CreatedByFixAbsolutePath()
    {
        var dirPath = Path.Combine(_tempDir, "scanroot");
        Directory.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, "x.txt");
        File.WriteAllBytes(filePath, new byte[300]);

        var scanRoot = new DirectorySpace("scanroot", null, DateTime.Now, DateTime.Now);
        FileInfo[] files = [new(filePath)];
        scanRoot.AddFiles(files.AsSpan());

        scanRoot.FixAbsolutePath(new(dirPath));

        var syntheticParent = (DirectorySpace)scanRoot.Parent!;
        var syntheticBefore = syntheticParent.TotalSize;

        scanRoot.Remove(scanRoot.Files[0]);

        Assert.That(
            syntheticParent.TotalSize,
            Is.EqualTo(syntheticBefore),
            "Синтетический родитель FixAbsolutePath не должен изменяться при Remove");
    }

    [Test]
    public void Remove_Subdir_UpdatesParentTotalSize()
    {
        var (root, sub1) = BuildTree(_tempDir);

        root.Remove(sub1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                root.TotalSize,
                Is.EqualTo(250),
                "TotalSize корня уменьшился на размер удалённого подкаталога");

            Assert.That(
                root.SubDirectories,
                Does.Not.Contain(sub1),
                "Подкаталог удалён из коллекции SubDirectories");
        }
    }

    [Test]
    public void Remove_AllFiles_LeavesEmptyDirectory()
    {
        var parentDir = new DirectoryInfo(_tempDir);
        var parent = new DirectorySpace(parentDir.Name, null, parentDir.CreationTime, parentDir.LastAccessTime);

        var pathA = Path.Combine(_tempDir, "a.bin");
        var pathB = Path.Combine(_tempDir, "b.bin");
        File.WriteAllBytes(pathA, new byte[100]);
        File.WriteAllBytes(pathB, new byte[200]);
        FileInfo[] files = [new(pathA), new(pathB)];
        parent.AddFiles(files.AsSpan());

        var fileA = parent.Files[0];
        var fileB = parent.Files[1];

        parent.Remove(fileA);
        parent.Remove(fileB);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parent.Files, Is.Empty);
            Assert.That(parent.TotalFileCount, Is.Zero);
            Assert.That(parent.TotalSize, Is.Zero);
        }
    }

    private static (DirectorySpace Root, DirectorySpace Sub1) BuildTree(string tempDir)
    {
        var rootDir = new DirectoryInfo(tempDir);
        var root = new DirectorySpace(rootDir.Name, null, rootDir.CreationTime, rootDir.LastAccessTime);

        var sub1Path = Path.Combine(tempDir, "sub1");
        Directory.CreateDirectory(sub1Path);
        var file1Path = Path.Combine(sub1Path, "file1.txt");
        File.WriteAllBytes(file1Path, new byte[100]);

        var sub1Info = new DirectoryInfo(sub1Path);
        var sub1 = new DirectorySpace(sub1Info.Name, root, sub1Info.CreationTime, sub1Info.LastAccessTime);
        FileInfo[] sub1Files = [new(file1Path)];
        sub1.AddFiles(sub1Files.AsSpan());

        var sub2Path = Path.Combine(tempDir, "sub2");
        Directory.CreateDirectory(sub2Path);
        var file2Path = Path.Combine(sub2Path, "file2.txt");
        File.WriteAllBytes(file2Path, new byte[200]);

        var sub2Info = new DirectoryInfo(sub2Path);
        var sub2 = new DirectorySpace(sub2Info.Name, root, sub2Info.CreationTime, sub2Info.LastAccessTime);
        FileInfo[] sub2Files = [new(file2Path)];
        sub2.AddFiles(sub2Files.AsSpan());

        var file3Path = Path.Combine(tempDir, "file3.txt");
        File.WriteAllBytes(file3Path, new byte[50]);
        FileInfo[] rootFiles = [new(file3Path)];
        root.AddFiles(rootFiles.AsSpan());

        root.Add(sub1);
        root.Add(sub2);

        return (root, sub1);
    }
}
