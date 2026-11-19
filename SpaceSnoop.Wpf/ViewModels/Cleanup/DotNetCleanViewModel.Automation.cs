namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class DotNetCleanViewModel
{
    internal void ResetForAutomation()
    {
        _plan = null;
        _inputVersion++;
        IsTrusted = false;
        DisplayPlan = null;
        ResultText = null;
        LogPath = null;
        StatusText = "Демонстрация dotnet clean отключена.";
        HasError = false;
    }

    internal void ShowForAutomation()
    {
        ResetForAutomation();
        ProjectPath = @"C:\workspace\sample\Sample.csproj";
        DisplayPlan = new(
            @"C:\workspace\sample\Sample.csproj",
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\workspace\sample",
            "10.0.303",
            "Debug",
            "",
            "",
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"clean\" \"C:\\workspace\\sample\\Sample.csproj\" \"--configuration\" \"Debug\" \"--disable-build-servers\"");

        StatusText = "Демонстрационный контекст. Запуск команды отключён.";
    }
}
