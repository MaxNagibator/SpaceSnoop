namespace SpaceSnoop.Core.Cleanup;

public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupTarget> BuildDefault(TimeSpan? minimumAge = null)
    {
        var age = minimumAge ?? CleanupTarget.DefaultMinimumAge;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
