using KeepShell.Services.Platform;
using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ArchiveProgressDialogFactory(
    ArchiveService service,
    OperationPreferences operations,
    IUiDispatcher uiDispatcher,
    ILogger<ArchiveProgressDialogViewModel> logger)
{
    public ArchiveProgressDialogViewModel Create(DirectorySpace dir)
    {
        return Create(CreateRequest(dir, operations.DeleteOriginalAfterArchive, interactive: true));
    }

    public ArchiveProgressDialogViewModel Create(ArchiveRequest request)
    {
        return new(request, service, uiDispatcher, logger);
    }

    public ArchiveRequest CreateRequest(DirectorySpace dir, bool deleteOriginal, bool interactive)
    {
        var source = dir.AbsolutePath;

        return new(source,
            UniqueZipPath(source),
            dir.TotalFileCount,
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
}
