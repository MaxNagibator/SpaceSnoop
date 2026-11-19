using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncRootsTests
{
    private const string AdminShare = @"\\localhost\c$\Windows";
    private const uint MountPointTag = 0xA000_0003;
    private const uint FsctlSetReparsePoint = 0x0009_00A4;
    private const uint GenericWrite = 0x4000_0000;
    private const uint FlagBackupSemantics = 0x0200_0000;
    private const uint FlagOpenReparsePoint = 0x0020_0000;
    private readonly List<string> _links = [];

    private string _tempDir = null!;
    private string _leftDir = null!;
    private string _nestedDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTest_{Guid.NewGuid():N}");
        _leftDir = Path.Combine(_tempDir, "left");
        _nestedDir = Path.Combine(_leftDir, "nested");
        Directory.CreateDirectory(_nestedDir);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var link in _links.Where(static link => new DirectoryInfo(link).Attributes != (FileAttributes)(-1)))
        {
            Directory.Delete(link);
        }

        _links.Clear();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Junction_в_середине_пути_ещё_не_созданного_приёмника_считается_перекрытием()
    {
        var link = Junction("link", _leftDir);

        Assert.That(SyncRoots.Overlap(_leftDir, Path.Combine(link, "backup", "new")), Is.True);
    }

    [Test]
    public void Junction_на_второй_корень_считается_перекрытием()
    {
        var link = Junction("link", _leftDir);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.Overlap(_leftDir, link), Is.True);
            Assert.That(SyncRoots.Overlap(_nestedDir, link), Is.True);
            Assert.That(SyncRoots.Overlap(link, _nestedDir), Is.True);
        }
    }

    [Test]
    public void Junction_на_соседний_каталог_перекрытием_не_считается()
    {
        var other = Directory.CreateDirectory(Path.Combine(_tempDir, "other")).FullName;
        var link = Junction("link", other);

        Assert.That(SyncRoots.Overlap(_leftDir, link), Is.False);
    }

    [Test]
    public void Битая_junction_на_пути_к_корню_даёт_отказ_а_не_строковое_сравнение()
    {
        var target = Directory.CreateDirectory(Path.Combine(_tempDir, "gone")).FullName;
        var link = Junction("link", target);
        Directory.Delete(target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.Refusal(_leftDir, Path.Combine(link, "backup")), Is.EqualTo(SyncRoots.UnresolvedLinkMessage));
            Assert.That(SyncRoots.Refusal(link, _leftDir), Is.EqualTo(SyncRoots.UnresolvedLinkMessage));
        }
    }

    [Test]
    public void Движок_отказывает_на_корне_через_junction_до_записи_на_диск()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "данные");
        var link = Junction("link", _nestedDir);

        var root = new DirectoryComparison("root", "");
        root.Files.Add(
            new("a.txt", "a.txt")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
            });

        root.SubDirectories.Add(
            new("fresh", "fresh")
            {
                Status = ComparisonStatus.LeftOnly,
                Action = SyncAction.CopyToRight,
            });

        var result = new ComparisonResult(_leftDir, link, root);

        var refusal = Assert.Throws<InvalidOperationException>(() => new SyncEngine(NullLogger<SyncEngine>.Instance, false).Execute(result, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refusal.Message, Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(Directory.EnumerateFileSystemEntries(_nestedDir), Is.Empty);
        }
    }

    [TestCase(@"\\?\C:\A\B", @"C:\A\B")]
    [TestCase(@"\\?\UNC\server\share\A", @"\\server\share\A")]
    [TestCase(@"\\?\unc\server\share", @"\\server\share")]
    [TestCase(@"C:\A", @"C:\A")]
    public void Окончательный_путь_теряет_длинный_префикс(string final, string expected)
    {
        Assert.That(SyncRoots.StripLongPrefix(final), Is.EqualTo(expected));
    }

    [TestCase(@"C:\A", @"C:\B", false)]
    [TestCase(@"C:\A", @"C:\AB", false)]
    [TestCase(@"C:\A", @"C:\A", true)]
    [TestCase(@"C:\A\", @"C:\A", true)]
    [TestCase(@"C:\A", @"c:\a", true)]
    [TestCase(@"C:\A", @"C:\A\Sub", true)]
    [TestCase(@"C:\A\Sub", @"C:\A", true)]
    [TestCase(@"C:\", @"C:\A", true)]
    [TestCase(@"C:\A\..\B", @"C:\B\Sub", true)]
    [TestCase("", @"C:\A", false)]
    [TestCase(@"\\server\share\A", @"\\SERVER\share\A\Sub", true)]
    public void Совпадающие_и_вложенные_каталоги_распознаются(string left, string right, bool overlap)
    {
        Assert.That(SyncRoots.Overlap(left, right), Is.EqualTo(overlap));
    }

    [TestCase(false, true, TestName = "Корни Foo и foo в обычном каталоге считаются перекрывающимися")]
    [TestCase(true, false, TestName = "Корни Foo и foo в регистрозависимом каталоге не считаются перекрывающимися")]
    public void Регистр_имени_корня_учитывает_регистрозависимость_родительского_каталога(bool caseSensitive, bool overlap)
    {
        var parent = Directory.CreateDirectory(Path.Combine(_tempDir, "cased")).FullName;

        if (caseSensitive)
        {
            Assume.That(TryEnableCaseSensitivity(parent), "fsutil не включает регистрозависимость каталога на этой машине");
        }

        var upper = Directory.CreateDirectory(Path.Combine(parent, "Foo")).FullName;
        var lower = Path.Combine(parent, "foo");

        if (caseSensitive)
        {
            Directory.CreateDirectory(lower);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.Overlap(upper, lower), Is.EqualTo(overlap));
            Assert.That(SyncRoots.Overlap(upper, Path.Combine(lower, "sub")), Is.EqualTo(overlap));
        }
    }

    [Test]
    public void Сравнение_отказывает_на_вложенных_корнях()
    {
        var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
        var request = new CompareDirectoriesRequest(_leftDir, _nestedDir, string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, true);

        var refusal = Assert.Throws<InvalidOperationException>(() => compare.Execute(request, CancellationToken.None));

        Assert.That(refusal.Message, Is.EqualTo(SyncRoots.OverlapMessage));
    }

    [Test]
    public void Том_без_буквы_смонтированный_в_папку_сравнивается_по_GUID_без_ложного_отказа()
    {
        if (LetterlessVolume() is not { } volume)
        {
            Assert.Ignore("на машине нет тома без буквы диска");
            return;
        }

        var mount = Directory.CreateDirectory(Path.Combine(_tempDir, "mounted")).FullName;

        if (!TryMountVolume(mount, volume))
        {
            Assert.Ignore("не удалось смонтировать том в папку");
        }

        _links.Add(mount);
        var alias = Junction("alias", mount);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.Refusal(mount, _leftDir), Is.Null);
            Assert.That(SyncRoots.Refusal(_leftDir, Path.Combine(mount, "new")), Is.Null);
            Assert.That(SyncRoots.Refusal(mount, volume), Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(SyncRoots.Refusal(alias, Path.Combine(mount, "new")), Is.EqualTo(SyncRoots.OverlapMessage));

            if (Directory.Exists(AdminShare))
            {
                Assert.That(SyncRoots.Refusal(mount, AdminShare), Is.Null);
            }
        }
    }

    private string Junction(string name, string target)
    {
        var path = Path.Combine(_tempDir, name);
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);
        process?.WaitForExit();

        if (process is not { ExitCode: 0 })
        {
            Assert.Ignore("не удалось создать junction");
        }

        _links.Add(path);
        return path;
    }

    private static bool TryEnableCaseSensitivity(string directory)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo("fsutil", ["file", "setCaseSensitiveInfo", directory, "enable"])
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static string? LetterlessVolume()
    {
        var name = new char[64];
        using var search = FindFirstVolume(name, name.Length);

        if (search.IsInvalid)
        {
            return null;
        }

        do
        {
            var volume = new string(name).TrimEnd('\0');
            var paths = new char[1024];

            if (GetVolumePathNamesForVolumeName(volume, paths, paths.Length, out _) && paths[0] == '\0' && Directory.Exists(volume))
            {
                return volume;
            }
        } while (FindNextVolume(search, name, name.Length));

        return null;
    }

    private static bool TryMountVolume(string path, string volume)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + volume[4..]);
        var pathBuffer = substitute.Length + 4;
        var buffer = new byte[16 + pathBuffer];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, MountPointTag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(8 + pathBuffer));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)(substitute.Length + 2));
        substitute.CopyTo(buffer, 16);

        using var handle = CreateFile(path, GenericWrite, 0x7, IntPtr.Zero, 3, FlagBackupSemantics | FlagOpenReparsePoint, IntPtr.Zero);

        return !handle.IsInvalid
            && DeviceIoControl(handle, FsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern VolumeSearchHandle FindFirstVolume([Out] char[] volumeName, int length);

    [DllImport("kernel32.dll", EntryPoint = "FindNextVolumeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FindNextVolume(VolumeSearchHandle search, [Out] char[] volumeName, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr search);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathNamesForVolumeName(string volumeName, [Out] char[] paths, int length, out int returned);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint code,
        byte[] input,
        int inputSize,
        IntPtr output,
        int outputSize,
        out int returned,
        IntPtr overlapped);

    private sealed class VolumeSearchHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            return FindVolumeClose(handle);
        }
    }
}
