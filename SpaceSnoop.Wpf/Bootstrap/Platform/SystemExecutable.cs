using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

internal static class SystemExecutable
{
    public static readonly string SchTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
}
