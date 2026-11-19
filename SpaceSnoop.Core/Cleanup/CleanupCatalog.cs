namespace SpaceSnoop.Core.Cleanup;

public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupTarget> BuildDefault(TimeSpan? minimumAge = null)
    {
        var age = minimumAge ?? CleanupTarget.DefaultMinimumAge;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localLowAppData = Path.Combine(userProfile, @"AppData\LocalLow");
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var systemDrive = Path.GetPathRoot(windows) ?? @"C:\";

        return
        [
            new()
            {
                Id = "TempFiles",
                Name = "Временные файлы",
                Description = "Каталог %TEMP% текущего пользователя",
                Kind = CleanupTargetKind.Directory,
                Path = Path.GetTempPath(),
                MinimumAge = age,
            },
            new()
            {
                Id = "SystemTemp",
                Name = "Временные файлы системы",
                Description = @"Каталог Windows\Temp",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(windows, "Temp"),
                MinimumAge = age,
            },
            new()
            {
                Id = "WindowsUpdate",
                Name = "Кэш обновлений Windows",
                Description = "Скачанные пакеты Центра обновления",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(windows, @"SoftwareDistribution\Download"),
                MinimumAge = age,
            },
            new()
            {
                Id = "DeliveryOptimization",
                Name = "Кэш доставки обновлений",
                Description = "Пакеты Центра обновления и магазина, сохранённые службой Delivery Optimization",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                MinimumAge = age,
            },
            new()
            {
                Id = "Prefetch",
                Name = "Prefetch",
                Description = "Данные предзагрузки приложений, Windows наполнит их заново",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(windows, "Prefetch"),
                MinimumAge = age,
            },
            new()
            {
                Id = "Thumbnails",
                Name = "Кэш эскизов",
                Description = "Файлы thumbcache_* проводника",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(localAppData, @"Microsoft\Windows\Explorer"),
                SearchPattern = "thumbcache_*",
                Recursive = false,
                MinimumAge = age,
            },
            new()
            {
                Id = "DirectXShaderCache",
                Name = "Кэш шейдеров DirectX",
                Description = "Скомпилированные шейдеры DirectX – игры и приложения создадут их заново",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(localAppData, "D3DSCache"),
                MinimumAge = age,
            },
            new()
            {
                Id = "GpuShaderCache",
                Name = "Кэш шейдеров видеокарты",
                Description = "Скомпилированные шейдеры драйверов NVIDIA, AMD и Intel – пересобираются автоматически",
                Kind = CleanupTargetKind.Directory,
                Paths =
                [
                    Path.Combine(localAppData, @"NVIDIA\DXCache"),
                    Path.Combine(localAppData, @"NVIDIA\GLCache"),
                    Path.Combine(roamingAppData, @"NVIDIA\ComputeCache"),
                    Path.Combine(localLowAppData, @"NVIDIA\PerDriverVersion\DXCache"),
                    Path.Combine(localAppData, @"NVIDIA Corporation\NV_Cache"),
                    Path.Combine(localAppData, @"AMD\DxCache"),
                    Path.Combine(localAppData, @"AMD\GLCache"),
                    Path.Combine(localAppData, @"AMD\VkCache"),
                    Path.Combine(localLowAppData, @"AMD\DxCache"),
                    Path.Combine(localLowAppData, @"Intel\ShaderCache"),
                    Path.Combine(programData, @"Intel\ShaderCache"),
                ],
                MinimumAge = age,
            },
            new()
            {
                Id = "RecycleBin",
                Name = "Корзина",
                Description = "Удалённые файлы всех томов",
                Kind = CleanupTargetKind.RecycleBin,
                MinimumAge = TimeSpan.Zero,
            },
            new()
            {
                Id = "ErrorReports",
                Name = "Отчёты об ошибках",
                Description = "Дампы аварийно завершившихся программ",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(localAppData, "CrashDumps"),
                MinimumAge = age,
            },
            new()
            {
                Id = "WindowsErrorReporting",
                Name = "Системные отчёты об ошибках",
                Description = "Архив и очередь отчётов службы Windows Error Reporting",
                Kind = CleanupTargetKind.Directory,
                Paths =
                [
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"),
                    Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"),
                    Path.Combine(programData, @"Microsoft\Windows\WER\Temp"),
                ],
                MinimumAge = age,
            },
            new()
            {
                Id = "LiveKernelReports",
                Name = "Дампы сбоев ядра и драйверов",
                Description = @"Дампы нефатальных сбоев ядра и видеодрайвера в Windows\LiveKernelReports",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(windows, "LiveKernelReports"),
                SearchPattern = "*.dmp",
                MinimumAge = age,
            },
            new()
            {
                Id = "OldWindowsInstallation",
                Name = "Предыдущая установка Windows",
                Description = @"Каталог Windows.old, удаляется штатным средством системы",
                Kind = CleanupTargetKind.Directory,
                Path = Path.Combine(systemDrive, "Windows.old"),
                Supported = false,
                MinimumAge = TimeSpan.Zero,
            },
        ];
    }
}
