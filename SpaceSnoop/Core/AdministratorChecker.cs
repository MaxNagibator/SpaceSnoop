using System.Diagnostics;
using System.Security.Principal;

namespace SpaceSnoop.Core;

public class AdministratorChecker(ILogger<AdministratorChecker> logger) : IAdministratorChecker
{
    private const string? WarningMessage =
        """
        Программа запущена не от имени администратора из-за чего у могут отображаться не все директории. 
        Рекомендуется запустить её от имени администратора. 
        Хотите перезапустить от имени администратора?
        """;

    public bool IsCurrentUserAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public bool IsRestartRequired()
    {
        if (IsCurrentUserAdmin())
        {
            return false;
        }

        DialogResult result = MessageBox.Show(WarningMessage, "Предупреждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            logger.LogWarning("Приложение запущено не от имени администратора");
            return false;
        }

        RestartAsAdmin();
        return true;
    }

    private void RestartAsAdmin()
    {
        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Application.ExecutablePath,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Не удалось перезапустить приложение от имени администратора");
            MessageBox.Show($"Не удалось перезапустить приложение от имени администратора: {exception.Message}");
        }

        Application.Exit();
    }
}
