using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SpaceSnoop.Core.Docker;

public sealed class DockerService
{
    public async Task<DockerSnapshot> GetSnapshotAsync(CancellationToken cancel = default)
    {
        var run = await RunAsync("docker", "system df --format \"{{json .}}\"", cancel: cancel);

        return run.Failed
            ? DockerSnapshot.Unavailable(DescribeFailure(run))
            : DockerSnapshot.Ok(DockerUsage.Parse(run.StdOut));
    }

    public async Task<IReadOnlyList<DockerObject>> GetInventoryAsync(CancellationToken cancel = default)
    {
        var run = await RunAsync("docker", "system df -v --format \"{{json .}}\"", cancel: cancel);
        return run.Failed ? [] : DockerInventory.Parse(run.StdOut);
    }

    public async Task<string> RemoveAsync(DockerObject target, CancellationToken cancel = default)
    {
        var args = target.Kind switch
        {
            DockerObjectKind.Image => $"image rm {target.Id}",
            DockerObjectKind.Container => $"container rm {target.Id}",
            DockerObjectKind.Volume => $"volume rm {target.Id}",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, null),
        };

        var run = await RunAsync("docker", args, cancel: cancel);
        return run.Failed ? throw new InvalidOperationException(DescribeFailure(run)) : run.StdOut.Trim();
    }

    public async Task<string> PruneAsync(DockerCleanupTarget target, bool allUnused = false, CancellationToken cancel = default)
    {
        var args = target switch
        {
            DockerCleanupTarget.BuildCache => "builder prune -f",
            DockerCleanupTarget.DanglingImages => "image prune -f",
            DockerCleanupTarget.UnusedImages => "image prune -a -f",
            DockerCleanupTarget.StoppedContainers => "container prune -f",
            DockerCleanupTarget.UnusedVolumes => allUnused ? "volume prune -a -f" : "volume prune -f",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        var run = await RunAsync("docker", args, cancel: cancel);

        return run.Failed ? throw new InvalidOperationException(DescribeFailure(run)) : run.StdOut.Trim();
    }

    public async Task<string> CompactAsync(CancellationToken cancel = default)
    {
        var log = new StringBuilder();

        var shutdown = await RunAsync("wsl", "--shutdown", cancel: cancel);
        log.AppendLine(shutdown.Failed ? $"wsl --shutdown: {DescribeFailure(shutdown)}" : "WSL остановлен.");

        var distros = await ListDockerDistrosAsync(cancel);
        if (distros.Count == 0)
        {
            return log.Append("Дистрибутивы docker-desktop не найдены – нечего сжимать.").ToString();
        }

        foreach (var distro in distros)
        {
            var sparse = await RunAsync("wsl", $"--manage {distro} --set-sparse true", cancel: cancel);
            log.AppendLine(sparse.Failed
                ? $"{distro}: не удалось включить sparse – {DescribeFailure(sparse)}"
                : $"{distro}: sparse включён, место возвращено системе.");
        }

        // TODO: агрессивный compact diskpart'ом (attach readonly → compact vdisk). Нужны права админа,
        //           освобождает лишь зануленные блоки – поверх sparse даёт немного. Optimize-VHD пропущен (тянет Hyper-V).
        foreach (var vhdx in LocateDockerVhdx())
        {
            log.AppendLine(await CompactVhdxAsync(vhdx, cancel));
        }

        return log.ToString().Trim();
    }

    private static IReadOnlyList<string> LocateDockerVhdx()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null)
        {
            return [];
        }

        var result = new List<string>();

        foreach (var name in lxss.GetSubKeyNames())
        {
            using var sub = lxss.OpenSubKey(name);

            if (sub?.GetValue("DistributionName") is not string distro
                || sub.GetValue("BasePath") is not string basePath
                || !distro.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var vhdx = Path.Combine(basePath, "ext4.vhdx");
            if (File.Exists(vhdx))
            {
                result.Add(vhdx);
            }
        }

        return result;
    }

    private static async Task<string> CompactVhdxAsync(string vhdx, CancellationToken cancel)
    {
        var script = $"select vdisk file=\"{vhdx}\"\r\nattach vdisk readonly\r\ncompact vdisk\r\ndetach vdisk\r\nexit\r\n";
        var run = await RunAsync("diskpart", string.Empty, stdin: script, cancel: cancel);

        return run.Failed
            ? $"{Path.GetFileName(vhdx)}: diskpart compact не удалось – {DescribeFailure(run)} (нужны права администратора)."
            : $"{Path.GetFileName(vhdx)}: образ диска скомпактизирован diskpart'ом.";
    }

    private static async Task<IReadOnlyList<string>> ListDockerDistrosAsync(CancellationToken cancel)
    {
        var run = await RunAsync("wsl", "--list --quiet", Encoding.Unicode, cancel);
        if (run.Failed)
        {
            return [];
        }

        return run.StdOut
            .Split(['\r', '\n', '\0'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task<ProcessRun> RunAsync(
        string fileName,
        string arguments,
        Encoding? outputEncoding = null,
        CancellationToken cancel = default,
        string? stdin = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin);
                process.StandardInput.Close();
            }

            var stdOut = process.StandardOutput.ReadToEndAsync(cancel);
            var stdErr = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);

            return new(process.ExitCode, await stdOut, await stdErr, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new(-1, string.Empty, string.Empty, ex.Message);
        }
    }

    private static string DescribeFailure(ProcessRun run)
    {
        if (run.LaunchError is not null)
        {
            return $"Не удалось запустить процесс: {run.LaunchError}. Установлен и запущен ли Docker Desktop?";
        }

        var message = run.StdErr.Trim();
        return message.Length > 0 ? message : $"Команда завершилась с кодом {run.ExitCode}.";
    }

    private sealed record ProcessRun(int ExitCode, string StdOut, string StdErr, string? LaunchError)
    {
        public bool Failed => LaunchError is not null || ExitCode != 0;
    }
}
