using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanPreferencesTests
{
    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, false)]
    [TestCase(SettingsKeys.ScanConfirmRescan, false)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, true)]
    public void Настройка_памяти_не_по_умолчанию_сохраняется(string key, bool value)
    {
        var settings = new MemorySettings();
        var preferences = new ScanPreferences(settings);

        switch (key)
        {
            case SettingsKeys.ScanReleaseBeforeRescan:
                preferences.ReleaseBeforeRescan = value;
                break;

            case SettingsKeys.ScanConfirmRescan:
                preferences.ConfirmRescan = value;
                break;

            case SettingsKeys.ScanReturnMemoryAfterScan:
                preferences.ReturnMemoryAfterScan = value;
                break;
        }

        Assert.That(ReadMemoryFlag(new(settings), key), Is.EqualTo(value));
    }

    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, null, true)]
    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, "false", false)]
    [TestCase(SettingsKeys.ScanReleaseBeforeRescan, "true", true)]
    [TestCase(SettingsKeys.ScanConfirmRescan, null, true)]
    [TestCase(SettingsKeys.ScanConfirmRescan, "false", false)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, null, false)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, "false", false)]
    [TestCase(SettingsKeys.ScanReturnMemoryAfterScan, "true", true)]
    public void Настройка_памяти_читается_из_настроек_с_умолчанием(string key, string? stored, bool expected)
    {
        var settings = new MemorySettings();

        if (stored is not null)
        {
            settings.SetValue(key, stored);
        }

        var preferences = new ScanPreferences(settings);

        Assert.That(ReadMemoryFlag(preferences, key), Is.EqualTo(expected));
    }

    [TestCase("false", false)]
    [TestCase("true", true)]
    public void Сохранённое_ограничение_диском_целиком_переживает_чтение(string stored, bool expected)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanMftRootOnly, stored);

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MftRootOnly, Is.EqualTo(expected));
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

    [Test]
    public void Умолчание_ограничивает_потоки_а_максимум_остаётся_вдвое_больше_ядер()
    {
        var preferences = new ScanPreferences(new MemorySettings());

        Assert.That(preferences.ParallelismCeiling, Is.EqualTo(Environment.ProcessorCount * 2));
        Assert.That(preferences.MaxParallelism, Is.EqualTo(Math.Min(Environment.ProcessorCount * 2, 16)));
    }

    [Test]
    public void Число_потоков_выше_потолка_урезается()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanParallelism, "4096");

        var preferences = new ScanPreferences(settings);

        Assert.That(preferences.MaxParallelism, Is.EqualTo(Environment.ProcessorCount * 2));
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
    public void Чтение_таблицы_NTFS_по_умолчанию_ограничено_диском_целиком()
    {
        var preferences = new ScanPreferences(new MemorySettings());

        Assert.That(preferences.MftRootOnly, Is.True);
    }

    private static bool ReadMemoryFlag(ScanPreferences preferences, string key)
    {
        return key switch
        {
            SettingsKeys.ScanReleaseBeforeRescan => preferences.ReleaseBeforeRescan,
            SettingsKeys.ScanConfirmRescan => preferences.ConfirmRescan,
            SettingsKeys.ScanReturnMemoryAfterScan => preferences.ReturnMemoryAfterScan,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
    }
}
