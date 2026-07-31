using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public enum ConfirmChoiceKind
{
    None = 0,
    Dismissive = 1,
    Secondary = 2,
    Primary = 3,
    Destructive = 4,
}

public sealed record ConfirmChoice(string Caption, ConfirmChoiceKind Kind)
{
    public bool IsDismissive => Kind == ConfirmChoiceKind.Dismissive;

    public bool IsSecondary => Kind == ConfirmChoiceKind.Secondary;

    public bool IsPrimary => Kind == ConfirmChoiceKind.Primary;

    public bool IsDestructive => Kind == ConfirmChoiceKind.Destructive;
}

public sealed partial class ConfirmDialogViewModel(
    string title,
    PackIconLucideKind iconKind,
    IReadOnlyList<string> lines,
    IReadOnlyList<ConfirmChoice> choices,
    string? warning = null) : ObservableObject, IDialogViewModel
{
    public event EventHandler<bool>? RequestClose;

    public string Title => title;

    public PackIconLucideKind IconKind => iconKind;

    public IReadOnlyList<string> Lines => lines;

    public IReadOnlyList<ConfirmChoice> Choices => choices;

    public string? Warning => warning;

    public bool HasWarning => !string.IsNullOrEmpty(warning);

    public ConfirmChoice? Chosen { get; private set; }

    [RelayCommand]
    private void Choose(ConfirmChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        Chosen = choice;
        RequestClose?.Invoke(this, !choice.IsDismissive);
    }
}
