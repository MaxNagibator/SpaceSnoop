using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ScanProgressTests
{
    [Test]
    public void Колбэк_готовности_тома_срабатывает_однократно_и_сбрасывается_через_null()
    {
        var progress = new ScanProgress();
        var calls = 0;

        progress.OnVolumeReady(() => calls++);
        progress.NotifyVolumeReady();
        progress.NotifyVolumeReady();

        Assert.That(calls, Is.EqualTo(1));

        progress.OnVolumeReady(() => calls++);
        progress.OnVolumeReady(null);
        progress.NotifyVolumeReady();

        Assert.That(calls, Is.EqualTo(1));
    }
}
