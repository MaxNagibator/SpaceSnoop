using KeepShell.Testing.Rules;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ArchitectureRuleTests
{
    private const int ThresholdLines = 500;

    private static readonly string[] PipelineTypes = ["DirectoryComparer", "SyncEngine"];

    private static readonly Regex[] PipelineConstruction =
    [
        .. PipelineTypes.Select(static type => new Regex($@"new\s+(?:[\w.]+\.)?{type}\s*[({{]", RegexOptions.Compiled)),
        .. PipelineTypes.Select(static type => new Regex($@"\b{type}\s+\w+\s*=\s*new\s*[({{]", RegexOptions.Compiled)),
    ];

    [Test]
    public void Конвейер_сравнения_и_синхронизации_собирается_только_в_use_case()
    {
        var offenders = SourceFiles()
            .Where(static file => !IsUseCase(file) && !IsTestProject(file))
            .SelectMany(file => Matches(file, PipelineConstruction))
            .ToList();

        Assert.That(offenders,
            Is.Empty,
            () => $"Сборка конвейера вне SpaceSnoop.Core/UseCases возвращает дублирование, ради устранения которого use case'ы и заводились:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Test]
    public void Модели_не_зовут_платформу_мимо_шлюзов()
    {
        var models = Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf", "ViewModels");
        var offenders = PlatformCallRule.Check(ProjectFiles.Enumerate(models, "*.cs"), RepositoryRoot());

        Assert.That(offenders,
            Is.Empty,
            () => $"Платформа доступна моделям только через шлюзы KeepShell.Services.Platform – прямой вызов не подменить дублем в тесте:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }

    [Test]
    public void Фронтенд_не_ссылается_на_проект_WinForms()
    {
        var offenders = BuildFilesOf("SpaceSnoop.Wpf")
            .Where(static path => File.ReadAllText(path).Contains("SpaceSnoop.csproj", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path))
            .ToList();

        Assert.That(offenders,
            Is.Empty,
            () => $"WPF работает поверх SpaceSnoop.Core; ссылка на WinForms-проект вернула бы в него System.Windows.Forms и три WinForms-завязанных типа:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Test]
    public void Модели_сценарии_и_разметка_держатся_под_порогом_в_500_строк()
    {
        var offenders = ThresholdFiles()
            .Select(static path => (Path: path, Lines: File.ReadAllLines(path).Length))
            .Where(static file => file.Lines > ThresholdLines)
            .Select(file => $"{Path.GetRelativePath(RepositoryRoot(), file.Path)} – {file.Lines}")
            .ToList();

        Assert.That(offenders,
            Is.Empty,
            () => $"Порог в {ThresholdLines} строк держит декомпозицию от отката: следующая обязанность идёт в новую модель, а не в эти файлы:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static IEnumerable<string> ThresholdFiles()
    {
        var frontend = Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf");

        return Directory.EnumerateFiles(Path.Combine(frontend, "ViewModels"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(frontend, "Mcp"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(frontend, "*.xaml", SearchOption.AllDirectories))
            .Where(static path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal);
    }

    private static IEnumerable<string> BuildFilesOf(string project)
    {
        var root = RepositoryRoot();

        string[] candidates =
        [
            Path.Combine(root, project, project + ".csproj"),
            Path.Combine(root, project, "Directory.Build.props"),
            Path.Combine(root, project, "Directory.Build.targets"),
            Path.Combine(root, "Directory.Build.props"),
            Path.Combine(root, "Directory.Build.targets"),
        ];

        return candidates.Where(File.Exists);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = RepositoryRoot();

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsUseCase(string path)
    {
        return path.Contains($"SpaceSnoop.Core{Path.DirectorySeparatorChar}UseCases{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool IsTestProject(string path)
    {
        return path.Contains(".Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Matches(string file, Regex[] patterns)
    {
        var relative = Path.GetRelativePath(RepositoryRoot(), file);
        var lines = File.ReadAllLines(file);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (Array.Exists(patterns, pattern => pattern.IsMatch(line)))
            {
                yield return $"{relative}:{index + 1} – {line.Trim()}";
            }
        }
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }
}
