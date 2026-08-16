using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanPreferencesTests
{
    [Test]
    public void По_умолчанию_потоков_вдвое_больше_чем_ядер()
    {
        var preferences = new ScanPreferences(new MemorySettings());

        Assert.That(preferences.ParallelismCeiling, Is.EqualTo(preferences.ProcessorCount * AppDefaults.ScanParallelismPerCore));
        Assert.That(preferences.MaxParallelism, Is.EqualTo(preferences.ParallelismCeiling));
    }

    [Test]
    public void Сохранённое_число_потоков_выше_ядер_переживает_чтение()
    {
        var settings = new MemorySettings();
        var requested = Environment.ProcessorCount + 1;
        settings.SetValue(SettingsKeys.ScanParallelism, requested.ToString());

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(requested));
    }

    [TestCase(0, 1)]
    [TestCase(-5, 1)]
    public void Число_потоков_ниже_единицы_поднимается(int stored, int expected)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanParallelism, stored.ToString());

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(expected));
    }

    [Test]
    public void Число_потоков_выше_потолка_урезается()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanParallelism, "4096");

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(preferences.ParallelismCeiling));
    }
}
