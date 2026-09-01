using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public static class SettingsResetCatalog
{
    public static IReadOnlyDictionary<string, SettingsResetPlan> Build(
        ThemeViewModel theme,
        ShellPreferences shell,
        ScanPreferences scan,
        OperationPreferences operations,
        UpdatePreferences update,
        McpPreferences mcp,
        AgentPreferences agent,
        ISettingsStore settings)
    {
        SettingsResetPlan[] plans =
        [
            new("appearance",
                "Вернуть раздел к заводским значениям: светлая тема, масштаб 100 %, заголовок страницы и уведомления включены, строка производительности скрыта.",
                ["тема оформления", "масштаб шрифта", "заголовок страницы", "всплывающие уведомления", "производительность в строке состояния"],
                [],
                () =>
                {
                    theme.ApplyCommand.Execute(AppDefaults.ThemeDefault);
                    shell.ResetAppearanceToDefaults();
                }),

            new("startup",
                "Вернуть раздел к заводским значениям: стартовая страница «Сканирование», рейл развёрнут, предупреждение о правах администратора включено.",
                ["стартовая страница", "свёрнутый навигационный рейл", "предупреждение о правах администратора"],
                [],
                shell.ResetStartupToDefaults),

            new("scan",
                $"Вернуть раздел к заводским значениям. Число потоков вернётся к {scan.ParallelismCeiling} – это вдвое больше ядер этой машины, а не запомненное когда-то число.",
                ["многопоточный обход", "число параллельных потоков", "учёт типа носителя", "чтение таблицы NTFS", "только диск целиком", "интенсивность подсветки", "открытие файлов в проводнике"],
                [],
                scan.ResetToDefaults),

            new("sync",
                "Вернуть раздел к заводским значениям. Исключения по умолчанию и список группируемых каталогов останутся как есть – это введённые вами строки, и сброс их не выбрасывает.",
                ["автодополнение путей", "затираемый файл – в корзину", "git-папки"],
                ["исключения по умолчанию", "группируемые каталоги"],
                () =>
                {
                    operations.ResetSyncToDefaults();
                    settings.SetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault);
                }),

            new("delete",
                "Вернуть раздел к заводским значениям: подтверждение включено, помеченное уходит в корзину.",
                ["подтверждение перед удалением", "куда удалять помеченные элементы"],
                [],
                operations.ResetDeleteToDefaults),

            new("archive",
                "Вернуть раздел к заводским значениям: оптимальное сжатие, оригинал после упаковки уходит в корзину.",
                ["уровень сжатия .zip", "оригинал в корзину после упаковки"],
                [],
                operations.ResetArchiveToDefaults),

            new("update",
                "Вернуть раздел к заводским значениям. Репозиторий обновлений останется как есть – это введённая вами строка; заводское значение подставит кнопка под полем.",
                ["проверка обновлений при запуске", "автоматическое скачивание"],
                ["репозиторий обновлений"],
                update.ResetToDefaults),

            new("storage",
                "Сбрасывать в этом разделе нечего: расположение данных – это перенос файлов и перезапуск приложения, такое делают галкой осознанно, а не сбросом раздела. Остальное здесь – пути и кнопки, а не настройки.",
                [],
                [],
                null),

            new("mcp",
                $"Вернуть раздел к заводским значениям. Токен доступа не сбрасывается – новый оборвал бы все уже настроенные клиенты. Порт вернётся к {AppDefaults.McpPortDefault}, поэтому строку подключения придётся раздать заново.",
                ["сервер выключен", "изменяющие операции запрещены", "порт"],
                ["токен доступа"],
                mcp.ResetToDefaults),

            new("agent",
                "Вернуть раздел к заводским значениям. Согласие, модель, глубина рассуждений и путь к CLI не сбрасываются: они свои у каждого CLI, и сброс задел бы только выбранный, оставив остальные как были.",
                ["страница чата", "видимость истории", "запись транскрипта", "выбранный CLI"],
                ["согласие на отправку данных", "модель", "глубина рассуждений", "путь к CLI"],
                agent.ResetToDefaults),
        ];

        return plans.ToDictionary(plan => plan.SectionKey, StringComparer.Ordinal);
    }

    public static void EnsureExhaustive(SettingsSectionList sections, IReadOnlyDictionary<string, SettingsResetPlan> plans)
    {
        var missing = sections.Items
            .Select(section => section.Key)
            .Where(key => !plans.ContainsKey(key))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Разделы настроек без решения о сбросе: {string.Join(", ", missing)}. Заведите строку в SettingsResetCatalog.Build – в том числе с Reset = null, если сбрасывать нечего.");
        }

        var orphans = plans.Keys
            .Where(key => sections.Items.All(section => section.Key != key))
            .ToArray();

        if (orphans.Length > 0)
        {
            throw new InvalidOperationException($"Решения о сбросе без разделов настроек: {string.Join(", ", orphans)}.");
        }
    }
}
