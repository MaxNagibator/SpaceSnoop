namespace SpaceSnoop.Core.Cleanup;

public sealed record CleanupTarget
{
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(24);

    private readonly string _path = string.Empty;
    private readonly IReadOnlyList<string> _paths = [];
    private readonly bool _hasExplicitPaths;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required CleanupTargetKind Kind { get; init; }

    public string Path
    {
        get => _path;
        init
        {
            _path = value;
            if (!_hasExplicitPaths)
            {
                _paths = string.IsNullOrEmpty(value) ? [] : [value];
            }
        }
    }

    public IReadOnlyList<string> Paths
    {
        get => _paths;
        init
        {
            _paths = value;
            _hasExplicitPaths = value.Count > 0;
            if (string.IsNullOrEmpty(_path) && value.Count > 0)
            {
                _path = value[0];
            }
        }
    }

    public string SearchPattern { get; init; } = "*";

    public bool Recursive { get; init; } = true;

    public bool Supported { get; init; } = true;

    public TimeSpan MinimumAge { get; init; } = DefaultMinimumAge;

    public bool IsIndivisible => Kind == CleanupTargetKind.RecycleBin;
}
