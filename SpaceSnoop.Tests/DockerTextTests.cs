using SpaceSnoop.Core.Docker;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DockerTextTests
{
    [TestCase("Images", "Образы")]
    [TestCase("Containers", "Контейнеры")]
    [TestCase("Local Volumes", "Тома")]
    [TestCase("Build Cache", "Кэш сборки")]
    [TestCase("Something Else", "Something Else")]
    public void Category_TranslatesKnownBuckets(string type, string expected)
    {
        Assert.That(DockerText.Category(type), Is.EqualTo(expected));
    }

    [TestCase("7 weeks ago", "7 недель назад")]
    [TestCase("18 hours ago", "18 часов назад")]
    [TestCase("1 day ago", "1 день назад")]
    [TestCase("2 days ago", "2 дня назад")]
    [TestCase("11 days ago", "11 дней назад")]
    [TestCase("21 hours ago", "21 час назад")]
    [TestCase("3 months ago", "3 месяца назад")]
    [TestCase("2 years ago", "2 года назад")]
    [TestCase("About a minute ago", "около минуты назад")]
    [TestCase("About an hour ago", "около часа назад")]
    [TestCase("Less than a second ago", "меньше секунды назад")]
    public void Age_TranslatesDockerDuration(string since, string expected)
    {
        Assert.That(DockerText.Age(since), Is.EqualTo(expected));
    }

    [TestCase("", "")]
    [TestCase("сегодня", "сегодня")]
    [TestCase("42 parsecs ago", "42 parsecs ago")]
    public void Age_UnknownFormat_StaysAsIs(string since, string expected)
    {
        Assert.That(DockerText.Age(since), Is.EqualTo(expected));
    }

    [TestCase("Up 2 hours", "Работает 2 часа")]
    [TestCase("Up About an hour", "Работает около часа")]
    [TestCase("Up 5 minutes (healthy)", "Работает 5 минут, здоров")]
    [TestCase("Up 3 days (unhealthy)", "Работает 3 дня, нездоров")]
    [TestCase("Up 4 hours (Paused)", "Работает 4 часа, на паузе")]
    [TestCase("Exited (0) 3 days ago", "Остановлен (0), 3 дня назад")]
    [TestCase("Exited (137) 2 days ago", "Остановлен (137), 2 дня назад")]
    [TestCase("Restarting (1) 5 seconds ago", "Перезапускается (1), 5 секунд назад")]
    [TestCase("Created", "Создан")]
    [TestCase("Paused", "На паузе")]
    [TestCase("Removal In Progress", "Удаляется")]
    [TestCase("", "")]
    [TestCase("Weird status", "Weird status")]
    public void Status_TranslatesContainerState(string status, string expected)
    {
        Assert.That(DockerText.Status(status), Is.EqualTo(expected));
    }

    [TestCase("ce337cbe340c74c43d96e5191279aceae151c1f56c81570b52ded6bed85dfdd3", "ce337cbe340c")]
    [TestCase("data-vol", "data-vol")]
    [TestCase("0d4ccd469bdd", "0d4ccd469bdd")]
    public void ShortName_TrimsAnonymousVolumeOnly(string name, string expected)
    {
        Assert.That(DockerText.ShortName(name), Is.EqualTo(expected));
    }
}
