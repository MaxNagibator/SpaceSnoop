using System.Diagnostics;
using System.Security.Principal;

namespace SpaceSnoop;

public sealed class AdministratorChecker
{
    private static readonly object Padlock = new();
    private static AdministratorChecker? _instance;

    private AdministratorChecker()
    {
    }

    public static AdministratorChecker Instance
    {
        get
        {
            lock (Padlock)
            {
                return _instance ??= new AdministratorChecker();
            }
        }
    }

    public bool IsCurrentUserAdmin()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void RestartAsAdmin()
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
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось перезапустить приложение от имени администратора: {ex.Message}");
        }

        Application.Exit();
    }
}
