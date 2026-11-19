namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal readonly record struct ScanMarkScopeSnapshot(SpaceBase Root, SpaceBase Scope, int Count, long Bytes);

internal readonly record struct ScanMarkSnapshot(SpaceBase Root, SpaceBase Scope, int Count, long Bytes)
{
    private readonly IReadOnlyList<ScanMarkScopeSnapshot>? _scopes;

    public IReadOnlyList<ScanMarkScopeSnapshot> Scopes => _scopes ?? (Root is not null ? [new(Root, Scope, Count, Bytes)] : []);

    public ScanMarkSnapshot(IReadOnlyList<ScanMarkScopeSnapshot> scopes)
        : this(
            scopes.Count > 0 ? scopes[0].Root : null!,
            scopes.Count > 0 ? scopes[0].Scope : null!,
            scopes.Sum(static s => s.Count),
            scopes.Sum(static s => s.Bytes))
    {
        _scopes = scopes;
    }
}
