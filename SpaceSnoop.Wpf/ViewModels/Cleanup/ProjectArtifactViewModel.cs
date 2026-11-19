using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class ProjectArtifactViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public ProjectArtifactKind Kind { get; }
    public string Id { get; }
    public string Path { get; }
    public string ProjectDirectory { get; }
    public IReadOnlyList<ProjectArtifactFile> Contents { get; }
    public long ProjectBytes { get; }
    public long Bytes { get; }
    public int Files { get; }
    public bool CanSelect { get; }
    public string ErrorText { get; }
    public bool HasError => ErrorText.Length > 0;
    public bool HasContents => Contents.Count > 0;
    public string SizeText => SizeFormatter.Format(Bytes);
    public string ProjectSizeText => SizeFormatter.Format(ProjectBytes);
    public string FilesText => $"Файлов: {Files:N0}";

    public string Name => Kind switch
    {
        ProjectArtifactKind.Bin => "bin · результаты сборки",
        ProjectArtifactKind.Obj => "obj · промежуточные файлы",
        ProjectArtifactKind.TestResults => "TestResults · результаты тестов",
        ProjectArtifactKind.Benchmarks => "BenchmarkDotNet · результаты замеров",
        ProjectArtifactKind.Coverage => "Coverage · отчёт покрытия",
        ProjectArtifactKind.CustomOutput => "Нестандартный output",
        _ => string.Empty,
    };

    public string Hint => Kind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks or ProjectArtifactKind.Coverage
        ? "Отчёты могут быть нужны для сравнения и диагностики. Выбирайте отдельно."
        : string.Empty;

    public ProjectArtifactViewModel(ProjectArtifactScan scan, long projectBytes = 0)
        : this(
            scan.Kind,
            scan.Path,
            scan.Bytes,
            scan.Files,
            scan.CanClean,
            string.Join(Environment.NewLine, scan.Errors),
            scan.ProjectDirectory,
            scan.Contents,
            scan.Id,
            projectBytes)
    {
    }

    internal ProjectArtifactViewModel(
        ProjectArtifactKind kind,
        string path,
        long bytes,
        int files,
        bool canSelect,
        string error,
        string? projectDirectory = null,
        IReadOnlyList<ProjectArtifactFile>? contents = null,
        string? id = null,
        long projectBytes = 0)
    {
        Kind = kind;
        Id = id ?? kind + "|" + path;
        Path = path;
        Bytes = bytes;
        Files = files;
        CanSelect = canSelect;
        ErrorText = error;
        ProjectDirectory = projectDirectory ?? Directory.GetParent(path)?.FullName ?? path;
        Contents = contents ?? [];
        ProjectBytes = projectBytes == 0 ? bytes : projectBytes;
    }
}
