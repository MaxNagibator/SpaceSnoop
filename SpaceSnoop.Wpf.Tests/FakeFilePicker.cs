using KeepShell.Services.Platform;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class FakeFilePicker : IFilePicker
{
    public string? Folder { get; set; }

    public string? SavePath { get; set; }

    public List<string> Titles { get; } = [];

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        Titles.Add(title);

        return Folder;
    }

    public string? SaveFile(FileSaveRequest request)
    {
        Titles.Add(request.Title);

        return SavePath;
    }
}
