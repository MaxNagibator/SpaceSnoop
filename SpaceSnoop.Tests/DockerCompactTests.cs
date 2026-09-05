using SpaceSnoop.Core.Docker;

namespace SpaceSnoop.Tests;

public sealed class DockerCompactTests
{
    private const string BasePath = @"C:\Docker\wsl";
    private const string VhdxPath = @"C:\Docker\wsl\disk\docker_data.vhdx";

    [Test]
    public async Task Сжатие_использует_образ_данных_и_отчитывается_о_занятом_месте()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0docker-desktop-data\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Name                Value\r\nStatus              stopped\r\n"),
            ("diskpart", "") => Success("DiskPart successfully compacted the virtual disk file."),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var fileSystem = new FakeFileSystem(
            VhdxPath,
            [new(1000, 800), new(1000, 400)]);
        var service = CreateService(runner, fileSystem);

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.VhdxPath, Is.EqualTo(VhdxPath));
        Assert.That(result.FreedAllocatedBytes, Is.EqualTo(400));
        Assert.That(runner.Calls.Any(call => call.Arguments.Contains("--set-sparse", StringComparison.Ordinal)), Is.False);

        var diskpart = runner.Calls.Single(call => call.FileName == "diskpart");
        Assert.That(diskpart.Options.StandardInput, Does.Contain("select vdisk file=\"C:\\Docker\\wsl\\disk\\docker_data.vhdx\""));
        Assert.That(diskpart.Options.StandardInput, Does.Contain("compact vdisk"));
        Assert.That(diskpart.Options.StandardInput, Does.Not.Contain("attach vdisk"));
        Assert.That(diskpart.Options.StandardInput, Does.Not.Contain("detach vdisk"));
        Assert.That(diskpart.Options.Timeout, Is.EqualTo(System.Threading.Timeout.InfiniteTimeSpan));
        Assert.That(diskpart.Cancel, Is.EqualTo(CancellationToken.None));
    }

    [Test]
    public async Task Ошибка_штатной_остановки_требует_отдельного_подтверждения_WSL()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0KeepBench-Kimi\0"),
            ("docker", "desktop stop") => new(-1, string.Empty, "Docker Desktop CLI недоступен", "docker не найден"),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.RequiresWslShutdownConfirmation));
        Assert.That(result.WslDistributions, Is.EqualTo(["docker-desktop", "KeepBench-Kimi"]));
        Assert.That(runner.Calls.Any(call => call.FileName == "wsl" && call.Arguments == "--shutdown"), Is.False);
    }

    [Test]
    public async Task Подтверждённый_fallback_останавливает_WSL_и_продолжает_сжатие()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => new(-1, string.Empty, "stop failed"),
            ("wsl", "--shutdown") => Success(),
            ("diskpart", "") => Success("DiskPart successfully compacted the virtual disk file."),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]));

        var result = await service.CompactAsync(allowWslShutdownFallback: true);

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(runner.Calls.Any(call => call.FileName == "wsl" && call.Arguments == "--shutdown"), Is.True);
        Assert.That(runner.Calls.Count(call => call.FileName == "docker" && call.Arguments == "desktop stop"), Is.Zero);
    }

    [Test]
    public async Task Ошибка_diskpart_не_возвращается_как_успех()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Status: stopped"),
            ("diskpart", "") => new(1, string.Empty, "DiskPart error: compact failed"),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 800)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Failed));
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Summary, Does.Contain("error"));
        Assert.That(result.DockerStopped, Is.True);
    }

    [Test]
    public async Task Сжатие_не_падает_если_замер_после_diskpart_не_удался()
    {
        var runner = CreateSuccessfulRunner();
        var service = CreateService(
            runner,
            new FakeFileSystem(VhdxPath, [new(1000, 800)], new IOException("Файл занят")),
            new(BasePath, true, null));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.After, Is.Null);
        Assert.That(result.Summary, Does.Contain("не удалось измерить"));
    }

    [Test]
    public async Task Пустой_CustomWslDistroDir_использует_стандартный_путь()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Docker",
            "wsl",
            "disk",
            "docker_data.vhdx");
        var service = CreateService(
            CreateSuccessfulRunner(),
            new FakeFileSystem(path, [new(1000, 800), new(1000, 600)]),
            new(null, true, null));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.VhdxPath, Is.EqualTo(path));
    }

    [Test]
    public async Task Старая_раскладка_использует_ext4_образ_docker_desktop_data()
    {
        const string legacyPath = @"C:\Docker\legacy\ext4.vhdx";
        var service = CreateService(
            CreateSuccessfulRunner(),
            new FakeFileSystem(legacyPath, [new(1000, 800), new(1000, 600)]),
            new(null, true, null),
            new StaticLegacyVhdxProvider([legacyPath]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.VhdxPath, Is.EqualTo(legacyPath));
    }

    [Test]
    public async Task Префикс_длинного_пути_нормализуется()
    {
        var service = CreateService(
            CreateSuccessfulRunner(),
            new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]),
            new(@"\\?\C:\Docker\wsl", true, null));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.VhdxPath, Is.EqualTo(VhdxPath));
    }

    [Test]
    public async Task UNC_префикс_длинного_пути_нормализуется_в_сетевой_путь()
    {
        const string normalizedPath = @"\\server\share\Docker\wsl\disk\docker_data.vhdx";
        var service = CreateService(
            CreateSuccessfulRunner(),
            new FakeFileSystem(normalizedPath, [new(1000, 800), new(1000, 600)]),
            new(@"\\?\UNC\server\share\Docker\wsl", true, null));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(result.VhdxPath, Is.EqualTo(normalizedPath));
    }

    [Test]
    public async Task Неадминистратор_останавливать_Docker_не_начинает()
    {
        var runner = new ScriptedProcessRunner((_, _) => throw new AssertionException("Процессы не должны запускаться"));
        var service = CreateService(
            runner,
            new FakeFileSystem(VhdxPath, [new(1000, 800)]),
            new(BasePath, true, null),
            administratorCheck: () => false);

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Failed));
        Assert.That(result.Summary, Does.Contain("права администратора"));
        Assert.That(runner.Calls, Is.Empty);
    }

    [Test]
    public async Task Не_WSL2_бэкенд_отклоняется_до_остановки_Docker()
    {
        var runner = new ScriptedProcessRunner((_, _) => throw new AssertionException("Процессы не должны запускаться"));
        var service = CreateService(
            runner,
            new FakeFileSystem(VhdxPath, [new(1000, 800)]),
            new(BasePath, false, @"C:\ProgramData\DockerDesktop\vm-data"));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Failed));
        Assert.That(result.Summary, Does.Contain("только WSL2"));
        Assert.That(runner.Calls, Is.Empty);
    }

    [Test]
    public async Task Отсутствующие_настройки_останавливают_операцию_до_запуска_процессов()
    {
        var runner = new ScriptedProcessRunner((_, _) => throw new AssertionException("Процессы не должны запускаться"));
        var service = CreateService(
            runner,
            new FakeFileSystem(VhdxPath, [new(1000, 800)]),
            settings: null,
            settingsProvider: new StaticSettingsProvider(null));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Failed));
        Assert.That(result.Summary, Does.Contain("settings-store.json"));
        Assert.That(runner.Calls, Is.Empty);
    }

    [Test]
    public async Task Ложное_слово_error_в_выводе_diskpart_не_считает_операцию_ошибкой()
    {
        var runner = CreateSuccessfulRunner("DiskPart diagnostic: error handling is enabled.");
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
    }

    [Test]
    public async Task Ложное_слово_error_в_stderr_diskpart_не_считает_операцию_ошибкой()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Status: stopped"),
            ("diskpart", "") => new(0, "DiskPart completed.", "diagnostic: error handling is enabled"),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
    }

    [TestCase("Docker Desktop is not running")]
    [TestCase("Docker Desktop is not running.")]
    [TestCase("Docker Desktop is stopped.")]
    [TestCase("Docker Desktop не запущен.")]
    public async Task Остановка_Docker_без_ключа_Status_распознаётся(string statusOutput)
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success(statusOutput),
            ("diskpart", "") => Success(),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
    }

    [Test]
    public async Task Стадия_Compacting_сообщается_до_запуска_diskpart()
    {
        var compactingReported = false;
        var runner = new ScriptedProcessRunner((fileName, arguments) =>
        {
            if (fileName == "diskpart")
            {
                Assert.That(compactingReported, Is.True);
            }

            return (fileName, arguments) switch
            {
                ("wsl", "--list --quiet") => Success("docker-desktop\0"),
                ("docker", "desktop stop") => Success(),
                ("docker", "desktop status") => Success("Status: stopped"),
                ("diskpart", "") => Success(),
                _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
            };
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)]));

        var result = await service.CompactAsync(stageChanged: stage => compactingReported = stage == DockerCompactStage.Compacting);

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
    }

    [Test]
    public async Task Ожидание_занятого_файла_повторяет_проверку_до_освобождения()
    {
        var runner = CreateSuccessfulRunner();
        var service = CreateService(
            runner,
            new FakeFileSystem(VhdxPath, [new(1000, 800), new(1000, 600)], availability: [false, true]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Succeeded));
        Assert.That(runner.Calls.Count(call => call.FileName == "docker" && call.Arguments == "desktop status"), Is.EqualTo(2));
    }

    [Test]
    public async Task Отмена_процесса_возвращает_отменённый_результат()
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => new(-1, string.Empty, string.Empty, Canceled: true),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
        var service = CreateService(runner, new FakeFileSystem(VhdxPath, [new(1000, 800)]));

        var result = await service.CompactAsync();

        Assert.That(result.Status, Is.EqualTo(DockerCompactStatus.Canceled));
    }

    private static DockerService CreateService(
        ScriptedProcessRunner runner,
        FakeFileSystem fileSystem,
        DockerDesktopSettings? settings = null,
        IDockerLegacyVhdxProvider? legacyVhdxProvider = null,
        Func<bool>? administratorCheck = null,
        IDockerDesktopSettingsProvider? settingsProvider = null)
    {
        return new(
            runner,
            fileSystem,
            settingsProvider ?? new StaticSettingsProvider(settings ?? new(BasePath, true, null)),
            new DockerCompactOptions
            {
                ProcessTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.FromSeconds(1),
                StatusPollInterval = TimeSpan.Zero,
            },
            legacyVhdxProvider,
            administratorCheck ?? (() => true));
    }

    private static ScriptedProcessRunner CreateSuccessfulRunner(string diskpartOutput = "DiskPart successfully compacted the virtual disk file.")
    {
        return new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Status: stopped"),
            ("diskpart", "") => Success(diskpartOutput),
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });
    }

    private static DockerProcessResult Success(string output = "")
    {
        return new(0, output, string.Empty);
    }

    private sealed class ScriptedProcessRunner(
        Func<string, string, DockerProcessResult> responder) : IDockerProcessRunner
    {
        public List<Call> Calls { get; } = [];

        public Task<DockerProcessResult> RunAsync(
            string fileName,
            string arguments,
            DockerProcessOptions options,
            CancellationToken cancel = default)
        {
            Calls.Add(new(fileName, arguments, options, cancel));
            return Task.FromResult(responder(fileName, arguments));
        }
    }

    private sealed record Call(string FileName, string Arguments, DockerProcessOptions Options, CancellationToken Cancel);

    private sealed class FakeFileSystem(
        string expectedPath,
        IReadOnlyList<DockerFileMetrics> measurements,
        Exception? afterMetricsException = null,
        IReadOnlyList<bool>? availability = null) : IDockerFileSystem
    {
        private readonly Queue<DockerFileMetrics> _measurements = new(measurements);
        private readonly Queue<bool> _availability = new(availability ?? []);
        private int _metricsRead;

        public bool FileExists(string path)
        {
            return string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAvailable(string path)
        {
            Assert.That(path, Is.EqualTo(expectedPath));
            return _availability.Count > 0 ? _availability.Dequeue() : FileExists(path);
        }

        public DockerFileMetrics GetMetrics(string path)
        {
            Assert.That(path, Is.EqualTo(expectedPath));
            if (++_metricsRead == 2 && afterMetricsException is not null)
            {
                throw afterMetricsException;
            }

            return _measurements.Dequeue();
        }
    }

    private sealed class StaticSettingsProvider(DockerDesktopSettings? settings) : IDockerDesktopSettingsProvider
    {
        public DockerDesktopSettings? Read()
        {
            return settings;
        }
    }

    private sealed class StaticLegacyVhdxProvider(IReadOnlyList<string> paths) : IDockerLegacyVhdxProvider
    {
        public IReadOnlyList<string> Locate()
        {
            return paths;
        }
    }
}
