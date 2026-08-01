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

public enum ConfirmTextTone
{
    None = 0,
    Muted = 1,
    Strong = 2,
    Danger = 3,
}

public enum ConfirmMetricTone
{
    None = 0,
    Sub = 1,
    Danger = 2,
}

public sealed record ConfirmChoice(string Caption, ConfirmChoiceKind Kind)
{
    public bool IsDismissive => Kind == ConfirmChoiceKind.Dismissive;

    public bool IsSecondary => Kind == ConfirmChoiceKind.Secondary;

    public bool IsPrimary => Kind == ConfirmChoiceKind.Primary;

    public bool IsDestructive => Kind == ConfirmChoiceKind.Destructive;
}

public abstract record ConfirmLine
{
    public abstract string PlainText { get; }
}

public sealed record ConfirmTextLine(string Body, ConfirmTextTone Tone = ConfirmTextTone.None) : ConfirmLine
{
    public override string PlainText => Body;
}

public sealed record ConfirmMetricLine(
    string Caption,
    string Count,
    string Size,
    ConfirmMetricTone Tone = ConfirmMetricTone.None) : ConfirmLine
{
    public override string PlainText
    {
        get
        {
            var value = (Count, Size) switch
            {
                ("", var size) => size,
                (var count, "") => count,
                _ => $"{Count} ({Size})",
            };

            return Tone == ConfirmMetricTone.Sub ? $"    – {Caption}: {value}" : $"{Caption}: {value}";
        }
    }
}

public sealed record ConfirmGapLine : ConfirmLine
{
    public override string PlainText => string.Empty;
}

public sealed partial class ConfirmDialogViewModel(
    string title,
    PackIconLucideKind iconKind,
    IReadOnlyList<ConfirmLine> lines,
    IReadOnlyList<ConfirmChoice> choices) : ObservableObject, IDialogViewModel, IAcceptableDialog
{
    public ConfirmDialogViewModel(
        string title,
        PackIconLucideKind iconKind,
        IReadOnlyList<string> lines,
        IReadOnlyList<ConfirmChoice> choices)
        : this(title, iconKind, AsLines(lines), choices)
    {
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => title;

    public PackIconLucideKind IconKind => iconKind;

    public IReadOnlyList<ConfirmLine> Lines => lines;

    public IReadOnlyList<ConfirmChoice> Choices => choices;

    public string? Summary { get; init; }

    public string? Warning { get; init; }

    public bool HasSummary => !string.IsNullOrEmpty(Summary);

    public bool HasWarning => !string.IsNullOrEmpty(Warning);

    public ConfirmChoice? Chosen { get; private set; }

    public static string AsText(IEnumerable<ConfirmLine> lines)
    {
        return string.Join(Environment.NewLine, lines.Select(static line => line.PlainText));
    }

    public bool TryAccept()
    {
        if (choices.Any(static choice => choice.IsDestructive)
            || choices.FirstOrDefault(static choice => choice.IsPrimary) is not { } primary)
        {
            return false;
        }

        Choose(primary);
        return true;
    }

    private static List<ConfirmLine> AsLines(IReadOnlyList<string> lines)
    {
        return [.. lines.Select(static line => string.IsNullOrEmpty(line)
            ? new ConfirmGapLine()
            : (ConfirmLine)new ConfirmTextLine(line))];
    }

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
