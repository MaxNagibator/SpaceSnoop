using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class StorageMediaTests
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Повторные_проверки_медленного_тома_делят_одну_пробу_в_полёте()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var timeout = TimeSpan.FromMilliseconds(50);

        var probe = new StorageMedia.VolumeProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return StorageKind.Rotational;
            });

        try
        {
            var first = probe.LimitParallelismAsync(@"Z:\first", 8, timeout);
            await entered.Task.WaitAsync(Ceiling);

            Assert.That(await first.WaitAsync(Ceiling), Is.EqualTo(8));
            Assert.That(await probe.LimitParallelismAsync(@"z:\second", 8, timeout).WaitAsync(Ceiling), Is.EqualTo(8));
        }
        finally
        {
            release.SetResult();
        }

        var detected = await Task.Run(() => probe.Detect(@"Z:\third")).WaitAsync(Ceiling);

        Assert.That(detected, Is.EqualTo(StorageKind.Rotational));
        Assert.That(await probe.LimitParallelismAsync(@"Z:\fourth", 8, timeout).WaitAsync(Ceiling), Is.EqualTo(1));
        Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));
    }

    [Test]
    public async Task Сетевой_UNC_и_сетевой_диск_определяются_как_Network_и_ограничиваются_потолком_8()
    {
        var probe = new StorageMedia.VolumeProbe(
            static _ => StorageKind.SolidState,
            static letter => string.Equals(letter, "Z:", StringComparison.OrdinalIgnoreCase));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(probe.Detect(@"\\nas\media\movies"), Is.EqualTo(StorageKind.Network));
            Assert.That(probe.Detect(@"//nas/media"), Is.EqualTo(StorageKind.Network));
            Assert.That(probe.Detect(@"\\?\UNC\nas\media"), Is.EqualTo(StorageKind.Network));
            Assert.That(probe.Detect(@"Z:\projects"), Is.EqualTo(StorageKind.Network));
            Assert.That(probe.Detect(@"C:\projects"), Is.EqualTo(StorageKind.SolidState));

            Assert.That(StorageMedia.LimitParallelism(@"\\nas\media", 16), Is.EqualTo(8));
            Assert.That(StorageMedia.LimitParallelism(@"\\nas\media", 4), Is.EqualTo(4));
            Assert.That(StorageMedia.LimitParallelism(@"\\nas\media", 0), Is.EqualTo(0));
            Assert.That(StorageMedia.ApplyLimit(StorageKind.Rotational, 8), Is.EqualTo(1));
        }

        Assert.That(await probe.LimitParallelismAsync(@"Z:\projects", 16, Ceiling), Is.EqualTo(8));
        Assert.That(await probe.LimitParallelismAsync(@"Z:\projects", 4, Ceiling), Is.EqualTo(4));
        Assert.That(await probe.LimitParallelismAsync(@"\\nas\media", 16, Ceiling), Is.EqualTo(8));
    }
}
