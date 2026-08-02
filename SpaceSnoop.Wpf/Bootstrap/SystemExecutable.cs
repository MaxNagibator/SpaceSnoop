using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

internal static class SystemExecutable
{
    public static readonly string SchTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
}
