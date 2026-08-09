using KeepShell.Testing.Rules;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class XamlBindingRuleTests
{
    public static IEnumerable<string> MarkupFiles()
    {
        var views = Frontend();

        if (!Directory.Exists(views))
        {
            Assert.Fail($"Каталог разметки не найден: {views}");
        }

        return ProjectFiles.Enumerate(views, "*.xaml").Select(path => Path.GetRelativePath(views, path));
    }

    [TestCaseSource(nameof(MarkupFiles))]
    public void Полоса_прогресса_привязана_только_на_чтение(string file)
    {
        var offenders = MarkupRules.ReadOnlyBindings(Path.Combine(Frontend(), file), Frontend());

        Assert.That(offenders,
            Is.Empty,
            () => $"Привязка Value без Mode=OneWay бросает InvalidOperationException при отрисовке, если свойство только для чтения:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }

    [TestCaseSource(nameof(MarkupFiles))]
    public void Pack_URI_несёт_имя_сборки(string file)
    {
        var offenders = MarkupRules.PackUris(Path.Combine(Frontend(), file), Frontend());

        Assert.That(offenders,
            Is.Empty,
            () => $"Запись без ;component/ резолвится через Application.ResourceAssembly, а в тест-процессе это testhost – ресурс не находится:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }

    private static string Frontend()
    {
        return Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf");
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }
}
