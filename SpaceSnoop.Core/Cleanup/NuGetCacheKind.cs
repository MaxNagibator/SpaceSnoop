namespace SpaceSnoop.Core.Cleanup;

public enum NuGetCacheKind
{
    None = 0,
    HttpCache = 1,
    Temp = 2,
    PluginsCache = 3,
    GlobalPackages = 4,
}
