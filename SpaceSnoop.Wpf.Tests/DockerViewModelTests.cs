using KeepShell.Services.Modal;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Wpf.ViewModels.Docker;

namespace SpaceSnoop.Wpf.Tests;

public sealed class DockerViewModelTests
{
    private const string VhdxPath = @"C:\Docker\wsl\disk\docker_data.vhdx";
    private const string BasePath = @"C:\Docker\wsl";

    [Test]
    public async Task Успешное_сжатие_оставляет_статус_о_необходимости_запустить_Docker()
    {
        var vm = CreateViewModel(diskpart: Success("DiskPart successfully compacted the virtual disk file."));

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("Готово. Запустите Docker заново."));
        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.CancelCommand, Is.Null);
    }

    [Test]
    public async Task Провал_diskpart_оставляет_статус_об_остановленном_Docker()
    {
        var vm = CreateViewModel(diskpart: new(1, string.Empty, "DiskPart error: compact failed"));

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("Docker остановлен"));
    }

    [Test]
    public async Task Исчерпанный_таймаут_не_отменяет_первую_проверку_состояния()
    {
        var vm = CreateViewModel(diskpart: Success("DiskPart successfully compacted the virtual disk file."), stopTimeout: TimeSpan.Zero);

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("Готово. Запустите Docker заново."));
    }

    private static DockerViewModel CreateViewModel(DockerProcessResult diskpart, TimeSpan? stopTimeout = null)
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Status: stopped"),
            ("diskpart", "") => diskpart,
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var fileSystem = new FakeFileSystem([new(1000, 800), new(1000, 600)]);
        var service = new DockerService(
            runner,
            fileSystem,
            new StaticSettingsProvider(new(BasePath, true, null)),
            new DockerCompactOptions
            {
                ProcessTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(1),
                StatusPollInterval = TimeSpan.Zero,
            },
            administratorCheck: () => true);

        return new(service, new NoopDialogs(showResult: true), NullLogger<DockerViewModel>.Instance);
    }

    private static DockerProcessResult Success(string output = "")
    {
        return new(0, output, string.Empty);
    }

    private sealed class StaticSettingsProvider(DockerDesktopSettings settings) : IDockerDesktopSettingsProvider
    {
        public DockerDesktopSettings? Read()
        {
            return settings;
        }
    }

    private sealed class FakeFileSystem(IReadOnlyList<DockerFileMetrics> measurements) : IDockerFileSystem
    {
        private readonly Queue<DockerFileMetrics> _measurements = new(measurements);

        public bool FileExists(string path)
        {
            return string.Equals(path, VhdxPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAvailable(string path)
        {
            return FileExists(path);
        }

        public DockerFileMetrics GetMetrics(string path)
        {
            Assert.That(path, Is.EqualTo(VhdxPath));
            return _measurements.Dequeue();
        }
    }

    private sealed class ScriptedProcessRunner(
        Func<string, string, DockerProcessResult> responder) : IDockerProcessRunner
    {
        public Task<DockerProcessResult> RunAsync(
            string fileName,
            string arguments,
            DockerProcessOptions options,
            CancellationToken cancel = default)
        {
            return Task.FromResult(responder(fileName, arguments));
        }
    }
}
