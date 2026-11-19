using ModelContextProtocol;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed partial class McpScanTools
{
    private const string ArchiveTool = "archive_directory";

    internal static Func<string, bool> SupportsRecycleBin { get; set; } = RecycleBin.SupportsRecycleBin;

    public async Task<string> ArchiveDirectoryAsync(string path, bool deleteOriginal, bool dryRun, CancellationToken cancellationToken)
    {
        path = path.Trim();

        if (!dryRun)
        {
            McpGuards.RequireMutations(preferences, logger, ArchiveTool);
        }

        if (deleteOriginal && path.Length > 0)
        {
            await RequireArchiveRecycleBinAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var directory = McpDispatch.Run(() => ResolveArchiveDirectory(path));
        if (deleteOriginal && !string.Equals(path, directory.AbsolutePath, StringComparison.Ordinal))
        {
            await RequireArchiveRecycleBinAsync(directory.AbsolutePath, cancellationToken).ConfigureAwait(false);
        }

        var request = await Task.Run(() => scan.CreateArchiveRequest(directory, deleteOriginal), cancellationToken)
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        if (dryRun)
        {
            logger.McpToolInvoked(ArchiveTool, $"«{path}», план");

            McpDispatch.Run(() =>
            {
                RequireCurrentArchiveDirectory(path, directory);
                return true;
            });
            return BuildArchivePlan(request);
        }

        var run = McpDispatch.Run(() => PrepareArchiveRun(path, directory, request, cancellationToken));

        var outcome = await run.ConfigureAwait(false);

        return McpDispatch.Run(() =>
        {
            if (outcome.ArchivePath is null)
            {
                throw new McpException($"Архив не создан: {outcome.StatusText}");
            }

            return McpFormat.Serialize(
                new McpArchiveResult(
                    request.SourcePath,
                    outcome.ArchivePath,
                    request.EstimatedFiles,
                    outcome.OriginalDeleted,
                    outcome.StatusText,
                    state.ReadScanState()));
        });
    }

    private static async Task RequireArchiveRecycleBinAsync(string path, CancellationToken token)
    {
        if (!await Task.Run(() => SupportsRecycleBin(path), token).WaitAsync(token).ConfigureAwait(false))
        {
            throw new McpException($"Путь «{path}» не поддерживает корзину Windows – удаление оригинала без корзины через MCP запрещено.");
        }
    }

    private static string BuildArchivePlan(ArchiveRequest request)
    {
        return McpFormat.Serialize(
            new McpArchivePlan(
                request.SourcePath,
                request.TargetPath,
                request.EstimatedFiles,
                request.TotalBytes,
                SizeFormatter.Format(request.TotalBytes),
                request.DeleteOriginal));
    }

    private Task<ArchiveOutcome> PrepareArchiveRun(
        string path,
        DirectorySpace verifiedDirectory,
        ArchiveRequest request,
        CancellationToken cancellationToken)
    {
        if (scan.IsScanning)
        {
            logger.McpToolRejected(ArchiveTool, BusyReason);
            throw new McpException(ScanBusyMessage);
        }

        RequireCurrentArchiveDirectory(path, verifiedDirectory);
        logger.McpMutationRequested(
            ArchiveTool,
            $"«{request.SourcePath}» → «{request.TargetPath}», файлов ≈{request.EstimatedFiles}, оригинал в корзину {request.DeleteOriginal}");

        notifier.Notify($"Агент упаковывает в архив: {request.SourcePath}", StatusSeverity.Warning);

        return scan.ArchiveFromAutomationAsync(verifiedDirectory, request, cancellationToken);
    }

    private void RequireCurrentArchiveDirectory(string path, DirectorySpace verifiedDirectory)
    {
        if (!ReferenceEquals(ResolveArchiveDirectory(path), verifiedDirectory))
        {
            throw new McpException("Сканирование изменилось во время проверки пути. Повторите вызов.");
        }

    }

    private DirectorySpace ResolveArchiveDirectory(string path)
    {
        if (path.Length == 0)
        {
            throw new McpException("Путь к каталогу должен быть задан.");
        }

        if (scan.FindForAutomation(path) is not { } space)
        {
            throw new McpException(
                $"Каталог «{path}» не найден в открытом сканировании. Упаковывается только то, что уже отсканировано: откройте дерево через open_scan с scan=true.");
        }

        if (space is not DirectorySpace dir)
        {
            throw new McpException($"«{path}» – файл, а упаковать можно только каталог.");
        }

        if (scan.IsScanRoot(space) || dir.Parent is not DirectorySpace)
        {
            throw new McpException($"«{path}» – корень сканирования, архив некуда положить. Выберите подкаталог.");
        }

        return dir;
    }
}
