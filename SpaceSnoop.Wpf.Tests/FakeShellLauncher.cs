using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeShellLauncher : IShellLauncher
{
    public bool Succeeds { get; set; } = true;

    public List<string> Opened { get; } = [];

    public List<string> Revealed { get; } = [];

    public List<string> Started { get; } = [];

    public bool Open(string pathOrUrl)
    {
        Opened.Add(pathOrUrl);

        return Succeeds;
    }

    public bool Reveal(string path)
    {
        Revealed.Add(path);

        return Succeeds;
    }

    public bool Start(string executable, params string[] arguments)
    {
        Started.Add(string.Join(' ', [executable, .. arguments]));

        return Succeeds;
    }
}
