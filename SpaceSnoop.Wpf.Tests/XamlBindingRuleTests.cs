using KeepShell.Testing.Rules;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class XamlBindingRuleTests
{
    [Test]
    public void Pack_URI_несёт_имя_сборки()
    {
        var frontend = Frontend();
        var offenders = MarkupFiles(frontend)
            .SelectMany(file => MarkupRules.PackUris(file, frontend))
            .ToList();

        Assert.That(
            offenders,
            Is.Empty,
            () => $"Запись без ;component/ резолвится через Application.ResourceAssembly, а в тест-процессе это testhost – ресурс не находится:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }

    [Test]
    public void Полоса_прогресса_привязана_только_на_чтение()
    {
        var frontend = Frontend();
        var offenders = MarkupFiles(frontend)
            .SelectMany(file => MarkupRules.ReadOnlyBindings(file, frontend))
            .ToList();

        Assert.That(
            offenders,
            Is.Empty,
            () => $"Привязка Value без Mode=OneWay бросает InvalidOperationException при отрисовке, если свойство только для чтения:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }

    private static IEnumerable<string> MarkupFiles(string frontend)
    {
        if (!Directory.Exists(frontend))
        {
            Assert.Fail($"Каталог разметки не найден: {frontend}");
        }

        return ProjectFiles.Enumerate(frontend, "*.xaml");
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
