using SpaceSnoop.Services;
using System.ComponentModel;

namespace SpaceSnoop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (AdministratorChecker.IsRestartRequired())
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        var spaceColorCalculator = new SpaceColorCalculator();
        var colorService = new ColorService(spaceColorCalculator);

        var diskSpaceCalculator = new DiskSpaceCalculator();
        var backgroundWorker = new BackgroundWorker();
        var workerService = new WorkerService(diskSpaceCalculator, backgroundWorker);

        var sortService = new SortService();

        using var form = new MainForm(workerService, colorService, sortService);
        Application.Run(form);
    }
}
