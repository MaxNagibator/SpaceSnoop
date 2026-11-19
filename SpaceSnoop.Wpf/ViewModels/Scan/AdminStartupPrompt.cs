using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public enum StartupAdminMode
{
    None = 0,
    Ask = 1,
    WithoutAdmin = 2,
    AlwaysAdmin = 3,
}

public enum StartupAdminAction
{
    None = 0,
    WelcomeCard = 1,
    Question = 2,
    Restart = 3,
}

public sealed class AdminStartupPrompt(
    IDialogService dialogs,
    ShellPreferences shell,
    Func<Task> restart,
    ToastNotifier? notifier = null)
{
    internal static readonly ConfirmChoice ContinueWithoutAdmin = new("Не перезапускать", ConfirmChoiceKind.Dismissive);

    internal static readonly ConfirmChoice Restart = new("Перезапустить", ConfirmChoiceKind.Primary);

    public static StartupAdminAction Decide(
        bool isElevated,
        bool welcomePending,
        StartupAdminMode mode,
        bool elevationRefused = false)
    {
        if (welcomePending)
        {
            return StartupAdminAction.WelcomeCard;
        }

        if (isElevated)
        {
            return StartupAdminAction.None;
        }

        return mode switch
        {
            StartupAdminMode.AlwaysAdmin => elevationRefused ? StartupAdminAction.None : StartupAdminAction.Restart,
            StartupAdminMode.Ask => StartupAdminAction.Question,
            _ => StartupAdminAction.None,
        };
    }

    public async Task RunAsync(bool isElevated, bool welcomePending, bool elevationRefused = false)
    {
        if (elevationRefused)
        {
            notifier?.Notify(AdminRestartPrompt.RefusedMessage, StatusSeverity.Warning);
            return;
        }

        switch (Decide(isElevated, welcomePending, shell.StartupAdmin, elevationRefused))
        {
            case StartupAdminAction.Restart:
                await restart();
                return;

            case StartupAdminAction.Question:
                break;

            default:
                return;
        }

        var dialog = BuildConfirm();
        await dialogs.ShowAsync(dialog);

        if (ReferenceEquals(dialog.Chosen, Restart))
        {
            if (dialog.RememberChoice)
            {
                shell.StartupAdmin = StartupAdminMode.AlwaysAdmin;
                shell.Flush();
            }

            await restart();
        }
        else if (dialog.RememberChoice && ReferenceEquals(dialog.Chosen, ContinueWithoutAdmin))
        {
            shell.StartupAdmin = StartupAdminMode.WithoutAdmin;
        }
    }

    internal static ConfirmDialogViewModel BuildConfirm()
    {
        return new(
            "Запустить с правами администратора?",
            PackIconLucideKind.ShieldAlert,
            [
                "Программа запущена без прав администратора, поэтому часть системных папок не прочитается и размеры выйдут заниженными.",
                "Программа закроется и откроется снова, Windows спросит разрешение.",
                "Выбор можно изменить в «Настройки → Запуск».",
            ],
            [
                ContinueWithoutAdmin,
                Restart,
            ])
        {
            RememberCaption = "Больше не спрашивать",
        };
    }
}
