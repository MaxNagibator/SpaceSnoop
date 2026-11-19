using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class RescanPrompt(IDialogService dialogs, ScanPreferences preferences)
{
    internal static readonly ConfirmChoice StopAsking = new("Сканировать и больше не спрашивать", ConfirmChoiceKind.Secondary);

    public bool ShouldAsk(IEnumerable<ScanNodeViewModel> roots, string path, bool marksPresent)
    {
        return preferences.ReleaseBeforeRescan
            && preferences.ConfirmRescan
            && !string.IsNullOrWhiteSpace(path)
            && ScanTreeEditor.HasReleasableRoot(roots, path, marksPresent);
    }

    public async Task<bool> ConfirmAsync(IEnumerable<ScanNodeViewModel> roots, string path, bool marksPresent)
    {
        if (!ShouldAsk(roots, path, marksPresent))
        {
            return true;
        }

        var dialog = BuildConfirm(path);

        if (!await dialogs.ShowAsync(dialog))
        {
            return false;
        }

        if (ReferenceEquals(dialog.Chosen, StopAsking))
        {
            preferences.ConfirmRescan = false;
        }

        return true;
    }

    internal static ConfirmDialogViewModel BuildConfirm(string path)
    {
        return new(
            "Сканировать заново?",
            PackIconLucideKind.RefreshCw,
            [
                "У этой папки уже есть результат сканирования. Он уберётся с экрана, как только начнётся новое сканирование, – так программе хватает памяти.",
                "Новый результат появится, когда сканирование закончится.",
            ],
            [
                new("Не сканировать", ConfirmChoiceKind.Dismissive),
                StopAsking,
                new("Сканировать заново", ConfirmChoiceKind.Primary),
            ])
        {
            Summary = path,
            Warning = "Если остановить сканирование раньше времени, прежнего результата уже не будет – папку придётся сканировать ещё раз.",
        };
    }
}
