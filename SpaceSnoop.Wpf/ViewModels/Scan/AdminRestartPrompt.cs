using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class AdminRestartPrompt(IDialogService dialogs, ToastNotifier notifier, Func<int> markedCount, Func<bool> restart)
{
    public const string RefusedMessage = "Перезапуск отменён – права администратора не выданы";

    public async Task<bool> RunAsync()
    {
        var marked = markedCount();

        if (marked > 0 && !await dialogs.ShowAsync(BuildConfirm(marked)))
        {
            return false;
        }

        if (restart())
        {
            return true;
        }

        notifier.Notify(RefusedMessage, StatusSeverity.Warning);
        return false;
    }

    internal static ConfirmDialogViewModel BuildConfirm(int marked)
    {
        return new(
            "Перезапуск с правами администратора",
            PackIconLucideKind.ShieldAlert,
            [
                "Программа закроется и откроется снова с правами администратора.",
                "Результат сканирования не сохранится – сканирование нужно будет запустить заново.",
            ],
            [
                new("Не перезапускать", ConfirmChoiceKind.Dismissive),
                new("Перезапустить", ConfirmChoiceKind.Destructive),
            ])
        {
            Warning =
                $"Пометки на удаление пропадут: {Plural.Format(marked, "объект", "объекта", "объектов")} {Plural.Word(marked, "не уйдёт", "не уйдут", "не уйдут")} в корзину.",
        };
    }
}
