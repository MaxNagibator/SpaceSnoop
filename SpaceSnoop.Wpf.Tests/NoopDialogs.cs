using KeepShell.Services;
using KeepShell.Services.Modal;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class NoopDialogs(bool showResult = false) : IDialogService
{
    private readonly bool _showResult = showResult;

    public Task<bool> ShowAsync(IDialogViewModel viewModel)
    {
        return Task.FromResult(_showResult);
    }

    public Task<bool> ReplaceAsync(IDialogViewModel viewModel)
    {
        return Task.FromResult(false);
    }

    public bool Confirm(string title, string message, bool defaultYes = false)
    {
        return true;
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
    }

    public void Error(string title, string message)
    {
    }
}
