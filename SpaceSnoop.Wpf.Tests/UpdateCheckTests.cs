using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class UpdateCheckTests
{
    [TestCase("v2.9.0", "2.8.16", true)]
    [TestCase("2.9.0", "2.8.16", true)]
    [TestCase("v2.8.17", "2.8.16", true)]
    [TestCase("v2.8.16", "2.8.16", false)]
    [TestCase("v2.8.2", "2.8.16", false)]
    [TestCase("v2.7.99", "2.8.0", false)]
    [TestCase("v2.9.0-beta", "2.8.16", true)]
    [TestCase(null, "2.8.16", false)]
    [TestCase("", "2.8.16", false)]
    [TestCase("мусор", "2.8.16", false)]
    [TestCase("v2.9.0", "хлам", false)]
    public void Релиз_новее_текущей_версии_только_при_большем_номере(string? latestTag, string current, bool expected)
    {
        Assert.That(UpdateCheck.IsNewer(latestTag, current), Is.EqualTo(expected));
    }
}
