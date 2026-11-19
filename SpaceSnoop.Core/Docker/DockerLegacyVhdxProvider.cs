using Microsoft.Win32;

namespace SpaceSnoop.Core.Docker;

public interface IDockerLegacyVhdxProvider
{
    IReadOnlyList<string> Locate();
}

public sealed class WindowsDockerLegacyVhdxProvider : IDockerLegacyVhdxProvider
{
    public IReadOnlyList<string> Locate()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null)
        {
            return [];
        }

        foreach (var name in lxss.GetSubKeyNames())
        {
            using var sub = lxss.OpenSubKey(name);
            if (sub?.GetValue("DistributionName") is not string distro
                || !string.Equals(distro, "docker-desktop-data", StringComparison.OrdinalIgnoreCase)
                || sub.GetValue("BasePath") is not string basePath)
            {
                continue;
            }

            var vhdx = Path.Combine(NormalizePath(basePath), "ext4.vhdx");
            return File.Exists(vhdx) ? [vhdx] : [];
        }

        return [];
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return $@"\\{path[8..]}";
        }

        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
    }
}
