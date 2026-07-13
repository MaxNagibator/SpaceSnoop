using System.IO;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class SyncProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
    public int Mode { get; set; }
    public bool Mirror { get; set; }
    public string Exclusions { get; set; } = string.Empty;
    public ScheduleInterval Interval { get; set; } = ScheduleInterval.Daily;
    public string Time { get; set; } = "03:00";
    public bool Enabled { get; set; }

    public static bool SourceMissing(string left, string right, SyncMode mode)
    {
        return mode switch
        {
            SyncMode.RightToLeft => !Directory.Exists(right),
            SyncMode.Bidirectional => !Directory.Exists(left) && !Directory.Exists(right),
            _ => !Directory.Exists(left),
        };
    }

    public static bool PathsOverlap(string left, string right)
    {
        string a, b;

        try
        {
            a = Trim(Path.GetFullPath(left));
            b = Trim(Path.GetFullPath(right));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        static string Trim(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
