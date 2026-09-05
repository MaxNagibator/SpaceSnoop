using Microsoft.Extensions.Logging;

namespace SpaceSnoop.Core.UseCases;

public sealed record CompareDirectoriesRequest(
    string LeftPath,
    string RightPath,
    string Exclusions,
    SyncMode Mode,
    SyncWinner Winner,
    bool Mirror);

public sealed class CompareDirectoriesUseCase(ILogger<DirectoryComparer> logger)
{
    public ComparisonResult Execute(CompareDirectoriesRequest request, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var comparer = new DirectoryComparer(new(request.Exclusions), logger);
        var result = comparer.Compare(request.LeftPath.Trim(), request.RightPath.Trim(), cancel, progress);
        result.ApplyMode(request.Mode, request.Mirror, request.Winner);

        return result;
    }
}
