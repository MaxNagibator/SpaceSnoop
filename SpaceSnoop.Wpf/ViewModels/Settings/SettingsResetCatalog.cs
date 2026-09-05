using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public static class SettingsResetCatalog
{
    private const double ScaleTolerance = 0.005;

    public static IReadOnlyDictionary<string, SettingsResetPlan> Build(
        ThemeViewModel theme,
        ShellPreferences shell,
        ScanPreferences scan,
        OperationPreferences operations,
        UpdatePreferences update,
        McpPreferences mcp,
        AgentPreferences agent,
        ISettingsStore settings,
        Action<SettingsResetField> onFieldReset)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(mcp);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(settings);

        SettingsResetField Field(string key, string label, string defaultText, Func<bool> isDefault, Action apply)
        {
            return new(key, label, defaultText, isDefault, apply, onFieldReset);
        }

        SettingsResetPlan[] plans =
        [
            new("appearance",
                "Вернуть раздел к заводским значениям: светлая тема, масштаб 100 %, заголовок страницы и уведомления включены, строка производительности скрыта.",
                [
                    Field(SettingsKeys.Theme, "тема оформления", SettingsOptions.LabelOf(SettingsOptions.Themes, AppDefaults.ThemeDefault),
                        () => theme.Current == AppDefaults.ThemeDefault,
                        () => theme.ApplyCommand.Execute(AppDefaults.ThemeDefault)),
                    Field(SettingsKeys.FontScale, "масштаб шрифта", $"{FontScaleManager.DefaultScale * 100:F0} %",
                        () => Math.Abs(shell.FontScale - FontScaleManager.DefaultScale) < ScaleTolerance,
                        () => shell.FontScale = FontScaleManager.DefaultScale),
                    Field(SettingsKeys.ShowPageHeader, "заголовок страницы", Flag(AppDefaults.ShowPageHeaderDefault),
                        () => shell.ShowPageHeader == AppDefaults.ShowPageHeaderDefault,
                        () => shell.ShowPageHeader = AppDefaults.ShowPageHeaderDefault),
                    Field(SettingsKeys.EnableToastNotifications, "всплывающие уведомления", Flag(AppDefaults.ToastNotificationsDefault),
                        () => shell.EnableToastNotifications == AppDefaults.ToastNotificationsDefault,
                        () => shell.EnableToastNotifications = AppDefaults.ToastNotificationsDefault),
                    Field(SettingsKeys.PerformanceHud, "производительность в строке состояния", Flag(AppDefaults.PerformanceHudDefault),
                        () => shell.ShowPerformanceHud == AppDefaults.PerformanceHudDefault,
                        () => shell.ShowPerformanceHud = AppDefaults.PerformanceHudDefault),
                ],
                []),

            new("startup",
                "Вернуть раздел к заводским значениям: стартовая страница «Сканирование», рейл развёрнут, предупреждение о правах администратора включено.",
                [
                    Field(SettingsKeys.StartupPage, "стартовая страница", SettingsOptions.LabelOf(SettingsOptions.StartupPages, AppDefaults.StartupPageDefault),
                        () => shell.StartupPage == AppDefaults.StartupPageDefault,
                        () => shell.StartupPage = AppDefaults.StartupPageDefault),
                    Field(SettingsKeys.NavCollapsed, "свёрнутый навигационный рейл", Flag(AppDefaults.NavCollapsedDefault),
                        () => shell.NavCollapsed == AppDefaults.NavCollapsedDefault,
                        () => shell.NavCollapsed = AppDefaults.NavCollapsedDefault),
                    Field(SettingsKeys.WarnIfNotAdmin, "предупреждение о правах администратора", Flag(AppDefaults.WarnIfNotAdminDefault),
                        () => shell.WarnIfNotAdministrator == AppDefaults.WarnIfNotAdminDefault,
                        () => shell.WarnIfNotAdministrator = AppDefaults.WarnIfNotAdminDefault),
                ],
                []),

            new("scan",
                $"Вернуть раздел к заводским значениям. Число потоков вернётся к {scan.ParallelismCeiling} – это вдвое больше ядер этой машины, а не запомненное когда-то число.",
                [
                    Field(SettingsKeys.ScanMultithreading, "многопоточный обход", Flag(AppDefaults.ScanMultithreadingDefault),
                        () => scan.UseMultithreading == AppDefaults.ScanMultithreadingDefault,
                        () => scan.UseMultithreading = AppDefaults.ScanMultithreadingDefault),
                    Field(SettingsKeys.ScanParallelism, "число параллельных потоков", $"{scan.ParallelismCeiling}",
                        () => scan.MaxParallelism == scan.ParallelismCeiling,
                        () => scan.MaxParallelism = scan.ParallelismCeiling),
                    Field(SettingsKeys.ScanMediaAware, "учёт типа носителя", Flag(AppDefaults.ScanMediaAwareDefault),
                        () => scan.MediaAware == AppDefaults.ScanMediaAwareDefault,
                        () => scan.MediaAware = AppDefaults.ScanMediaAwareDefault),
                    Field(SettingsKeys.ScanMftEnabled, "чтение таблицы NTFS", Flag(AppDefaults.ScanMftEnabledDefault),
                        () => scan.MftEnabled == AppDefaults.ScanMftEnabledDefault,
                        () => scan.MftEnabled = AppDefaults.ScanMftEnabledDefault),
                    Field(SettingsKeys.ScanMftRootOnly, "только диск целиком", Flag(AppDefaults.ScanMftRootOnlyDefault),
                        () => scan.MftRootOnly == AppDefaults.ScanMftRootOnlyDefault,
                        () => scan.MftRootOnly = AppDefaults.ScanMftRootOnlyDefault),
                    Field(SettingsKeys.ScanIntensity, "интенсивность подсветки", $"{AppDefaults.IntensityDefault:F0}",
                        () => Math.Abs(scan.Intensity - AppDefaults.IntensityDefault) < ScaleTolerance,
                        () => scan.Intensity = AppDefaults.IntensityDefault),
                    Field(SettingsKeys.ScanRevealFiles, "открытие файлов в проводнике", Flag(AppDefaults.ScanRevealFilesDefault),
                        () => scan.RevealFiles == AppDefaults.ScanRevealFilesDefault,
                        () => scan.RevealFiles = AppDefaults.ScanRevealFilesDefault),
                ],
                []),

            new("sync",
                "Вернуть раздел к заводским значениям. Исключения по умолчанию и список группируемых каталогов останутся как есть – это введённые вами строки, и сброс их не выбрасывает.",
                [
                    Field(SettingsKeys.SyncPathSuggest, "автодополнение путей", Flag(AppDefaults.SyncPathSuggestDefault),
                        () => operations.SyncPathSuggest == AppDefaults.SyncPathSuggestDefault,
                        () => operations.SyncPathSuggest = AppDefaults.SyncPathSuggestDefault),
                    Field(SettingsKeys.SyncRecycleOverwritten, "затираемый файл – в корзину", Flag(AppDefaults.SyncRecycleOverwrittenDefault),
                        () => operations.RecycleOverwritten == AppDefaults.SyncRecycleOverwrittenDefault,
                        () => operations.RecycleOverwritten = AppDefaults.SyncRecycleOverwrittenDefault),
                    // TODO: политику git-папок пишет ещё и запрос на странице синхронизации, мимо холдеров,
                    // поэтому кнопка её строки досчитывается только при следующем заходе на страницу настроек:
                    // страница пересобирается, и новая кнопка спрашивает CanExecute заново. Заводить событие
                    // хранилища – когда мимо холдеров начнёт писаться вторая настройка страницы.
                    Field(SettingsKeys.SyncGitFolders, "git-папки", SettingsOptions.LabelOf(SettingsOptions.GitFolders, AppDefaults.SyncGitFoldersDefault),
                        () => settings.GetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault) == AppDefaults.SyncGitFoldersDefault,
                        () => settings.SetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault)),
                ],
                ["исключения по умолчанию", "группируемые каталоги"]),

            new("delete",
                "Вернуть раздел к заводским значениям: подтверждение включено, помеченное уходит в корзину.",
                [
                    Field(SettingsKeys.DeleteConfirm, "подтверждение перед удалением", Flag(AppDefaults.DeleteConfirmDefault),
                        () => operations.ConfirmBeforeDelete == AppDefaults.DeleteConfirmDefault,
                        () => operations.ConfirmBeforeDelete = AppDefaults.DeleteConfirmDefault),
                    Field(SettingsKeys.DeleteMode, "куда удалять помеченные элементы", SettingsOptions.LabelOf(SettingsOptions.DeleteModes, AppDefaults.DeleteModeDefault),
                        () => operations.DeleteMode == AppDefaults.DeleteModeDefault,
                        () => operations.DeleteMode = AppDefaults.DeleteModeDefault),
                ],
                []),

            new("archive",
                "Вернуть раздел к заводским значениям: оптимальное сжатие, оригинал после упаковки уходит в корзину.",
                [
                    Field(SettingsKeys.ArchiveCompression, "уровень сжатия .zip", SettingsOptions.LabelOf(SettingsOptions.CompressionLevels, AppDefaults.ArchiveCompressionDefault),
                        () => operations.ArchiveCompression == AppDefaults.ArchiveCompressionDefault,
                        () => operations.ArchiveCompression = AppDefaults.ArchiveCompressionDefault),
                    Field(SettingsKeys.ArchiveDeleteOriginal, "оригинал в корзину после упаковки", Flag(AppDefaults.ArchiveDeleteOriginalDefault),
                        () => operations.DeleteOriginalAfterArchive == AppDefaults.ArchiveDeleteOriginalDefault,
                        () => operations.DeleteOriginalAfterArchive = AppDefaults.ArchiveDeleteOriginalDefault),
                ],
                []),

            new("update",
                "Вернуть раздел к заводским значениям. Репозиторий обновлений останется как есть – это введённая вами строка; заводское значение подставит кнопка под полем.",
                [
                    Field(SettingsKeys.UpdateCheckOnStartup, "проверка обновлений при запуске", Flag(AppDefaults.UpdateCheckOnStartupDefault),
                        () => update.CheckOnStartup == AppDefaults.UpdateCheckOnStartupDefault,
                        () => update.CheckOnStartup = AppDefaults.UpdateCheckOnStartupDefault),
                    Field(SettingsKeys.UpdateAutoDownload, "автоматическое скачивание", Flag(AppDefaults.UpdateAutoDownloadDefault),
                        () => update.AutoDownload == AppDefaults.UpdateAutoDownloadDefault,
                        () => update.AutoDownload = AppDefaults.UpdateAutoDownloadDefault),
                ],
                ["репозиторий обновлений"]),

            new("storage",
                "Сбрасывать в этом разделе нечего: расположение данных – это перенос файлов и перезапуск приложения, такое делают галкой осознанно, а не сбросом раздела. Остальное здесь – пути и кнопки, а не настройки.",
                [],
                []),

            new("mcp",
                $"Вернуть раздел к заводским значениям. Токен доступа не сбрасывается – новый оборвал бы все уже настроенные клиенты. Порт вернётся к {AppDefaults.McpPortDefault}, поэтому строку подключения придётся раздать заново.",
                [
                    Field(SettingsKeys.McpEnabled, "сервер MCP", Flag(AppDefaults.McpEnabledDefault),
                        () => mcp.Enabled == AppDefaults.McpEnabledDefault,
                        () => mcp.Enabled = AppDefaults.McpEnabledDefault),
                    Field(SettingsKeys.McpAllowMutations, "изменяющие операции", Flag(AppDefaults.McpAllowMutationsDefault),
                        () => mcp.AllowMutations == AppDefaults.McpAllowMutationsDefault,
                        () => mcp.AllowMutations = AppDefaults.McpAllowMutationsDefault),
                    Field(SettingsKeys.McpPort, "порт", $"{AppDefaults.McpPortDefault}",
                        () => mcp.Port == AppDefaults.McpPortDefault,
                        () => mcp.Port = AppDefaults.McpPortDefault),
                ],
                ["токен доступа"]),

            new("agent",
                "Вернуть раздел к заводским значениям. Согласие, модель, глубина рассуждений и путь к CLI не сбрасываются: они свои у каждого CLI, и сброс задел бы только выбранный, оставив остальные как были.",
                [
                    Field(SettingsKeys.AgentEnabled, "страница чата", Flag(AppDefaults.AgentEnabledDefault),
                        () => agent.Enabled == AppDefaults.AgentEnabledDefault,
                        () => agent.Enabled = AppDefaults.AgentEnabledDefault),
                    Field(SettingsKeys.AgentHistoryVisible, "видимость истории", Flag(AppDefaults.AgentHistoryVisibleDefault),
                        () => agent.HistoryVisible == AppDefaults.AgentHistoryVisibleDefault,
                        () => agent.HistoryVisible = AppDefaults.AgentHistoryVisibleDefault),
                    Field(SettingsKeys.AgentTranscript, "запись транскрипта", Flag(AppDefaults.AgentTranscriptDefault),
                        () => agent.Transcript == AppDefaults.AgentTranscriptDefault,
                        () => agent.Transcript = AppDefaults.AgentTranscriptDefault),
                    Field(SettingsKeys.AgentBackend, "выбранный CLI", SettingsOptions.LabelOf(AppDefaults.AgentBackendDefault),
                        () => agent.Backend == AppDefaults.AgentBackendDefault,
                        () => agent.Backend = AppDefaults.AgentBackendDefault),
                ],
                ["согласие на отправку данных", "модель", "глубина рассуждений", "путь к CLI"]),
        ];

        EnsureFieldKeysUnique(plans);

        return plans.ToDictionary(plan => plan.SectionKey, StringComparer.Ordinal);
    }

    public static void EnsureExhaustive(SettingsSectionList sections, IReadOnlyDictionary<string, SettingsResetPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(plans);

        var missing = sections.Items
            .Select(section => section.Key)
            .Where(key => !plans.ContainsKey(key))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Разделы настроек без решения о сбросе: {string.Join(", ", missing)}. Заведите строку в SettingsResetCatalog.Build – в том числе с пустым списком настроек, если сбрасывать нечего.");
        }

        var orphans = plans.Keys
            .Where(key => sections.Items.All(section => section.Key != key))
            .ToArray();

        if (orphans.Length > 0)
        {
            throw new InvalidOperationException($"Решения о сбросе без разделов настроек: {string.Join(", ", orphans)}.");
        }
    }

    private static void EnsureFieldKeysUnique(IReadOnlyList<SettingsResetPlan> plans)
    {
        var duplicates = plans
            .SelectMany(plan => plan.Fields)
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Настройки с одинаковым ключом сброса: {string.Join(", ", duplicates)}. Кнопка строки ищется по ключу, поэтому вторая такая настройка сбрасывала бы первую.");
        }
    }

    private static string Flag(bool value)
    {
        return value ? "включено" : "выключено";
    }
}
