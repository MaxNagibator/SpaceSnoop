using KeepShell.Bootstrap;
using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Storage;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SystemThemeTests
{
    [TestCase(1, true)]
    [TestCase(0, false)]
    [TestCase(null, true)]
    [TestCase("0", true)]
    public void Значение_AppsUseLightTheme_решает_светлая_или_тёмная(object? registryValue, bool expectedLight)
    {
        Assert.That(SystemTheme.IsLight(registryValue), Is.EqualTo(expectedLight));
    }

    [Test]
    public void Новый_профиль_записывает_системную_тему()
    {
        var settings = new MemorySettings();

        var key = AppThemes.StartupKey(settings, true);

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo(AppThemes.SystemKey));
            Assert.That(settings.GetStringValue(SettingsKeys.Theme), Is.EqualTo(AppThemes.SystemKey));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Системная_тема_регистрируется_палитрой_режима_Windows(bool appsUseLightTheme, bool expectedDark)
    {
        AppThemes.FollowSystem(appsUseLightTheme);

        var system = ThemeManager.Find(AppThemes.SystemKey);
        var source = expectedDark ? ThemeManager.DefaultDark : ThemeManager.DefaultLight;

        Assert.That(system, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(system!.IsDark, Is.EqualTo(expectedDark));
            Assert.That(system.Palette, Is.EqualTo(source.Palette));
            Assert.That(ThemeManager.IsDarkFamily(AppThemes.SystemKey), Is.EqualTo(expectedDark));
        });
    }

    [Test]
    public void Смена_режима_Windows_при_системной_теме_применяется_одним_Changed_без_морфа()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register();
        AppThemes.RegisterSystem(appsUseLightTheme: true);
        ThemeManager.Apply(AppThemes.SystemKey);

        var changed = new List<ThemeDefinition>();
        var applying = 0;
        EventHandler<ThemeDefinition> onChanged = (_, theme) => changed.Add(theme);
        EventHandler<ThemeTransition> onApplying = (_, _) => applying++;
        ThemeManager.Changed += onChanged;
        ThemeManager.Applying += onApplying;

        try
        {
            AppThemes.FollowSystem(appsUseLightTheme: false);
        }
        finally
        {
            ThemeManager.Changed -= onChanged;
            ThemeManager.Applying -= onApplying;
            AppThemes.RegisterSystem(appsUseLightTheme: true);
            ThemeManager.Apply(AppThemes.LightKey);
        }

        Assert.Multiple(() =>
        {
            Assert.That(changed, Has.Count.EqualTo(1));
            Assert.That(changed[0].Key, Is.EqualTo(AppThemes.SystemKey));
            Assert.That(changed[0].IsDark, Is.True);
            Assert.That(applying, Is.Zero);
        });
    }

    [Test]
    public void Старый_профиль_без_ключа_темы_остаётся_светлым_и_ключ_не_пишется()
    {
        var settings = new MemorySettings();

        var key = AppThemes.StartupKey(settings, false);

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo(AppThemes.LightKey));
            Assert.That(settings.GetStringValue(SettingsKeys.Theme), Is.Null.Or.Empty);
        });
    }

    [Test]
    public void Системная_тема_стоит_первой_в_выборе()
    {
        AppThemes.Register();

        Assert.That(ThemeManager.Themes.First().Key, Is.EqualTo(AppThemes.SystemKey));
    }

    [TestCase(AppThemes.LightKey)]
    [TestCase(AppThemes.DarkKey)]
    public void Явно_выбранная_тема_не_перезаписывается(string chosen)
    {
        AppThemes.Register();
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.Theme, chosen);

        var key = AppThemes.StartupKey(settings, false);

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.EqualTo(chosen));
            Assert.That(settings.GetStringValue(SettingsKeys.Theme), Is.EqualTo(chosen));
        });
    }
    [Test]
    public void Сохранённый_ключ_остаётся_при_отсутствии_пакета_и_работает_после_регистрации()
    {
        AppThemes.Register();
        const string key = "startup-test-theme";
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.Theme, key);

        Assert.That(AppThemes.StartupKey(settings, false), Is.EqualTo(AppThemes.SystemKey));
        Assert.That(settings.GetStringValue(SettingsKeys.Theme), Is.EqualTo(key));

        ThemeManager.Register(ThemeManager.DefaultDark with { Key = key });
        Assert.That(AppThemes.StartupKey(settings, false), Is.EqualTo(key));
    }

    [Test]
    public void Повреждённый_пакет_темы_при_старте_отклоняется_и_откатывает_к_системной()
    {
        AppThemes.Register();
        var dir = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string packageId = "KeepShell.Themes.Corrupt";
            var packPath = Path.Combine(dir, packageId + ".theme.zip");
            File.WriteAllText(packPath, "not-a-valid-zip");

            var rejected = new List<string>();
            var loader = new ThemePackLoader(dir, (path, _) => rejected.Add(path));
            var settings = new MemorySettings();
            settings.SetValue(SettingsKeys.Theme, "corrupt-theme");
            settings.SetValue(ThemePackLoader.SettingsKeyName, packageId);

            var startup = AppThemes.StartupKey(settings, false, loader);

            Assert.Multiple(() =>
            {
                Assert.That(startup, Is.EqualTo(AppThemes.SystemKey));
                Assert.That(rejected, Contains.Item(packPath));
                Assert.That(settings.GetStringValue(SettingsKeys.Theme), Is.EqualTo("corrupt-theme"));
            });
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
