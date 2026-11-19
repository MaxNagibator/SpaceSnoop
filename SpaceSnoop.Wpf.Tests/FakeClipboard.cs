using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeClipboard : IClipboardService
{
    public bool Succeeds { get; set; } = true;

    public string? LastText { get; private set; }

    public bool TrySetText(string? text)
    {
        LastText = text;

        return Succeeds;
    }
}
