using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class XamlBindingRuleTests
{
    private static readonly string[] ReadOnlyTargets = ["ProgressBar"];

    public static IEnumerable<string> MarkupFiles()
    {
        var views = Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf");

        if (!Directory.Exists(views))
        {
            Assert.Fail($"Каталог разметки не найден: {views}");
        }

        return Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(views, path))
            .Order(StringComparer.Ordinal);
    }

    [TestCaseSource(nameof(MarkupFiles))]
    public void Полоса_прогресса_привязана_только_на_чтение(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf", file);
        var document = XDocument.Load(path, LoadOptions.SetLineInfo);

        var offenders = document.Descendants()
            .Where(element => ReadOnlyTargets.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(static element => element.Attribute("Value"))
            .OfType<XAttribute>()
            .Where(static attribute => attribute.Value.StartsWith("{Binding", StringComparison.Ordinal))
            .Where(static attribute => !attribute.Value.Contains("Mode=OneWay", StringComparison.Ordinal))
            .Select(attribute => $"{file}:{((IXmlLineInfo)attribute).LineNumber} – {attribute.Value}")
            .ToList();

        Assert.That(offenders,
            Is.Empty,
            () => $"Привязка Value без Mode=OneWay бросает InvalidOperationException при отрисовке, если свойство только для чтения:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }
}
