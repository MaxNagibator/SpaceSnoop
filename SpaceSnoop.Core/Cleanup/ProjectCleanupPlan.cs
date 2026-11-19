namespace SpaceSnoop.Core.Cleanup;

public sealed class ProjectCleanupPlan
{
    internal int Used;
    public string Root => Roots.Count > 0 ? Roots[0] : CustomOutputs[0];
    public IReadOnlyList<string> Roots { get; }
    public IReadOnlyList<string> CustomOutputs { get; }
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
    public IReadOnlyList<ProjectArtifactScan> Artifacts { get; }
    public IReadOnlyList<string> Errors { get; }

    internal Guid Owner { get; }
    internal IReadOnlyList<CacheFileStamp> RootStamps { get; }

    internal ProjectCleanupPlan(
        Guid owner,
        IReadOnlyList<string> roots,
        IReadOnlyList<string> customOutputs,
        IReadOnlyList<CacheFileStamp> stamps,
        IReadOnlyList<ProjectArtifactScan> artifacts,
        IReadOnlyList<string> errors)
    {
        Owner = owner;
        Roots = roots;
        CustomOutputs = customOutputs;
        RootStamps = stamps;
        Artifacts = artifacts;
        Errors = errors;
    }
}

public sealed class ProjectArtifactScan
{
    public ProjectArtifactKind Kind { get; }
    public string Id { get; }
    public string ProjectDirectory { get; }
    public IReadOnlyList<ProjectArtifactFile> Contents { get; }
    public string Path { get; }
    public long Bytes => Entries.Sum(file => file.Length);
    public int Files => Entries.Count;
    public IReadOnlyList<string> Errors { get; }
    public bool CanClean => Errors.Count == 0 && Files > 0;
    internal CacheFileStamp Root { get; }
    internal IReadOnlyList<CacheFileStamp> Entries { get; }

    internal ProjectArtifactScan(
        ProjectArtifactKind kind,
        string path,
        CacheFileStamp root,
        IReadOnlyList<CacheFileStamp> files,
        IReadOnlyList<string> errors,
        string projectDirectory)
    {
        Kind = kind;
        Path = path;
        Root = root;
        Entries = files;
        Errors = errors;
        ProjectDirectory = projectDirectory;
        Id = kind + "|" + path;
        Contents = files.Select(file => new ProjectArtifactFile(file.Path, file.Length)).ToList().AsReadOnly();
    }
}

public sealed record ProjectArtifactFile(string Path, long Bytes);
