using System.Diagnostics;
using System.Security.Principal;

namespace SpaceSnoop.Core;

public static class AdministratorChecker
{
    private const string? WarningMessage =
        """
        Программа запущена не от имени администратора из-за чего у могут отображаться не все директории. 
        Рекомендуется запустить её от имени администратора. 
        Хотите перезапустить от имени администратора?
        """;

    public static bool IsCurrentUserAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsRestartRequired()
    {
        if (IsCurrentUserAdmin())
        {
            return false;
        }

        var result = MessageBox.Show(WarningMessage, "Предупреждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return false;
        }

        RestartAsAdmin();
        return true;
    }

    private static void RestartAsAdmin()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Application.ExecutablePath,
            Verb = "runas",
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не удалось перезапустить приложение от имени администратора: {exception.Message}");
        }

        Application.Exit();
    }
}
