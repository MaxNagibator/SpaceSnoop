using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public static class CleanupConfirm
{
    public static ConfirmDialogViewModel Build(
        IReadOnlyList<CleanupTargetViewModel> rows,
        long bytes,
        int files,
        string? requestedBy)
    {
        List<ConfirmLine> lines = [];

        if (requestedBy is not null)
        {
            lines.Add(new ConfirmTextLine(requestedBy, ConfirmTextTone.Strong));
            lines.Add(new ConfirmGapLine());
        }

        lines.Add(new ConfirmTextLine($"Будет очищено корзин: {rows.Count:N0}."));
        lines.Add(new ConfirmMetricLine("Файлов", $"{files:N0}", SizeFormatter.Format(bytes)));

        lines.AddRange(rows.Select(static row =>
            new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText, ConfirmMetricTone.Sub)));

        return new ConfirmDialogViewModel(
            "Очистка диска",
            PackIconLucideKind.Trash2,
            lines,
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Очистить", ConfirmChoiceKind.Destructive),
            ])
        {
            Warning = "Файлы удаляются безвозвратно, мимо корзины – восстановить их нельзя.",
        };
    }
}
