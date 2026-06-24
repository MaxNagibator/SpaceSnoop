using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ArchiveProgressDialogFactory(
    ArchiveService service,
    OperationPreferences operations,
    ILogger<ArchiveProgressDialogViewModel> logger)
{
    public ArchiveProgressDialogViewModel Create(DirectorySpace dir)
    {
        var source = dir.AbsolutePath;
        var files = new List<string>();
        Collect(dir, files);

        var request = new ArchiveRequest(source,
            UniqueZipPath(source),
            files,
            dir.TotalSize,
            operations.DeleteOriginalAfterArchive,
            operations.ArchiveCompression);

        return new(request, service, logger);
    }

    private static string UniqueZipPath(string source)
    {
        var target = source + ".zip";
        var index = 2;

        while (File.Exists(target))
        {
            target = $"{source} ({index++}).zip";
        }

        return target;
    }

    private static void Collect(DirectorySpace dir, List<string> files)
    {
        foreach (var file in dir.Files)
        {
            files.Add(file.AbsolutePath);
        }

        foreach (var sub in dir.SubDirectories)
        {
            Collect(sub, files);
        }
    }
}
