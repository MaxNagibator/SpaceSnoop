using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SettingsResetTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        PackScheme.Ensure();
    }

    public static IEnumerable<ResetCase> Cases =>
    [
        new("appearance",
            store =>
            {
                store.SetBool(SettingsKeys.ShowPageHeader, false);
                store.SetBool(SettingsKeys.EnableToastNotifications, false);
                store.SetDouble(SettingsKeys.FontScale, 1.4);
                store.SetBool(SettingsKeys.PerformanceHud, true);
                store.SetValue(SettingsKeys.Theme, AppThemes.DarkKey);
            },
            (bench, store) =>
            {
                var shell = bench.Shell;

                Assert.That(store.GetStringValue(SettingsKeys.Theme), Is.EqualTo(AppThemes.LightKey), "Тема осталась прежней: без окна ThemeManager.Apply ничего не делает, и единственный след сброса – ключ в хранилище.");

                Assert.That(shell.ShowPageHeader, Is.EqualTo(AppDefaults.ShowPageHeaderDefault));
                Assert.That(shell.EnableToastNotifications, Is.EqualTo(AppDefaults.ToastNotificationsDefault));
                Assert.That(shell.FontScale, Is.EqualTo(FontScaleManager.DefaultScale));
                Assert.That(shell.ShowPerformanceHud, Is.EqualTo(AppDefaults.PerformanceHudDefault));

                Assert.That(store.GetBool(SettingsKeys.ShowPageHeader), Is.EqualTo(AppDefaults.ShowPageHeaderDefault));
                Assert.That(store.GetBool(SettingsKeys.EnableToastNotifications), Is.EqualTo(AppDefaults.ToastNotificationsDefault));
                Assert.That(store.GetDouble(SettingsKeys.FontScale), Is.EqualTo(FontScaleManager.DefaultScale));
                Assert.That(store.GetBool(SettingsKeys.PerformanceHud), Is.EqualTo(AppDefaults.PerformanceHudDefault));
            }),

        new("startup",
            store =>
            {
                store.SetEnum(SettingsKeys.StartupPage, StartupPage.Logs);
                store.SetBool(SettingsKeys.NavCollapsed, true);
                store.SetBool(SettingsKeys.WarnIfNotAdmin, false);
            },
            (bench, store) =>
            {
                var shell = bench.Shell;

                Assert.That(shell.StartupPage, Is.EqualTo(AppDefaults.StartupPageDefault));
                Assert.That(shell.NavCollapsed, Is.EqualTo(AppDefaults.NavCollapsedDefault));
                Assert.That(shell.WarnIfNotAdministrator, Is.EqualTo(AppDefaults.WarnIfNotAdminDefault));

                Assert.That(store.GetEnum(SettingsKeys.StartupPage, StartupPage.Logs), Is.EqualTo(AppDefaults.StartupPageDefault));
                Assert.That(store.GetBool(SettingsKeys.NavCollapsed), Is.EqualTo(AppDefaults.NavCollapsedDefault));
                Assert.That(store.GetBool(SettingsKeys.WarnIfNotAdmin), Is.EqualTo(AppDefaults.WarnIfNotAdminDefault));
            }),

        new("scan",
            store =>
            {
                store.SetBool(SettingsKeys.ScanMultithreading, false);
                store.SetInt(SettingsKeys.ScanParallelism, 1);
                store.SetBool(SettingsKeys.ScanMediaAware, false);
                store.SetDouble(SettingsKeys.ScanIntensity, AppDefaults.IntensityMax);
                store.SetBool(SettingsKeys.ScanRevealFiles, true);
                store.SetBool(SettingsKeys.ScanMftEnabled, true);
                store.SetBool(SettingsKeys.ScanMftRootOnly, false);
            },
            (bench, store) =>
            {
                var scan = bench.Scan;

                Assert.That(scan.UseMultithreading, Is.EqualTo(AppDefaults.ScanMultithreadingDefault));
                Assert.That(scan.MaxParallelism, Is.EqualTo(scan.ParallelismCeiling));
                Assert.That(scan.MediaAware, Is.EqualTo(AppDefaults.ScanMediaAwareDefault));
                Assert.That(scan.Intensity, Is.EqualTo(AppDefaults.IntensityDefault));
                Assert.That(scan.RevealFiles, Is.EqualTo(AppDefaults.ScanRevealFilesDefault));
                Assert.That(scan.MftEnabled, Is.EqualTo(AppDefaults.ScanMftEnabledDefault));
                Assert.That(scan.MftRootOnly, Is.EqualTo(AppDefaults.ScanMftRootOnlyDefault));

                Assert.That(store.GetBool(SettingsKeys.ScanMultithreading), Is.EqualTo(AppDefaults.ScanMultithreadingDefault));
                Assert.That(store.GetInt(SettingsKeys.ScanParallelism, 1), Is.EqualTo(scan.ParallelismCeiling));
                Assert.That(store.GetBool(SettingsKeys.ScanMediaAware), Is.EqualTo(AppDefaults.ScanMediaAwareDefault));
                Assert.That(store.GetDouble(SettingsKeys.ScanIntensity), Is.EqualTo(AppDefaults.IntensityDefault));
                Assert.That(store.GetBool(SettingsKeys.ScanRevealFiles), Is.EqualTo(AppDefaults.ScanRevealFilesDefault));
                Assert.That(store.GetBool(SettingsKeys.ScanMftEnabled), Is.EqualTo(AppDefaults.ScanMftEnabledDefault));
                Assert.That(store.GetBool(SettingsKeys.ScanMftRootOnly), Is.EqualTo(AppDefaults.ScanMftRootOnlyDefault));
            }),

        new("sync",
            store =>
            {
                store.SetBool(SettingsKeys.SyncPathSuggest, false);
                store.SetBool(SettingsKeys.SyncRecycleOverwritten, false);
            },
            (bench, store) =>
            {
                var operations = bench.Operations;

                Assert.That(operations.SyncPathSuggest, Is.EqualTo(AppDefaults.SyncPathSuggestDefault));
                Assert.That(operations.RecycleOverwritten, Is.EqualTo(AppDefaults.SyncRecycleOverwrittenDefault));

                Assert.That(store.GetBool(SettingsKeys.SyncPathSuggest), Is.EqualTo(AppDefaults.SyncPathSuggestDefault));
                Assert.That(store.GetBool(SettingsKeys.SyncRecycleOverwritten), Is.EqualTo(AppDefaults.SyncRecycleOverwrittenDefault));
            }),

        new("delete",
            store =>
            {
                store.SetBool(SettingsKeys.DeleteConfirm, false);
                store.SetEnum(SettingsKeys.DeleteMode, DeleteMode.Permanent);
            },
            (bench, store) =>
            {
                var operations = bench.Operations;

                Assert.That(operations.ConfirmBeforeDelete, Is.EqualTo(AppDefaults.DeleteConfirmDefault));
                Assert.That(operations.DeleteMode, Is.EqualTo(AppDefaults.DeleteModeDefault));

                Assert.That(store.GetBool(SettingsKeys.DeleteConfirm), Is.EqualTo(AppDefaults.DeleteConfirmDefault));
                Assert.That(store.GetEnum(SettingsKeys.DeleteMode, DeleteMode.Permanent), Is.EqualTo(AppDefaults.DeleteModeDefault));
            }),

        new("archive",
            store =>
            {
                store.SetBool(SettingsKeys.ArchiveDeleteOriginal, false);
                store.SetEnum(SettingsKeys.ArchiveCompression, CompressionLevel.NoCompression);
            },
            (bench, store) =>
            {
                var operations = bench.Operations;

                Assert.That(operations.DeleteOriginalAfterArchive, Is.EqualTo(AppDefaults.ArchiveDeleteOriginalDefault));
                Assert.That(operations.ArchiveCompression, Is.EqualTo(AppDefaults.ArchiveCompressionDefault));

                Assert.That(store.GetBool(SettingsKeys.ArchiveDeleteOriginal), Is.EqualTo(AppDefaults.ArchiveDeleteOriginalDefault));
                Assert.That(store.GetEnum(SettingsKeys.ArchiveCompression, CompressionLevel.NoCompression), Is.EqualTo(AppDefaults.ArchiveCompressionDefault));
            }),

        new("update",
            store =>
            {
                store.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
                store.SetBool(SettingsKeys.UpdateAutoDownload, true);
            },
            (bench, store) =>
            {
                var update = bench.Update;

                Assert.That(update.CheckOnStartup, Is.EqualTo(AppDefaults.UpdateCheckOnStartupDefault));
                Assert.That(update.AutoDownload, Is.EqualTo(AppDefaults.UpdateAutoDownloadDefault));

                Assert.That(store.GetBool(SettingsKeys.UpdateCheckOnStartup), Is.EqualTo(AppDefaults.UpdateCheckOnStartupDefault));
                Assert.That(store.GetBool(SettingsKeys.UpdateAutoDownload), Is.EqualTo(AppDefaults.UpdateAutoDownloadDefault));
            }),

        new("mcp",
            store =>
            {
                store.SetBool(SettingsKeys.McpEnabled, true);
                store.SetInt(SettingsKeys.McpPort, 9123);
                store.SetBool(SettingsKeys.McpAllowMutations, true);
            },
            (bench, store) =>
            {
                var mcp = bench.Mcp;

                Assert.That(mcp.Enabled, Is.EqualTo(AppDefaults.McpEnabledDefault));
                Assert.That(mcp.Port, Is.EqualTo(AppDefaults.McpPortDefault));
                Assert.That(mcp.AllowMutations, Is.EqualTo(AppDefaults.McpAllowMutationsDefault));

                Assert.That(store.GetBool(SettingsKeys.McpEnabled), Is.EqualTo(AppDefaults.McpEnabledDefault));
                Assert.That(store.GetInt(SettingsKeys.McpPort, 0), Is.EqualTo(AppDefaults.McpPortDefault));
                Assert.That(store.GetBool(SettingsKeys.McpAllowMutations), Is.EqualTo(AppDefaults.McpAllowMutationsDefault));
            }),

        new("agent",
            store =>
            {
                store.SetBool(SettingsKeys.AgentEnabled, false);
                store.SetBool(SettingsKeys.AgentHistoryVisible, true);
                store.SetBool(SettingsKeys.AgentTranscript, true);
                store.SetEnum(SettingsKeys.AgentBackend, AgentBackendKind.OpenCode);
            },
            (bench, store) =>
            {
                var agent = bench.Agent;

                Assert.That(agent.Enabled, Is.EqualTo(AppDefaults.AgentEnabledDefault));
                Assert.That(agent.HistoryVisible, Is.EqualTo(AppDefaults.AgentHistoryVisibleDefault));
                Assert.That(agent.Transcript, Is.EqualTo(AppDefaults.AgentTranscriptDefault));
                Assert.That(agent.Backend, Is.EqualTo(AppDefaults.AgentBackendDefault));

                Assert.That(store.GetBool(SettingsKeys.AgentEnabled), Is.EqualTo(AppDefaults.AgentEnabledDefault));
                Assert.That(store.GetBool(SettingsKeys.AgentHistoryVisible), Is.EqualTo(AppDefaults.AgentHistoryVisibleDefault));
                Assert.That(store.GetBool(SettingsKeys.AgentTranscript), Is.EqualTo(AppDefaults.AgentTranscriptDefault));
                Assert.That(store.GetEnum(SettingsKeys.AgentBackend, AgentBackendKind.OpenCode), Is.EqualTo(AppDefaults.AgentBackendDefault));
            }),
    ];

    [TestCaseSource(nameof(Cases))]
    public void Сброс_раздела_возвращает_его_настройки_к_умолчанию_и_доводит_их_до_хранилища(ResetCase testCase)
    {
        ISettingsStore store = new MemorySettings();
        testCase.Seed(store);

        var bench = Build(store);
        bench.Plans[testCase.Name].Reset();

        using (Assert.EnterMultipleScope())
        {
            testCase.Verify(bench, store);
        }
    }

    [Test]
    public void Число_потоков_сбрасывается_к_пересчитанному_потолку_а_не_к_запомненному_числу()
    {
        ISettingsStore store = new MemorySettings();
        var ceiling = Environment.ProcessorCount * AppDefaults.ScanParallelismPerCore;

        store.SetInt(SettingsKeys.ScanParallelism, 1);

        var bench = Build(store);
        var scan = bench.Scan;

        Assert.That(scan.MaxParallelism, Is.EqualTo(1), "Затравка не доехала – сбрасывать нечего.");

        bench.Plans["scan"].Reset();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.MaxParallelism, Is.EqualTo(ceiling), "Потолок посчитан не по этой машине: умолчание для параллелизма вычисляемое, а не константа.");
            Assert.That(store.GetInt(SettingsKeys.ScanParallelism, 0), Is.EqualTo(ceiling));
        }
    }

    [Test]
    public void Число_потоков_выше_потолка_нормализуется_в_хранилище_а_не_только_в_памяти()
    {
        ISettingsStore store = new MemorySettings();
        var ceiling = Environment.ProcessorCount * AppDefaults.ScanParallelismPerCore;

        store.SetInt(SettingsKeys.ScanParallelism, ceiling * 4);

        var bench = Build(store);
        var scan = bench.Scan;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.MaxParallelism, Is.EqualTo(ceiling));
            Assert.That(store.GetInt(SettingsKeys.ScanParallelism, 0), Is.EqualTo(ceiling), "Кламп конструктора не доехал до хранилища: сброс к тому же числу окажется молчаливым no-op.");
        }

        bench.Plans["scan"].Reset();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.MaxParallelism, Is.EqualTo(ceiling));
            Assert.That(store.GetInt(SettingsKeys.ScanParallelism, 0), Is.EqualTo(ceiling));
        }
    }

    [Test]
    public void Порт_MCP_вне_допустимого_диапазона_нормализуется_в_хранилище()
    {
        ISettingsStore store = new MemorySettings();

        store.SetInt(SettingsKeys.McpPort, AppDefaults.McpPortMax + 1000);

        var mcp = new McpPreferences(store);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mcp.Port, Is.EqualTo(AppDefaults.McpPortMax));
            Assert.That(store.GetInt(SettingsKeys.McpPort, 0), Is.EqualTo(AppDefaults.McpPortMax));
        }
    }

    [Test]
    public void Сброс_не_выбрасывает_введённое_руками()
    {
        ISettingsStore store = new MemorySettings();

        store.SetValue(SettingsKeys.DefaultExclusions, "bin,obj,*.tmp");
        store.SetValue(SettingsKeys.SyncGroupFolders, "node_modules");
        store.SetValue(SettingsKeys.UpdateRepository, "someone/fork");
        store.SetValue(SettingsKeys.McpToken, "секрет-который-уже-роздан");
        store.SetValue(SettingsKeys.AgentModel(AgentBackendKind.Claude), "opus");
        store.SetValue(SettingsKeys.AgentCliPath(AgentBackendKind.Claude), @"D:\tools\claude.exe");
        store.SetBool(SettingsKeys.AgentConsent(AgentBackendKind.Claude), true);
        store.SetValue(SettingsKeys.AgentEffort(AgentBackendKind.Claude), "xhigh");

        var bench = Build(store);
        var operations = bench.Operations;
        var update = bench.Update;
        var mcp = bench.Mcp;
        var agent = bench.Agent;

        foreach (var plan in bench.Plans.Values)
        {
            plan.Reset();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operations.DefaultExclusions, Is.EqualTo("bin,obj,*.tmp"), "Сброс выбросил исключения по умолчанию.");
            Assert.That(operations.GroupFolders, Is.EqualTo("node_modules"), "Сброс выбросил список группируемых каталогов.");
            Assert.That(update.Repository, Is.EqualTo("someone/fork"), "Сброс выбросил репозиторий обновлений.");
            Assert.That(mcp.Token, Is.EqualTo("секрет-который-уже-роздан"), "Сброс перевыпустил токен – все настроенные клиенты оборваны.");
            Assert.That(agent.ModelFor(AgentBackendKind.Claude), Is.EqualTo("opus"));
            Assert.That(agent.CliPathFor(AgentBackendKind.Claude), Is.EqualTo(@"D:\tools\claude.exe"));
            Assert.That(agent.EffortFor(AgentBackendKind.Claude), Is.EqualTo("xhigh"));
            Assert.That(agent.ConsentFor(AgentBackendKind.Claude), Is.True, "Сброс отозвал согласие только у выбранного CLI – половинчатое действие.");
        }
    }

    [Test]
    public void Сброс_синхронизации_возвращает_к_умолчанию_и_политику_git_папок()
    {
        ISettingsStore store = new MemorySettings();
        store.SetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Skip);

        Build(store).Plans["sync"].Reset();

        Assert.That(store.GetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Skip), Is.EqualTo(AppDefaults.SyncGitFoldersDefault),
            "Политика git-папок живёт мимо держателей настроек, и сброс раздела о ней забыл.");
    }

    [Test]
    public void Разделу_расположения_данных_сбрасывать_нечего_и_он_об_этом_говорит()
    {
        var plan = Build(new MemorySettings()).Plans["storage"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.CanReset, Is.False, "Сброс расположения данных перенёс бы файлы и перезапустил приложение.");
            Assert.That(plan.Hint, Is.Not.Empty, "Выключенная кнопка обязана объяснить себя, а не молчать.");
        }
    }

    [Test]
    public void Раздел_без_решения_о_сбросе_роняет_сборку_страницы_настроек_а_не_отдаёт_мёртвую_кнопку()
    {
        var plans = Build(new MemorySettings()).Plans;

        var withNewSection = new SettingsSectionList(
            new SettingsSection("appearance", "Внешний вид", PackIconLucideKind.Palette, string.Empty),
            new SettingsSection("шпионаж", "Шпионаж", PackIconLucideKind.Eye, string.Empty));

        var exception = Assert.Throws<InvalidOperationException>(() => SettingsResetCatalog.EnsureExhaustive(withNewSection, plans));

        Assert.That(exception.Message, Does.Contain("шпионаж"));
    }

    [Test]
    public void Все_разделы_страницы_настроек_имеют_решение_о_сбросе()
    {
        ISettingsStore store = new MemorySettings();

        Assert.DoesNotThrow(() => SettingsResetCatalog.EnsureExhaustive(SettingsViewModel.CreateSections(), Build(store).Plans));
    }

    public static IEnumerable<string> FieldKeys => Build(new MemorySettings()).Fields.Select(entry => entry.Key).ToArray();

    [TestCaseSource(nameof(FieldKeys))]
    public void Кнопка_строки_молчит_пока_настройка_стоит_на_заводском_значении(string key)
    {
        var field = Build(new MemorySettings()).Field(key);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(field.IsDefault, Is.True, $"Хранилище пустое, а «{field.Label}» считается изменённой: предикат сравнивает не с тем умолчанием.");
            Assert.That(field.ResetCommand.CanExecute(null), Is.False, $"Строка «{field.Label}» предлагает вернуть значение, которое и так заводское.");
        }
    }

    [Test]
    public void Изменённые_настройки_включают_кнопки_своих_строк()
    {
        ISettingsStore store = new MemorySettings();
        SeedEverything(store);

        var bench = Build(store);

        using (Assert.EnterMultipleScope())
        {
            foreach (var field in bench.Fields.Where(field => field.Key != SettingsKeys.Theme))
            {
                Assert.That(field.ResetCommand.CanExecute(null), Is.True, $"«{field.Label}» изменена, а кнопка её строки спрятана: предикат умолчания не видит правку.");
            }
        }
    }

    [Test]
    public void Сброс_строки_возвращает_к_умолчанию_только_свою_настройку()
    {
        ISettingsStore store = new MemorySettings();
        SeedEverything(store);

        var bench = Build(store);
        bench.Field(SettingsKeys.ScanParallelism).ResetCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bench.Scan.MaxParallelism, Is.EqualTo(bench.Scan.ParallelismCeiling));
            Assert.That(store.GetInt(SettingsKeys.ScanParallelism, 0), Is.EqualTo(bench.Scan.ParallelismCeiling), "Сброс строки не доехал до хранилища.");
            Assert.That(bench.Scan.MftEnabled, Is.True, "Сброс одной строки задел соседнюю настройку того же раздела.");
            Assert.That(bench.Scan.RevealFiles, Is.True, "Сброс одной строки задел соседнюю настройку того же раздела.");
            Assert.That(bench.Reset.Select(field => field.Key), Is.EqualTo(new[] { SettingsKeys.ScanParallelism }), "Команда строки доложила не о той настройке или сразу о нескольких.");
        }
    }

    [Test]
    public void Каждая_настройка_раздела_сбрасывается_своей_строкой()
    {
        ISettingsStore store = new MemorySettings();
        SeedEverything(store);

        var bench = Build(store);

        using (Assert.EnterMultipleScope())
        {
            foreach (var plan in bench.Plans.Values)
            {
                Assert.That(plan.Restored, Is.EqualTo(plan.Fields.Select(field => field.Label).ToArray()), $"Раздел «{plan.SectionKey}» перечисляет в тосте не то, что сбрасывает.");
            }

            foreach (var plan in bench.Plans.Values)
            {
                plan.Reset();
            }

            foreach (var field in bench.Fields.Where(field => field.Key != SettingsKeys.Theme))
            {
                Assert.That(field.IsDefault, Is.True, $"После сброса всех разделов «{field.Label}» осталась изменённой.");
            }
        }
    }

    private static void SeedEverything(ISettingsStore store)
    {
        store.SetBool(SettingsKeys.ShowPageHeader, false);
        store.SetBool(SettingsKeys.EnableToastNotifications, false);
        store.SetDouble(SettingsKeys.FontScale, 1.4);
        store.SetBool(SettingsKeys.PerformanceHud, true);
        store.SetValue(SettingsKeys.Theme, AppThemes.DarkKey);

        store.SetEnum(SettingsKeys.StartupPage, StartupPage.Logs);
        store.SetBool(SettingsKeys.NavCollapsed, true);
        store.SetBool(SettingsKeys.WarnIfNotAdmin, false);

        store.SetBool(SettingsKeys.ScanMultithreading, false);
        store.SetInt(SettingsKeys.ScanParallelism, 1);
        store.SetBool(SettingsKeys.ScanMediaAware, false);
        store.SetDouble(SettingsKeys.ScanIntensity, AppDefaults.IntensityMax);
        store.SetBool(SettingsKeys.ScanRevealFiles, true);
        store.SetBool(SettingsKeys.ScanMftEnabled, true);
        store.SetBool(SettingsKeys.ScanMftRootOnly, false);

        store.SetBool(SettingsKeys.SyncPathSuggest, false);
        store.SetBool(SettingsKeys.SyncRecycleOverwritten, false);
        store.SetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Skip);

        store.SetBool(SettingsKeys.DeleteConfirm, false);
        store.SetEnum(SettingsKeys.DeleteMode, DeleteMode.Permanent);

        store.SetBool(SettingsKeys.ArchiveDeleteOriginal, false);
        store.SetEnum(SettingsKeys.ArchiveCompression, CompressionLevel.NoCompression);

        store.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        store.SetBool(SettingsKeys.UpdateAutoDownload, true);

        store.SetBool(SettingsKeys.McpEnabled, true);
        store.SetInt(SettingsKeys.McpPort, 9123);
        store.SetBool(SettingsKeys.McpAllowMutations, true);

        store.SetBool(SettingsKeys.AgentEnabled, false);
        store.SetBool(SettingsKeys.AgentHistoryVisible, true);
        store.SetBool(SettingsKeys.AgentTranscript, true);
        store.SetEnum(SettingsKeys.AgentBackend, AgentBackendKind.OpenCode);
    }

    private static Bench Build(ISettingsStore store)
    {
        PackScheme.Ensure();

        var theme = new ThemeViewModel(store);
        var shell = new ShellPreferences(store);
        var scan = new ScanPreferences(store);
        var operations = new OperationPreferences(store);
        var update = new UpdatePreferences(store);
        var mcp = new McpPreferences(store);
        var agent = new AgentPreferences(store);
        var reset = new List<SettingsResetField>();

        var plans = SettingsResetCatalog.Build(theme, shell, scan, operations, update, mcp, agent, store, field =>
        {
            reset.Add(field);
            field.Apply();
        });

        return new(shell, scan, operations, update, mcp, agent, plans, reset);
    }

    public sealed record Bench(
        ShellPreferences Shell,
        ScanPreferences Scan,
        OperationPreferences Operations,
        UpdatePreferences Update,
        McpPreferences Mcp,
        AgentPreferences Agent,
        IReadOnlyDictionary<string, SettingsResetPlan> Plans,
        IReadOnlyList<SettingsResetField> Reset)
    {
        public IEnumerable<SettingsResetField> Fields => Plans.Values.SelectMany(plan => plan.Fields);

        public SettingsResetField Field(string key)
        {
            return Fields.First(field => field.Key == key);
        }
    }

    public sealed record ResetCase(
        string Name,
        Action<ISettingsStore> Seed,
        Action<Bench, ISettingsStore> Verify)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
