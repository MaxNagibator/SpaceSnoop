using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ArchiveProgressDialogFactory(
    ArchiveService service,
    OperationPreferences operations,
    ILogger<ArchiveProgressDialogViewModel> logger)
{
    public ArchiveProgressDialogViewModel Create(DirectorySpace dir)
    {
        return Create(CreateRequest(dir, operations.DeleteOriginalAfterArchive, interactive: true));
    }

    public ArchiveProgressDialogViewModel Create(ArchiveRequest request)
    {
        return new(request, service, logger);
    }

    public ArchiveRequest CreateRequest(DirectorySpace dir, bool deleteOriginal, bool interactive)
    {
        var source = dir.AbsolutePath;
        var files = new List<string>();
        Collect(dir, files);

        return new(source,
            UniqueZipPath(source),
            files,
            dir.TotalSize,
            deleteOriginal,
            operations.ArchiveCompression,
            interactive);
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
