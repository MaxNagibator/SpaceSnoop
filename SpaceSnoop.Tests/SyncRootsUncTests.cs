using SpaceSnoop.Core;
using System.Net;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncRootsUncTests
{
    [Test]
    public void Алгебра_идентичностей_и_хвостов_различает_перекрытие_и_соседние_ветки()
    {
        var shareId = new SyncRoots.DirIdentity(0x1234_5678, 100, 0);
        var childId = new SyncRoots.DirIdentity(0x1234_5678, 200, 0);

        SyncRoots.IdentityProbe ProbeShareAndChild(string path)
        {
            if (path.EndsWith(@"\sub", StringComparison.OrdinalIgnoreCase))
            {
                return SyncRoots.IdentityProbe.From(childId);
            }

            if (path.Equals(@"\\nas\media", StringComparison.OrdinalIgnoreCase)
                || path.Equals(@"\\192.168.1.20\media", StringComparison.OrdinalIgnoreCase))
            {
                return SyncRoots.IdentityProbe.From(shareId);
            }

            return SyncRoots.IdentityProbe.Missing;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.HasPhysicalIdentityOverlap(@"\\nas\media", @"\\192.168.1.20\media", ProbeShareAndChild), Is.True);
            Assert.That(SyncRoots.HasPhysicalIdentityOverlap(@"\\nas\media\a", @"\\192.168.1.20\media\a\b", ProbeShareAndChild), Is.True);
            Assert.That(SyncRoots.HasPhysicalIdentityOverlap(@"\\nas\media\a", @"\\192.168.1.20\media\b", ProbeShareAndChild), Is.False);
            Assert.That(SyncRoots.HasPhysicalIdentityOverlap(@"\\nas\media", @"\\192.168.1.20\media\sub", ProbeShareAndChild), Is.True);
            Assert.That(SyncRoots.HasPhysicalIdentityOverlap(@"\\nas\media\sub\new", @"\\192.168.1.20\media", ProbeShareAndChild), Is.True);
        }
    }

    [Test]
    public void Нулевой_FileId_отключает_сравнение_идентичностей_без_ложного_отказа()
    {
        var refusal = SyncRoots.RefusalCore(
            @"\\router1\usb\dirA",
            @"\\router2\usb\dirB",
            static (path, _) => new(SyncRoots.FinalKind.Resolved, path),
            static _ => null,
            static _ => SyncRoots.IdentityProbe.Zero);

        Assert.That(refusal, Is.Null);
    }

    [Test]
    public void Фолбэк_VolumeNameNt_различает_совпадающие_и_разные_тома_при_NoVolumeName()
    {
        SyncRoots.Final ReadNtFallback(string path, uint volumeName)
        {
            if (volumeName != 2)
            {
                return new(SyncRoots.FinalKind.NoVolumeName, path);
            }

            return path switch
            {
                @"X:\data" => new(SyncRoots.FinalKind.Resolved, @"\Device\ImDisk0\data"),
                @"Y:\data\sub" => new(SyncRoots.FinalKind.Resolved, @"\Device\ImDisk0\data\sub"),
                @"\\nas\share" => new(SyncRoots.FinalKind.Resolved, @"\Device\Mup\nas\share"),
                _ => new(SyncRoots.FinalKind.Resolved, path),
            };
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SyncRoots.RefusalCore(@"X:\data", @"Y:\data\sub", ReadNtFallback, static _ => null, static _ => SyncRoots.IdentityProbe.Failed),
                Is.EqualTo(SyncRoots.OverlapMessage));

            Assert.That(
                SyncRoots.RefusalCore(@"X:\data", @"\\nas\share", ReadNtFallback, static _ => null, static _ => SyncRoots.IdentityProbe.Failed),
                Is.Null);
        }
    }

    [Test]
    public void Физическая_идентичность_проверяется_даже_когда_том_не_вернул_имя()
    {
        var identity = SyncRoots.IdentityProbe.From(new(17, 5, 0));

        var refusal = SyncRoots.RefusalCore(
            @"C:\data",
            @"\\localhost\data",
            static (path, _) => new(SyncRoots.FinalKind.NoVolumeName, path),
            static _ => null,
            _ => identity,
            static _ => []);

        Assert.That(refusal, Is.EqualTo(SyncRoots.OverlapMessage));
    }

    [Test]
    public void Локальная_шара_по_localhost_и_127_0_0_1_перекрывается_с_локальным_диском()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        var relativeSystem = Path.GetRelativePath(systemRoot, Environment.SystemDirectory);
        var adminLetter = $"{systemRoot[0]}$";
        var loopbackByHost = $@"\\localhost\{adminLetter}\{relativeSystem}";
        var loopbackByIp = $@"\\127.0.0.1\{adminLetter}\{relativeSystem}";

        if (!Directory.Exists(loopbackByHost))
        {
            Assert.Ignore("административная шара localhost недоступна на этой машине");
            return;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncRoots.Refusal(Environment.SystemDirectory, loopbackByHost), Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(SyncRoots.Refusal(Environment.SystemDirectory, loopbackByIp), Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(SyncRoots.Refusal(Environment.SystemDirectory, $@"{loopbackByIp}\drivers"), Is.EqualTo(SyncRoots.OverlapMessage));
        }
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void Фолбэк_UNC_при_недоступной_идентичности_отказывает_на_общем_IP_и_шаре_и_пропускает_разные(
        bool zeroId,
        bool hasVolumeName)
    {
        var probe = zeroId ? SyncRoots.IdentityProbe.Zero : SyncRoots.IdentityProbe.Failed;
        var kind = hasVolumeName ? SyncRoots.FinalKind.Resolved : SyncRoots.FinalKind.NoVolumeName;
        SyncRoots.Final ReadFinalPath(string path, uint _) => new(kind, path);

        IReadOnlyList<IPAddress> Resolve(string host) => host.ToLowerInvariant() switch
        {
            "nas" or "nas.local" or "192.168.0.5" => [IPAddress.Parse("192.168.0.5")],
            "other" => [IPAddress.Parse("192.168.0.9")],
            _ => [],
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SyncRoots.RefusalCore(
                    @"\\nas\data\a",
                    @"\\192.168.0.5\data\a\b",
                    ReadFinalPath,
                    static _ => null,
                    _ => probe,
                    Resolve),
                Is.EqualTo(SyncRoots.OverlapMessage));

            Assert.That(
                SyncRoots.RefusalCore(
                    @"\\nas\shareA\a",
                    @"\\192.168.0.5\shareB\a",
                    ReadFinalPath,
                    static _ => null,
                    _ => probe,
                    Resolve),
                Is.Null);

            Assert.That(
                SyncRoots.RefusalCore(
                    @"\\nas\data\a",
                    @"\\192.168.0.5\data\b",
                    ReadFinalPath,
                    static _ => null,
                    _ => probe,
                    Resolve),
                Is.Null);

            Assert.That(
                SyncRoots.RefusalCore(
                    @"\\nas\data\a",
                    @"\\other\data\a",
                    ReadFinalPath,
                    static _ => null,
                    _ => probe,
                    Resolve),
                Is.Null);
        }
    }

    [Test]
    public void Определение_предка_сохраняет_корень_диска_при_проверке_идентичности()
    {
        var seen = new List<string>();
        var rootIdentity = new SyncRoots.DirIdentity(100, 5, 0);

        SyncRoots.IdentityProbe Read(string path)
        {
            seen.Add(path);
            return string.Equals(path, @"C:\", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, @"\\localhost\C$", StringComparison.OrdinalIgnoreCase)
                ? SyncRoots.IdentityProbe.From(rootIdentity)
                : SyncRoots.IdentityProbe.Missing;
        }

        var refusal = SyncRoots.RefusalCore(
            @"\\localhost\C$\Data",
            @"C:\",
            static (path, _) => new(SyncRoots.FinalKind.Resolved, path),
            static _ => null,
            Read,
            static _ => []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refusal, Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(seen, Has.Member(@"C:\"));
            Assert.That(seen, Has.No.Member("C:"));
        }
    }
}
