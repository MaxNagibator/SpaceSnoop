using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel
{
    private const double AutomationChunkSeconds = 12.4;

    internal void ShowRunningForAutomation(int done)
    {
        IsRunning = true;
        StatusText = RunningStatus;
        DiagnosticsText = "отклик 6 мс · пик 18 мс · 118 файлов/с · осталось 31 с";

        for (var index = 0; index < Items.Count && index < done; index++)
        {
            _freedBytes += Items[index].Space.TotalSize;
            ApplyTick(new(index, DeleteRowState.Done, null, 0, 0, 0));
        }

        ApplyAggregate(_freedBytes, done, 0);

        if (done < Items.Count)
        {
            ApplyTick(new(done, DeleteRowState.Deleting, null, 0, 0, 0));
            CurrentPath = Items[done].Path;
        }

        if (Items.Count > 0)
        {
            var started = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * AutomationChunkSeconds);
            ApplyChunk(new(true, 1, 1, 0, Math.Min(Items.Count, ChunkSize) - 1, started));
        }
    }

    internal void ShowFinishedForAutomation()
    {
        ShowRunningForAutomation(Items.Count);
        ClearPulse();

        IsRunning = false;
        IsFinished = true;
        HasErrors = HasFailedItems;
        StatusText = BuildSummary();
        DiagnosticsText = null;
    }

    internal void ClearForAutomation()
    {
        IsRunning = false;
        IsFinished = false;
        DiagnosticsText = null;
        CancelRequested = false;
        ClearPulse();
    }
}
