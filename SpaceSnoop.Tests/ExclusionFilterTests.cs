using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ExclusionFilterTests
{
    [Test]
    public void EmptyFilter_ExcludesNothing()
    {
        var filter = new ExclusionFilter("");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.IsExcluded("file.txt"), Is.False);
            Assert.That(filter.IsExcluded(".git"), Is.False);
        }
    }

    [Test]
    public void WildcardFilter_ExcludesByExtension()
    {
        var filter = new ExclusionFilter("*.tmp, *.bak");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.IsExcluded("data.tmp"), Is.True);
            Assert.That(filter.IsExcluded("backup.bak"), Is.True);
            Assert.That(filter.IsExcluded("readme.txt"), Is.False);
        }
    }

    [Test]
    public void ExactNameFilter_ExcludesByName()
    {
        var filter = new ExclusionFilter(".git, node_modules");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.IsExcluded(".git"), Is.True);
            Assert.That(filter.IsExcluded("node_modules"), Is.True);
            Assert.That(filter.IsExcluded(".github"), Is.False);
        }
    }

    [Test]
    public void Filter_IsCaseInsensitive()
    {
        var filter = new ExclusionFilter("*.TMP");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.IsExcluded("data.tmp"), Is.True);
            Assert.That(filter.IsExcluded("DATA.TMP"), Is.True);
        }
    }

    [Test]
    public void Filter_TrimsWhitespace()
    {
        var filter = new ExclusionFilter("  *.tmp ,  *.bak  ");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(filter.IsExcluded("file.tmp"), Is.True);
            Assert.That(filter.IsExcluded("file.bak"), Is.True);
        }
    }
}
