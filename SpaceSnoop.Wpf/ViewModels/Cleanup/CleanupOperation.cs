using KeepShell.Services;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

internal sealed class CleanupOperation
{
    private CancellationTokenSource? _cancellation;
    private Action<bool>? _setBusy;
    private bool _confirming;

    public bool CanCancel => _cancellation is not null && !_confirming;
    public CancellationToken Token => _cancellation?.Token ?? throw new InvalidOperationException("Операция не запущена.");

    public void Begin(Action<bool> setBusy)
    {
        if (_cancellation is not null)
        {
            throw new InvalidOperationException("Операция уже выполняется.");
        }

        _cancellation = new();
        _setBusy = setBusy;
        setBusy(true);
    }

    public void RestartCancellation()
    {
        if (_cancellation is null)
        {
            throw new InvalidOperationException("Операция не запущена.");
        }

        _cancellation.Dispose();
        _cancellation = new();
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
    }

    public async Task<bool> ConfirmAsync(IDialogService dialogs, ConfirmDialogViewModel confirmation)
    {
        _confirming = true;
        try
        {
            return await dialogs.ShowAsync(confirmation) && !Token.IsCancellationRequested;
        }
        finally
        {
            _confirming = false;
        }
    }

    public void End()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        var setBusy = _setBusy;
        _setBusy = null;
        setBusy?.Invoke(false);
    }
}
