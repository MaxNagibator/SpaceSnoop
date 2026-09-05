using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AppStorageTests
{
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, true)]
    [TestCase(true, false, false, false)]
    [TestCase(true, true, true, false)]
    public void По_умолчанию_AppData_кроме_явного_portable_или_наследия(bool portableMarker, bool legacyAppDataMarker, bool legacyPortableData, bool expected)
    {
        Assert.That(AppStorage.Resolve(portableMarker, legacyAppDataMarker, legacyPortableData), Is.EqualTo(expected));
    }

    [Test]
    public void Migrate_переносит_файлы_не_оставляя_дубликатов_в_источнике()
    {
        var root = Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "src");
        var destination = Path.Combine(root, "dst");

        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, AppStorage.LogsFolderName));

            File.WriteAllText(Path.Combine(source, TomlSettingsFile.PrimaryFileName), "theme = \"dark\"");
            File.WriteAllText(Path.Combine(source, AppInfo.DeletionLogFileName), "deleted");
            File.WriteAllText(Path.Combine(source, AppInfo.SyncLogFileName), "synced");
            File.WriteAllText(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log"), "log");

            AppStorage.Migrate(source, destination);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(destination, TomlSettingsFile.PrimaryFileName)), Is.EqualTo("theme = \"dark\""));
                Assert.That(File.Exists(Path.Combine(destination, AppInfo.DeletionLogFileName)), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, AppStorage.LogsFolderName, "wpf-1.log")), Is.True);

                Assert.That(File.Exists(Path.Combine(source, TomlSettingsFile.PrimaryFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppInfo.SyncLogFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log")), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
