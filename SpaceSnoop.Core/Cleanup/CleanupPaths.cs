namespace SpaceSnoop.Core.Cleanup;

internal static class CleanupPaths
{
    internal static IEnumerable<string> ProtectedRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(home, ".dotnet"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty, Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)") ?? string.Empty,
            Path.Combine(Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") ?? home, ".dotnet"), AppContext.BaseDirectory,
        }.Where(path => !string.IsNullOrWhiteSpace(path));
    }
}
