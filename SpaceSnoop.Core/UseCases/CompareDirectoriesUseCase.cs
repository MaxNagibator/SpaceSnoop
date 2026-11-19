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
        var left = request.LeftPath.Trim();
        var right = request.RightPath.Trim();
        SyncRoots.EnsureDisjoint(left, right);

        var comparer = new DirectoryComparer(new(request.Exclusions), logger);
        var result = comparer.Compare(left, right, cancel, progress);
        result.ApplyMode(request.Mode, request.Mirror, request.Winner);

        return result;
    }
}
