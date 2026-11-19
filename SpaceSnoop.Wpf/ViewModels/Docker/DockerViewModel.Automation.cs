namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerViewModel
{
    internal void ShowBusyForAutomation()
    {
        lock (_refreshLock)
        {
            _refreshCts = new();
        }
        IsBusy = true;
        StatusText = PollingStatus;
    }

    internal void ClearBusyForAutomation()
    {
        lock (_refreshLock)
        {
            _refreshCts?.Dispose();
            _refreshCts = null;
        }
        IsBusy = false;
        NotifyCancelChanged();
    }
}
