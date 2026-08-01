using SpaceSnoop.Core.Docker;
using SpaceSnoop.Wpf.ViewModels.Docker;
using System.Globalization;

namespace SpaceSnoop.Wpf.Tests;

public sealed class DockerBucketViewModelTests
{
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [SetUp]
    public void SetUp()
    {
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("ru-RU");
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _culture;
    }

    [Test]
    public void Категория_и_размеры_переводятся_в_формат_приложения()
    {
        var bucket = new DockerBucketViewModel(new("Images", 4, 1, "2.987GB", "16.14MB (0%)"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bucket.Title, Is.EqualTo("Образы"));
            Assert.That(bucket.Count, Is.EqualTo(4));
            Assert.That(bucket.SizeText, Is.EqualTo("2,8 ГБ"));
            Assert.That(bucket.ReclaimText, Is.EqualTo("15,4 МБ (0,5 %)"));
        }
    }

    [Test]
    public void Нечего_возвращать_показывается_нулём_без_доли()
    {
        var bucket = new DockerBucketViewModel(new("Local Volumes", 2, 2, "224.4MB", "0B (0%)"));

        Assert.That(bucket.ReclaimText, Is.EqualTo("0 байт"));
    }

    [Test]
    public void Возврат_всего_объёма_даёт_сто_процентов()
    {
        var bucket = new DockerBucketViewModel(new("Containers", 3, 0, "2.942MB", "2.942MB (100%)"));

        Assert.That(bucket.ReclaimText, Is.EqualTo("2,8 МБ (100 %)"));
    }

    [TestCase("16.14MB (0%)", "16.14MB")]
    [TestCase("17.77GB", "17.77GB")]
    [TestCase("  0B (0%) ", "0B")]
    public void Доля_в_скобках_отрезается_перед_разбором(string reclaimable, string expected)
    {
        Assert.That(DockerBucketViewModel.SizePart(reclaimable), Is.EqualTo(expected));
    }

    [Test]
    public void Полоса_меряется_самой_крупной_категорией()
    {
        var buckets = DockerBucketViewModel.Build(
        [
            new("Images", 4, 1, "2.987GB", "16.14MB (0%)"),
            new("Build Cache", 393, 0, "20.06GB", "17.77GB"),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buckets[1].Fraction, Is.EqualTo(1).Within(0.001));
            Assert.That(buckets[0].Fraction, Is.EqualTo(0.149).Within(0.001));
            Assert.That(buckets[1].ReclaimFraction, Is.EqualTo(0.886).Within(0.001));
        }
    }

    [Test]
    public void Возвращаемое_никогда_не_длиннее_самой_полосы()
    {
        var buckets = DockerBucketViewModel.Build([new("Containers", 3, 0, "2.942MB", "2.942MB (100%)")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(buckets[0].Fraction, Is.EqualTo(1).Within(0.001));
            Assert.That(buckets[0].ReclaimFraction, Is.LessThanOrEqualTo(buckets[0].Fraction));
            Assert.That(buckets[0].HasReclaim, Is.True);
        }
    }

    [Test]
    public void Пустой_снимок_не_ломает_доли()
    {
        Assert.That(DockerBucketViewModel.Build([]), Is.Empty);
    }
}
