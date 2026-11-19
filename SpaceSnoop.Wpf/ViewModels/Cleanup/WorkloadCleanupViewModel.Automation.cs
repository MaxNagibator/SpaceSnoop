namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class WorkloadCleanupViewModel
{
    internal void ResetForAutomation()
    {
        _plan = null;
        DisplayPlan = null;
        ResultText = null;
        StatusText = "Демонстрация CLI workloads отключена.";
        LogPath = null;
    }

    internal void ShowForAutomation()
    {
        ResetForAutomation();
        DisplayPlan = new(
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\Users\Developer",
            "10.0.303",
            "Installed Workload Id      Manifest Version      Installation Source"
            + Environment.NewLine
            + "maui-windows               10.0.0/10.0.100      SDK 10.0.300",
            "dotnet workload clean");

        StatusText = "Демонстрационный контекст. Запуск команды отключён.";
    }
}
