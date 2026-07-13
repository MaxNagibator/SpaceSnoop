using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

internal static class SystemExecutable
{
    public static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public static readonly string SchTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
}
