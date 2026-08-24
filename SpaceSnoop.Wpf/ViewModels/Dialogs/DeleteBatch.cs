using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

internal readonly record struct DeleteBatchCallbacks(
    Func<string, bool> Exists,
    Action<IReadOnlyList<string>> DeleteChunk,
    Action<string> DeleteSingle);

internal readonly record struct DeleteItemResult(int Index, DeleteItemStatus Status, Exception? Failure);

internal static class DeleteBatch
{
    internal static void Run(
        IReadOnlyList<string> paths,
        int chunkSize,
        in DeleteBatchCallbacks callbacks,
        Action<int> onStarting,
        Action<DeleteItemResult> onResult,
        CancellationToken token)
    {
        var size = Math.Max(1, chunkSize);
        var pending = new List<int>(size);

        for (var start = 0; start < paths.Count; start += size)
        {
            token.ThrowIfCancellationRequested();

            pending.Clear();
            var end = Math.Min(start + size, paths.Count);

            for (var index = start; index < end; index++)
            {
                onStarting(index);

                if (callbacks.Exists(paths[index]))
                {
                    pending.Add(index);
                }
                else
                {
                    onResult(new(index, DeleteItemStatus.Missing, null));
                }
            }

            if (pending.Count == 0)
            {
                continue;
            }

            RunChunk(paths, pending, callbacks, onResult, token);
        }

        token.ThrowIfCancellationRequested();
    }

    private static void RunChunk(
        IReadOnlyList<string> paths,
        List<int> pending,
        in DeleteBatchCallbacks callbacks,
        Action<DeleteItemResult> onResult,
        CancellationToken token)
    {
        try
        {
            callbacks.DeleteChunk(Collect(paths, pending));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (pending.Count == 1)
            {
                onResult(ClassifyFailure(pending[0], paths[pending[0]], exception, callbacks));
                return;
            }

            RetryOneByOne(paths, pending, callbacks, onResult, token);
            return;
        }

        foreach (var index in pending)
        {
            onResult(new(index, DeleteItemStatus.Removed, null));
        }
    }

    private static void RetryOneByOne(
        IReadOnlyList<string> paths,
        List<int> pending,
        in DeleteBatchCallbacks callbacks,
        Action<DeleteItemResult> onResult,
        CancellationToken token)
    {
        for (var position = 0; position < pending.Count; position++)
        {
            if (token.IsCancellationRequested)
            {
                ReportUnprocessed(paths, pending, position, callbacks, onResult);
                token.ThrowIfCancellationRequested();
            }

            var index = pending[position];

            try
            {
                if (callbacks.Exists(paths[index]))
                {
                    callbacks.DeleteSingle(paths[index]);
                }

                onResult(new(index, DeleteItemStatus.Removed, null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                onResult(ClassifyFailure(index, paths[index], exception, callbacks));
            }
        }
    }

    private static DeleteItemResult ClassifyFailure(
        int index,
        string path,
        Exception exception,
        in DeleteBatchCallbacks callbacks)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(index, DeleteItemStatus.Missing, null);
        }

        return callbacks.Exists(path)
            ? new(index, DeleteItemStatus.Failed, exception)
            : new(index, DeleteItemStatus.Removed, null);
    }

    private static void ReportUnprocessed(
        IReadOnlyList<string> paths,
        List<int> pending,
        int position,
        in DeleteBatchCallbacks callbacks,
        Action<DeleteItemResult> onResult)
    {
        for (var rest = position; rest < pending.Count; rest++)
        {
            var index = pending[rest];

            onResult(callbacks.Exists(paths[index])
                ? new(index, DeleteItemStatus.None, null)
                : new(index, DeleteItemStatus.Removed, null));
        }
    }

    private static List<string> Collect(IReadOnlyList<string> paths, List<int> pending)
    {
        var chunk = new List<string>(pending.Count);

        foreach (var index in pending)
        {
            chunk.Add(paths[index]);
        }

        return chunk;
    }
}
