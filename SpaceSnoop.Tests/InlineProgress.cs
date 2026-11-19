using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

internal sealed class InlineProgress(Action<OperationProgress> onReport) : IProgress<OperationProgress>
{
    public void Report(OperationProgress value)
    {
        onReport(value);
    }
}
