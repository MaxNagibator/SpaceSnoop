using KeepShell.Services;
using KeepShell.Services.Modal;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class NoopDialogs(bool showResult = false, bool confirmResult = true, Func<IDialogViewModel, bool>? show = null) : IDialogService
{
    private readonly bool _showResult = showResult;

    public string? LastConfirmMessage { get; private set; }

    public int ShowCount { get; private set; }

    public string? LastWarningMessage { get; private set; }

    public string? LastErrorMessage { get; private set; }

    public Task<bool> ShowAsync(IDialogViewModel viewModel)
    {
        ShowCount++;

        return Task.FromResult(show?.Invoke(viewModel) ?? _showResult);
    }

    public Task<bool> ReplaceAsync(IDialogViewModel viewModel)
    {
        return Task.FromResult(false);
    }

    public bool Confirm(string title, string message, bool defaultYes = false)
    {
        LastConfirmMessage = message;

        return confirmResult;
    }

    public bool ConfirmWarning(string title, string message, bool defaultYes = false)
    {
        return true;
    }

    public void Info(string title, string message)
    {
    }

    public void Warning(string title, string message)
    {
        LastWarningMessage = message;
    }

    public void Error(string title, string message)
    {
        LastErrorMessage = message;
    }
}
