using Microsoft.Win32.SafeHandles;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DiskSpaceCalculatorTests
{
    private const uint FsctlSetReparsePoint = 0x0009_00A4;
    private const uint ThirdPartyReparseTag = 0x0000_0042;
    private const int ReparseGuidHeaderSize = 24;

    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopCalc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), new('a', 100));
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), new('b', 200));
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), new('c', 300));

        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "d.txt"), new('d', 400));
        File.WriteAllText(Path.Combine(sub, "e.txt"), new('e', 500));

        Directory.CreateDirectory(Path.Combine(_tempDir, "empty"));
    }

    [TearDown]
    public void TearDown()
    {
        RemoveTree(_tempDir);
    }

    [TestCase(1)]
    [TestCase(4)]
    public void CalculateMultithreaded_ReportsProgress(int degree)
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();

        calculator.CalculateMultithreaded(new(_tempDir), degree, progress, CancellationToken.None);

        var snapshot = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.FilesScanned, Is.EqualTo(5));
            Assert.That(snapshot.BytesScanned, Is.EqualTo(1500));
            Assert.That(snapshot.DirectoriesScanned, Is.EqualTo(3));
            Assert.That(snapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(snapshot.TopLevelCompleted, Is.EqualTo(2));
        }
    }

    [TestCase(null)]
    [TestCase(-4)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    public void Calculate_ComputesExpectedTotals(int? degree)
    {
        var calculator = new DiskSpaceCalculator();

        var result = degree is int workers
            ? calculator.CalculateMultithreaded(new(_tempDir), workers, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalFileCount, Is.EqualTo(5));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(2));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_CountsDirectoryItCouldNotRead(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();
        var missing = new DirectoryInfo(Path.Combine(_tempDir, "vanished"));

        var result = multithreaded
            ? calculator.CalculateMultithreaded(missing, 4, progress, CancellationToken.None)
            : calculator.Calculate(missing, progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.State, Is.EqualTo(SpaceState.Error));
            Assert.That(progress.CreateSnapshot().DirectoriesFailed, Is.EqualTo(1));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_CountsFileWithDataReparseTag(bool multithreaded)
    {
        var path = Path.Combine(_tempDir, "placeholder.bin");
        File.WriteAllText(path, new('p', 600));

        if (!TryTagAsDataReparsePoint(path))
        {
            Assert.Ignore("не удалось поставить reparse-точку с тегом-содержимым");
        }

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalFileCount, Is.EqualTo(6));
            Assert.That(result.TotalSize, Is.EqualTo(2100));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_CountsHiddenAndSystemFiles(bool multithreaded)
    {
        var hidden = Path.Combine(_tempDir, "hidden.txt");
        File.WriteAllText(hidden, new('h', 700));
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalFileCount, Is.EqualTo(6));
            Assert.That(result.TotalSize, Is.EqualTo(2200));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Calculate_CountsStayCorrectAfterRemoval(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();
        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        var sub = result.SubDirectories.Single(directory => directory.Name == "sub");
        var empty = result.SubDirectories.Single(directory => directory.Name == "empty");
        var subCountBefore = sub.TotalFileCount;

        sub.Remove(sub.Files[0]);
        result.Remove(empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(subCountBefore, Is.EqualTo(2));
            Assert.That(sub.TotalFileCount, Is.EqualTo(1));
            Assert.That(result.TotalFileCount, Is.EqualTo(4));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(1));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_DoesNotFollowJunction(bool multithreaded)
    {
        if (!TryCreateJunction(Path.Combine(_tempDir, "junction"), Path.Combine(_tempDir, "sub")))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(2));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_DoesNotFollowReparsePoint(bool multithreaded)
    {
        var link = Path.Combine(_tempDir, "link");

        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_tempDir, "sub"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("не удалось создать символьную ссылку");
        }

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(2));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_GivesAbsolutePathsToTree(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        var file = result.Files.Single(x => x.Name == "a.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AbsolutePath, Is.EqualTo(_tempDir));
            Assert.That(file.AbsolutePath, Is.EqualTo(Path.Combine(_tempDir, "a.txt")));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_KeepsCallerExtendedPrefix(bool multithreaded)
    {
        var extended = @"\\?\" + _tempDir;
        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(extended), 4, CancellationToken.None)
            : calculator.Calculate(new(extended), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalFileCount, Is.EqualTo(5));
            Assert.That(result.AbsolutePath, Is.EqualTo(extended));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_KeepsReadableSiblingsOfUnreadableDirectory(bool multithreaded)
    {
        var closed = Path.Combine(_tempDir, "closed");
        Directory.CreateDirectory(closed);
        File.WriteAllText(Path.Combine(closed, "f.txt"), new('f', 600));
        TestAcl.DenyEnumeration(closed);

        try
        {
            var calculator = new DiskSpaceCalculator();

            var result = multithreaded
                ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
                : calculator.Calculate(new(_tempDir), CancellationToken.None);

            var blocked = result.SubDirectories.Single(x => x.Name == "closed");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(blocked.State, Is.EqualTo(SpaceState.Error));
                Assert.That(result.TotalSize, Is.EqualTo(1500));
                Assert.That(result.TotalFileCount, Is.EqualTo(5));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(closed);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_KeepsTildePathsInTreeAndProgress(bool multithreaded)
    {
        var root = Path.Combine(_tempDir, "servicing");
        var package = Path.Combine(root, "Package~31bf3856ad364e35~amd64~~10.0.1");
        Directory.CreateDirectory(package);
        File.WriteAllText(Path.Combine(package, "update~1.mum"), new('u', 800));

        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(root), 4, progress, CancellationToken.None)
            : calculator.Calculate(new(root), progress, CancellationToken.None);

        var directory = result.SubDirectories.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(800));
            Assert.That(directory.AbsolutePath, Is.EqualTo(package));
            Assert.That(directory.Files.Single().AbsolutePath, Is.EqualTo(Path.Combine(package, "update~1.mum")));
            Assert.That(progress.CreateSnapshot().CurrentPath, Is.EqualTo(package));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_KeepsUncPathsInTree(bool multithreaded)
    {
        var unc = $@"\\localhost\{_tempDir[0]}${_tempDir[2..]}";
        Assume.That(Directory.Exists(unc), Is.True, "Административная шара localhost недоступна");

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(unc), 4, CancellationToken.None)
            : calculator.Calculate(new(unc), CancellationToken.None);

        var file = result.SubDirectories.Single(x => x.Name == "sub").Files.Single(x => x.Name == "d.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalFileCount, Is.EqualTo(5));
            Assert.That(result.AbsolutePath, Is.EqualTo(unc));
            Assert.That(file.AbsolutePath, Is.EqualTo(Path.Combine(unc, "sub", "d.txt")));
        }
    }

    [TestCase(true, "dotted.")]
    [TestCase(false, "dotted.")]
    [TestCase(true, "spaced ")]
    [TestCase(false, "spaced ")]
    public void Calculate_ReadsDirectoryWithTrailingDotOrSpace(bool multithreaded, string name)
    {
        var odd = @"\\?\" + Path.Combine(_tempDir, name);
        var file = Path.Combine(odd, "g.txt");
        Directory.CreateDirectory(odd);
        File.WriteAllText(file, new('g', 900));

        try
        {
            var calculator = new DiskSpaceCalculator();

            var result = multithreaded
                ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
                : calculator.Calculate(new(_tempDir), CancellationToken.None);

            var node = result.SubDirectories.Single(x => x.Name == name);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(node.State, Is.Not.EqualTo(SpaceState.Error));
                Assert.That(node.TotalSize, Is.EqualTo(900));
                Assert.That(result.TotalSize, Is.EqualTo(2400));
                Assert.That(result.TotalFileCount, Is.EqualTo(6));
            }
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(odd);
        }
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void Calculate_RefusesLinkRoot(bool junction, bool multithreaded)
    {
        var link = Path.Combine(_tempDir, "root-link");
        var target = Path.Combine(_tempDir, "sub");

        if (!(junction ? TryCreateJunction(link, target) : TryCreateSymbolicDirectory(link, target)))
        {
            Assert.Ignore("не удалось создать ссылку");
        }

        var calculator = new DiskSpaceCalculator();

        var exception = Assert.Throws<ScanRootLinkException>(() =>
        {
            _ = multithreaded
                ? calculator.CalculateMultithreaded(new(link), 4, CancellationToken.None)
                : calculator.Calculate(new(link), CancellationToken.None);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Path, Is.EqualTo(link));
            Assert.That(exception.Message, Does.Contain("ссылка на другое место"));
            Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(link + Path.DirectorySeparatorChar)), Is.Not.Null);
            Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(target)), Is.Null);
            Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(Path.GetPathRoot(_tempDir)!)), Is.Null);
        }
    }

    [Test]
    public void Calculate_ReportsProgress()
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();

        calculator.Calculate(new(_tempDir), progress, CancellationToken.None);

        var snapshot = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.FilesScanned, Is.EqualTo(5));
            Assert.That(snapshot.BytesScanned, Is.EqualTo(1500));
            Assert.That(snapshot.DirectoriesScanned, Is.EqualTo(3));
            Assert.That(snapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(snapshot.TopLevelCompleted, Is.EqualTo(2));
            Assert.That(snapshot.Fraction, Is.EqualTo(1d));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_SkipsSymbolicLinkFile(bool multithreaded)
    {
        try
        {
            File.CreateSymbolicLink(Path.Combine(_tempDir, "link.txt"), Path.Combine(_tempDir, "a.txt"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("не удалось создать символьную ссылку");
        }

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalFileCount, Is.EqualTo(5));
            Assert.That(result.TotalSize, Is.EqualTo(1500));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_ThrowsOnCancellation(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => _ = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, source.Token)
            : calculator.Calculate(new(_tempDir), source.Token));
    }

    [TestCase(@"\\?\Volume{0b1c2d3e-0000-0000-0000-100000000000}\dir")]
    [TestCase(@"\\.\C:\dir")]
    [TestCase(@"\??\C:\dir")]
    public void ScanProgressSnapshot_KeepsDevicePaths(string path)
    {
        var progress = new ScanProgress();
        progress.EnterDirectory(path);

        Assert.That(progress.CreateSnapshot().CurrentPath, Is.EqualTo(path));
    }

    [TestCase(@"\\?\C:\", @"C:\")]
    [TestCase(@"\\?\C:\Windows\System32", @"C:\Windows\System32")]
    [TestCase(@"\\?\UNC\server\share\dir", @"\\server\share\dir")]
    [TestCase(@"C:\dir\sub", @"C:\dir\sub")]
    [TestCase(@"\\server\share\dir", @"\\server\share\dir")]
    public void ScanProgressSnapshot_StripsExtendedLengthPrefixes(string input, string expected)
    {
        var progress = new ScanProgress();
        progress.EnterDirectory(input);

        Assert.That(progress.CreateSnapshot().CurrentPath, Is.EqualTo(expected));
    }

    [Test]
    public void IsLink_AcceptsVolumeMountedIntoFolder()
    {
        var mount = Path.Combine(_tempDir, "mounted-volume");
        Directory.CreateDirectory(mount);

        if (!TryMountVolume(mount, Path.GetPathRoot(_tempDir)!))
        {
            Assert.Ignore("не удалось смонтировать том в папку");
        }

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(mount)), Is.Null);
                Assert.That(DiskSpaceCalculator.IsReparsePoint(new(mount)), Is.True);
            }
        }
        finally
        {
            Directory.Delete(mount);
        }
    }

    [TestCase(@"\??\Volume{6f1d2c3a-0000-0000-0000-100000000000}\", ExpectedResult = true)]
    [TestCase(@"\??\volume{6f1d2c3a-0000-0000-0000-100000000000}\", ExpectedResult = true)]
    [TestCase(@"\??\Volume{6f1d2c3a-0000-0000-0000-100000000000}\Users\x", ExpectedResult = false)]
    [TestCase(@"\??\Volume{6f1d2c3a-0000-0000-0000-100000000000}", ExpectedResult = false)]
    [TestCase(@"\??\Volume{not-a-guid}\", ExpectedResult = false)]
    [TestCase(@"\??\C:\Data\Backup", ExpectedResult = false)]
    [TestCase(@"\??\UNC\server\share", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    public bool ReparsePoint_TellsVolumeTargetFromFolderTarget(string substituteName)
    {
        return ReparsePoint.IsVolumeTarget(substituteName);
    }

    [Test]
    public void ScanProgressSnapshot_FractionIsNull_WhenTopLevelUnknown()
    {
        var progress = new ScanProgress();

        Assert.That(progress.CreateSnapshot().Fraction, Is.Null);
    }

    [Test]
    public void ScanProgressSnapshot_TellsStageFromPath()
    {
        var progress = new ScanProgress();

        progress.Announce("Чтение $MFT тома C: 40 %");
        var stage = progress.CreateSnapshot();

        progress.EnterDirectory(@"C:\Windows");
        var path = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stage.CurrentPath, Is.EqualTo("Чтение $MFT тома C: 40 %"));
            Assert.That(stage.IsStage, Is.True);
            Assert.That(path.CurrentPath, Is.EqualTo(@"C:\Windows"));
            Assert.That(path.IsStage, Is.False);
        }
    }

    private static void RemoveTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var directory = new DirectoryInfo(path);

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();
            return;
        }

        foreach (var sub in directory.GetDirectories())
        {
            RemoveTree(sub.FullName);
        }

        directory.Delete(true);
    }

    private static bool TryTagAsDataReparsePoint(string path)
    {
        var buffer = new byte[ReparseGuidHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, ThirdPartyReparseTag);
        Guid.NewGuid().TryWriteBytes(buffer.AsSpan(8));

        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);

            if (!DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, char[] lpszVolumeName, int cchBufferLength);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    private static bool TryMountVolume(string path, string volumeRoot)
    {
        var name = new char[64];

        if (!GetVolumeNameForVolumeMountPoint(volumeRoot, name, name.Length))
        {
            return false;
        }

        var substitute = Encoding.Unicode.GetBytes(@"\??\" + new string(name).TrimEnd('\0')[4..]);
        var pathBuffer = substitute.Length + 4;
        var buffer = new byte[16 + pathBuffer];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0xA000_0003);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(8 + pathBuffer));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)(substitute.Length + 2));
        substitute.CopyTo(buffer, 16);

        using var handle = CreateFile(path, 0x4000_0000, 0x7, IntPtr.Zero, 3, 0x0200_0000 | 0x0020_0000, IntPtr.Zero);

        return !handle.IsInvalid
            && DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool TryCreateSymbolicDirectory(string path, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        process.WaitForExit();

        return process.ExitCode == 0;
    }
}
