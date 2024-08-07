namespace SpaceSnoop;

internal static class Program
{
    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        if (AdministratorChecker.Instance.IsCurrentUserAdmin() == false)
        {
            DialogResult result = MessageBox.Show("""
                                                  Программа запущена не от имени администратора из-за чего у могут отображаться не все директории. 
                                                  Рекомендуется запустить её от имени администратора. 
                                                  Хотите перезапустить от имени администратора?
                                                  """, "Предупреждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                AdministratorChecker.Instance.RestartAsAdmin();
                return;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
