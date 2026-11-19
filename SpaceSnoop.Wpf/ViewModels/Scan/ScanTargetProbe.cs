using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanTargetProbe
{
    public static Func<string, bool> DirectoryExists { get; set; } = Directory.Exists;

    public static Func<DirectoryInfo, string?> RootRefusal { get; set; } = DiskSpaceCalculator.DescribeRootRefusal;
}
