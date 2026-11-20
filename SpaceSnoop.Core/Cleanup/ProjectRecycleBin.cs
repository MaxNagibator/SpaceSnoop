namespace SpaceSnoop.Core.Cleanup;

internal static class ProjectRecycleBin
{
    internal static void Delete(string path)
    {
        RecycleBin.RecycleWithFileOperation([Path.GetFullPath(path)]);
    }
}
