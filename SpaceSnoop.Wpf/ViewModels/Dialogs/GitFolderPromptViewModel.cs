using KeepShell.Services.Modal;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public enum GitFolderPromptChoice
{
    None = 0,
    Ask = 1,
    Skip = 2,
    Keep = 3,
}

public sealed partial class GitFolderPromptViewModel(int count) : ObservableObject, IDialogViewModel
{
    [ObservableProperty]
    private bool _rememberChoice;

    public event EventHandler<bool>? RequestClose;

    public string Title => "Git-папки";

    public string CountText => $"Найдены git-папки (.git): {count}.";

    public GitFolderPromptChoice Choice { get; private set; } = GitFolderPromptChoice.Ask;

    [RelayCommand]
    private void Skip()
    {
        Close(GitFolderPromptChoice.Skip);
    }

    [RelayCommand]
    private void Keep()
    {
        Close(GitFolderPromptChoice.Keep);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, false);
    }

    private void Close(GitFolderPromptChoice choice)
    {
        Choice = RememberChoice ? choice : GitFolderPromptChoice.Ask;
        RequestClose?.Invoke(this, choice == GitFolderPromptChoice.Skip);
    }
}
