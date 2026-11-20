using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class NetworkDiscoveryTests
{
    [SetUp]
    public void SetUp()
    {
        NetworkDiscovery.ResetForTests();
        NetworkDiscovery.PersistentReader = static () => [];
        NetworkDiscovery.MountPointsReader = static () => [];
        NetworkDiscovery.ActiveConnectionsReader = static () => [];
    }

    [TearDown]
    public void TearDown()
    {
        NetworkDiscovery.ResetForTests();
        NetworkShares.Enumerator = DefaultSharesEnumerator();
    }

    [TestCase("##nas#media", @"\\nas\media")]
    [TestCase("##192.168.1.20#public", @"\\192.168.1.20\public")]
    [TestCase("##nas#media#sub", @"\\nas\media")]
    [TestCase("##nas", null)]
    [TestCase("##", null)]
    [TestCase("nas#media", null)]
    public void Ключи_MountPoints2_разбираются_в_корни_UNC_шар(string keyName, string? expected)
    {
        Assert.That(NetworkDiscovery.ParseMountPointKey(keyName), Is.EqualTo(expected));
    }

    [Test]
    public void Хосты_и_шары_сливаются_из_уровня_1_без_дубликатов_и_без_сети()
    {
        NetworkDiscovery.Remember(@"\\nas\media\movies");
        NetworkDiscovery.Remember(@"\\NAS\Archive");
        NetworkDiscovery.PersistentReader = static () => [@"\\backup\daily", @"\\nas\Media"];
        NetworkDiscovery.MountPointsReader = static () => [@"\\synology\photos", @"\\nas\docs"];

        var hosts = NetworkDiscovery.ListHosts();
        var nasShares = NetworkDiscovery.ListKnownShares("NAS");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hosts, Is.EqualTo(new[] { "backup", "nas", "synology" }));
            Assert.That(nasShares, Is.EqualTo(new[] { "Archive", "docs", "media" }));
        }
    }

    [Test]
    public async Task Активные_подключения_уровня_2_кэшируются_на_TTL_и_обновляются_после_истечения()
    {
        long nowMs = 100_000;
        var calls = 0;
        IReadOnlyList<string> active = [@"\\filesrv\share1"];

        NetworkDiscovery.ClockMs = () => Volatile.Read(ref nowMs);
        NetworkDiscovery.ActiveConnectionsReader = () =>
        {
            Interlocked.Increment(ref calls);
            return active;
        };

        await NetworkDiscovery.RefreshActiveAsync();
        Assert.That(NetworkDiscovery.ListHosts(), Is.EqualTo(new[] { "filesrv" }));
        Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));

        Volatile.Write(ref nowMs, 100_000 + ((AppDefaults.NetworkDiscoveryCacheSeconds - 1) * 1000L));
        active = [@"\\filesrv\share1", @"\\newbox\drop"];

        Assert.That(NetworkDiscovery.ListHosts(), Is.EqualTo(new[] { "filesrv" }));
        await Task.Delay(40);
        Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));

        Volatile.Write(ref nowMs, 100_000 + (AppDefaults.NetworkDiscoveryCacheSeconds * 1000L));
        using var refreshed = new ManualResetEventSlim();
        _ = NetworkDiscovery.ListHosts(() => refreshed.Set());

        Assert.That(refreshed.Wait(TimeSpan.FromSeconds(5)), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Volatile.Read(ref calls), Is.EqualTo(2));
            Assert.That(NetworkDiscovery.ListHosts(), Is.EqualTo(new[] { "filesrv", "newbox" }));
        }
    }

    [Test]
    public void Отказ_NetShareEnum_дополняется_известными_шарами_сервера()
    {
        var previous = NetworkShares.Enumerator;
        NetworkShares.Enumerator = static _ => [];
        NetworkDiscovery.Remember(@"\\nas\backups");
        NetworkDiscovery.MountPointsReader = static () => [@"\\nas\media"];

        try
        {
            var enumerated = NetworkShares.List("nas");
            var merged = NetworkDiscovery.MergeShares("nas", enumerated);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(enumerated, Is.Empty);
                Assert.That(merged, Is.EqualTo(new[] { "backups", "media" }));
            }
        }
        finally
        {
            NetworkShares.Enumerator = previous;
        }
    }

    private static Func<string, IReadOnlyList<string>> DefaultSharesEnumerator()
    {
        return NetworkShares.Enumerator;
    }
}
