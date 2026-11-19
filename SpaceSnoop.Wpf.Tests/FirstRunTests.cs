using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using KeepShell.ViewModels;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class FirstRunTests
{
    [TestCase(false, false, StartupAdminMode.Ask, 1, 0, TestName = "Вопрос задаётся на повторном запуске без прав")]
    [TestCase(false, true, StartupAdminMode.Ask, 0, 0, TestName = "Вопрос не задаётся, пока ждёт карточка")]
    [TestCase(true, false, StartupAdminMode.Ask, 0, 0, TestName = "Вопрос не задаётся администратору")]
    [TestCase(false, false, StartupAdminMode.WithoutAdmin, 0, 0, TestName = "Вопрос не задаётся в режиме продолжения без прав")]
    [TestCase(false, false, StartupAdminMode.AlwaysAdmin, 0, 1, TestName = "В режиме постоянного запуска с правами выполняется перезапуск без вопроса")]
    public async Task Вопрос_о_правах_показывается_только_по_решению(
        bool isElevated,
        bool welcomePending,
        StartupAdminMode mode,
        int expectedShows,
        int expectedRestarts)
    {
        var settings = new MemorySettings();
        ((ISettingsStore)settings).SetEnum(SettingsKeys.StartupAdmin, mode);
        var dialogs = new NoopDialogs();
        var restarts = 0;
        var prompt = new AdminStartupPrompt(
            dialogs,
            new(settings),
            () =>
            {
                restarts++;
                return Task.CompletedTask;
            });

        await prompt.RunAsync(isElevated, welcomePending);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialogs.ShowCount, Is.EqualTo(expectedShows));
            Assert.That(restarts, Is.EqualTo(expectedRestarts));
        }
    }

    [Test]
    public void Галки_карточки_пишут_те_же_настройки_что_и_раздел_настроек()
    {
        var settings = PendingSettings();
        var card = Card(settings, true, () => Task.FromResult(false));

        card.FastScan = true;
        card.CheckUpdates = false;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled), Is.True);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.UpdateCheckOnStartup, true), Is.False);
            Assert.That(card.IsVisible, Is.True);
        }
    }

    [Test]
    public void Закрытие_карточки_снимает_отметку_и_не_трогает_быстрый_скан()
    {
        var settings = PendingSettings();
        var card = Card(settings, false, () => Task.FromResult(false));

        card.DismissCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(card.IsVisible, Is.False);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending, true), Is.False);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled), Is.EqualTo(AppDefaults.ScanMftEnabledDefault));
            Assert.That(Card(settings, false, () => Task.FromResult(false)).IsVisible, Is.False);
        }
    }

    [TestCase(null, false, TestName = "Ключа нет – карточки нет (профиль старше карточки)")]
    [TestCase("true", true, TestName = "Отметка первого запуска – карточка видна")]
    [TestCase("false", false, TestName = "Карточку закрывали – карточки нет")]
    public void Карточка_видна_по_отметке(string? stored, bool expected)
    {
        var settings = new MemorySettings();

        if (stored is not null)
        {
            settings.SetValue(SettingsKeys.WelcomePending, stored);
        }

        Assert.That(Card(settings, false, () => Task.FromResult(false)).IsVisible, Is.EqualTo(expected));
    }

    [TestCase(0, false, StartupAdminMode.Ask, 0, TestName = "«Не перезапускать» без галки – спросит и в следующий раз")]
    [TestCase(0, true, StartupAdminMode.WithoutAdmin, 0, TestName = "«Не перезапускать» с галкой – запоминает продолжение без прав")]
    [TestCase(1, false, StartupAdminMode.Ask, 1, TestName = "«Перезапустить» без галки – перезапуск без смены настройки")]
    [TestCase(1, true, StartupAdminMode.AlwaysAdmin, 1, TestName = "«Перезапустить» с галкой – запоминает постоянный запуск от администратора")]
    public async Task Ответ_на_вопрос_о_правах(
        int choiceIndex,
        bool rememberChoice,
        StartupAdminMode expectedMode,
        int expectedRestarts)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopFirstRun", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var settingsPath = Path.Combine(directory, TomlSettingsFile.PrimaryFileName);
            using var settings = new SettingsStore(settingsPath);
            var restarts = 0;
            StartupAdminMode? persistedAtRestart = null;
            var dialogs = new NoopDialogs(
                show: viewModel =>
                {
                    var confirm = (ConfirmDialogViewModel)viewModel;
                    confirm.RememberChoice = rememberChoice;
                    var choice = confirm.Choices[choiceIndex];
                    confirm.ChooseCommand.Execute(choice);

                    return !choice.IsDismissive;
                });

            var shell = new ShellPreferences(settings);
            var prompt = new AdminStartupPrompt(
                dialogs,
                shell,
                () =>
                {
                    restarts++;
                    using var disk = new SettingsStore(settingsPath);
                    persistedAtRestart = ((ISettingsStore)disk).GetEnum(SettingsKeys.StartupAdmin, AppDefaults.StartupAdminDefault);
                    return Task.CompletedTask;
                });

            await prompt.RunAsync(false, false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(shell.StartupAdmin, Is.EqualTo(expectedMode));
                Assert.That(((ISettingsStore)settings).GetEnum(SettingsKeys.StartupAdmin, AppDefaults.StartupAdminDefault), Is.EqualTo(expectedMode));
                Assert.That(restarts, Is.EqualTo(expectedRestarts));

                if (expectedRestarts > 0)
                {
                    Assert.That(persistedAtRestart, Is.EqualTo(expectedMode));
                }
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase(true, StartupAdminMode.Ask, TestName = "Старый ключ wpf.startup.admin_warning = true мигрирует в Ask")]
    [TestCase(false, StartupAdminMode.WithoutAdmin, TestName = "Старый ключ wpf.startup.admin_warning = false мигрирует в WithoutAdmin")]
    public void Миграция_булева_ключа_предупреждения_о_правах(bool legacyValue, StartupAdminMode expectedMode)
    {
        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.WarnIfNotAdmin, legacyValue);

        var shell = new ShellPreferences(settings);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell.StartupAdmin, Is.EqualTo(expectedMode));
            Assert.That(settings.GetEnum(SettingsKeys.StartupAdmin, StartupAdminMode.None), Is.EqualTo(expectedMode));
            Assert.That(settings.GetStringValue(SettingsKeys.WarnIfNotAdmin), Is.Empty);
        }
    }

    [TestCase("None")]
    [TestCase("0")]
    public void Нулевое_значение_политики_запуска_заменяется_значением_по_умолчанию(string rawValue)
    {
        ISettingsStore settings = new MemorySettings();
        settings.SetValue(SettingsKeys.StartupAdmin, rawValue);

        var shell = new ShellPreferences(settings);

        Assert.That(shell.StartupAdmin, Is.EqualTo(AppDefaults.StartupAdminDefault));
    }

    [Test]
    public async Task Отказ_в_UAC_при_постоянном_запуске_от_администратора_показывает_предупреждение_без_повторного_вопроса()
    {
        ISettingsStore settings = new MemorySettings();
        settings.SetEnum(SettingsKeys.StartupAdmin, StartupAdminMode.AlwaysAdmin);

        var shell = new ShellPreferences(settings);
        var dialogs = new NoopDialogs();
        var restarts = 0;
        var toasts = new ToastHostViewModel();
        var notifier = new ToastNotifier(toasts, shell);
        var prompt = new AdminStartupPrompt(
            dialogs,
            shell,
            () =>
            {
                restarts++;
                return Task.CompletedTask;
            },
            notifier);

        await prompt.RunAsync(isElevated: false, welcomePending: false, elevationRefused: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialogs.ShowCount, Is.EqualTo(0));
            Assert.That(restarts, Is.EqualTo(0));
            Assert.That(shell.StartupAdmin, Is.EqualTo(StartupAdminMode.AlwaysAdmin));
            Assert.That(
                toasts.Toasts.Select(static toast => (toast.Message, toast.Severity)),
                Is.EqualTo(new[] { (AdminRestartPrompt.RefusedMessage, StatusSeverity.Warning) }));
        }
    }

    [TestCase(true, false, TestName = "Перезапуск из карточки включает быстрый скан и оставляет карточку до явного «Готово»")]
    [TestCase(false, false, TestName = "Несостоявшийся перезапуск оставляет карточку и возвращает выключенный быстрый скан")]
    [TestCase(false, true, TestName = "Несостоявшийся перезапуск оставляет включённый заранее быстрый скан")]
    public async Task Перезапуск_из_карточки(bool restarted, bool fastScanBefore)
    {
        var settings = PendingSettings();
        ((ISettingsStore)settings).SetBool(SettingsKeys.ScanMftEnabled, fastScanBefore);
        bool? pendingAtRestart = null;
        bool? fastScanAtRestart = null;
        var card = Card(
            settings,
            false,
            () =>
            {
                pendingAtRestart = ((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending, true);
                fastScanAtRestart = ((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled);
                return Task.FromResult(restarted);
            });

        await card.RestartAsAdminCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(pendingAtRestart, Is.True);
            Assert.That(fastScanAtRestart, Is.True);
            Assert.That(card.IsVisible, Is.True);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending), Is.True);
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanMftEnabled), Is.EqualTo(restarted || fastScanBefore));
            Assert.That(card.FastScan, Is.EqualTo(restarted || fastScanBefore));
        }
    }

    [Test]
    public void Переносной_каталог_со_старым_settings_txt_открывается_без_карточки()
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoopFirstRun", Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exe);
        Directory.CreateDirectory(appData);

        try
        {
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), $"{SettingsKeys.FontScale} 1.25\n");

            var useAppData = AppStorage.Resolve(exe, appData, isPortableBuild: false);
            var dataDirectory = useAppData ? appData : exe;
            var freshProfile = !AppStorage.HasSettings(dataDirectory);

            using var store = new SettingsStore(Path.Combine(dataDirectory, TomlSettingsFile.PrimaryFileName));
            ISettingsStore settings = store;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(useAppData, Is.False);
                Assert.That(freshProfile, Is.False);
                Assert.That(settings.GetDouble(SettingsKeys.FontScale, 1.0), Is.EqualTo(1.25));
                Assert.That(Card(settings, false, () => Task.FromResult(false)).IsVisible, Is.False);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task После_перезапуска_от_администратора_карточка_показывается_с_правами_и_закрывается_только_явно()
    {
        var settings = PendingSettings();
        await Card(settings, false, () => Task.FromResult(true)).RestartAsAdminCommand.ExecuteAsync(null);

        var elevated = Card(settings, true, () => Task.FromResult(false));
        var visibleAfterRestart = elevated.IsVisible;
        var needsElevation = elevated.NeedsElevation;
        var fastScan = elevated.FastScan;
        var decision = AdminStartupPrompt.Decide(true, ((ISettingsStore)settings).GetBool(SettingsKeys.WelcomePending), StartupAdminMode.Ask);
        elevated.DismissCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(visibleAfterRestart, Is.True);
            Assert.That(needsElevation, Is.False);
            Assert.That(fastScan, Is.True);
            Assert.That(decision, Is.EqualTo(StartupAdminAction.WelcomeCard));
            Assert.That(elevated.IsVisible, Is.False);
            Assert.That(Card(settings, true, () => Task.FromResult(false)).IsVisible, Is.False);
        }
    }

    [TestCase(true, false, true, TestName = "Проверка обновлений на старте идёт, когда карточка закрыта")]
    [TestCase(true, true, false, TestName = "Проверка обновлений на старте ждёт закрытия карточки первого запуска")]
    [TestCase(false, false, false, TestName = "Выключенная проверка обновлений на старте не идёт")]
    public void Проверка_обновлений_на_старте(bool checkOnStartup, bool welcomePending, bool expected)
    {
        Assert.That(AppUpdateViewModel.ChecksOnStartup(checkOnStartup, welcomePending), Is.EqualTo(expected));
    }

    [Test]
    public void Профиль_без_файла_настроек_считается_новым()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopFirstRun", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var fresh = AppStorage.HasSettings(directory);
            File.WriteAllText(Path.Combine(directory, TomlSettingsFile.LegacyFileName), string.Empty);
            var legacy = AppStorage.HasSettings(directory);
            File.Delete(Path.Combine(directory, TomlSettingsFile.LegacyFileName));
            File.WriteAllText(Path.Combine(directory, TomlSettingsFile.PrimaryFileName), string.Empty);
            var primary = AppStorage.HasSettings(directory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fresh, Is.False);
                Assert.That(legacy, Is.True);
                Assert.That(primary, Is.True);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase(false, true, StartupAdminMode.Ask, false, StartupAdminAction.WelcomeCard, TestName = "Первый запуск без прав – карточка вместо вопроса")]
    [TestCase(true, true, StartupAdminMode.Ask, false, StartupAdminAction.WelcomeCard, TestName = "Первый запуск от администратора – карточка")]
    [TestCase(false, false, StartupAdminMode.Ask, false, StartupAdminAction.Question, TestName = "Повторный запуск без прав – вопрос о правах")]
    [TestCase(false, false, StartupAdminMode.WithoutAdmin, false, StartupAdminAction.None, TestName = "Повторный запуск в режиме без прав – ни карточки, ни вопроса")]
    [TestCase(false, false, StartupAdminMode.AlwaysAdmin, false, StartupAdminAction.Restart, TestName = "Повторный запуск в режиме всегда с правами – перезапуск")]
    [TestCase(false, false, StartupAdminMode.AlwaysAdmin, true, StartupAdminAction.None, TestName = "Отказ в UAC при постоянном запуске с правами – продолжение без цикла перезапуска")]
    [TestCase(true, false, StartupAdminMode.Ask, false, StartupAdminAction.None, TestName = "Повторный запуск от администратора – ни карточки, ни вопроса")]
    [TestCase(true, false, StartupAdminMode.AlwaysAdmin, false, StartupAdminAction.None, TestName = "Запуск уже от администратора в режиме всегда с правами – без перезапуска")]
    public void Решение_на_старте(
        bool isElevated,
        bool welcomePending,
        StartupAdminMode mode,
        bool elevationRefused,
        StartupAdminAction expected)
    {
        Assert.That(AdminStartupPrompt.Decide(isElevated, welcomePending, mode, elevationRefused), Is.EqualTo(expected));
    }

    private static MemorySettings PendingSettings()
    {
        var settings = new MemorySettings();
        ((ISettingsStore)settings).SetBool(SettingsKeys.WelcomePending, true);

        return settings;
    }

    private static FirstRunViewModel Card(ISettingsStore settings, bool isElevated, Func<Task<bool>> restart)
    {
        return new(settings, new(settings), new(settings), new(settings), isElevated, restart);
    }
}
