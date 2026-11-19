using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AppInfoTests
{
    [Test]
    public void Version_ResolvesToNumberWithoutBuildMetadata()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AppInfo.Version, Does.Not.Contain("+"));
            Assert.That(Version.TryParse(AppInfo.Version.Split('-')[0], out _), Is.True, AppInfo.Version);
        }
    }
}
