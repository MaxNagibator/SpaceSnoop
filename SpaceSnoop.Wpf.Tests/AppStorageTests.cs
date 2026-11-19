using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using Tomlyn.Model;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AppStorageTests
{
    [Test]
    public void Migrate_переносит_файлы_не_оставляя_дубликатов_в_источнике()
    {
        var root = CreateTempRoot();
        var source = Path.Combine(root, "src");
        var destination = Path.Combine(root, "dst");

        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, AppStorage.LogsFolderName));

            File.WriteAllText(Path.Combine(source, TomlSettingsFile.PrimaryFileName), "theme = \"dark\"");
            File.WriteAllText(Path.Combine(source, TomlSettingsFile.LegacyFileName), "wpf.shell.font_scale 1.25");
            File.WriteAllText(Path.Combine(source, AppInfo.DeletionLogFileName), "deleted");
            File.WriteAllText(Path.Combine(source, AppInfo.SyncLogFileName), "synced");
            File.WriteAllText(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log"), "log");

            AppStorage.Migrate(source, destination);

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(destination, TomlSettingsFile.PrimaryFileName)), Is.EqualTo("theme = \"dark\""));
                Assert.That(File.ReadAllText(Path.Combine(destination, TomlSettingsFile.LegacyFileName)), Is.EqualTo("wpf.shell.font_scale 1.25"));
                Assert.That(File.Exists(Path.Combine(destination, AppInfo.DeletionLogFileName)), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, AppStorage.LogsFolderName, "wpf-1.log")), Is.True);

                Assert.That(File.Exists(Path.Combine(source, TomlSettingsFile.PrimaryFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, TomlSettingsFile.LegacyFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppInfo.SyncLogFileName)), Is.False);
                Assert.That(File.Exists(Path.Combine(source, AppStorage.LogsFolderName, "wpf-1.log")), Is.False);
            });
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Обычная_сборка_на_чистом_старте_выбирает_AppData_и_не_пишет_рядом_с_exe()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            var useAppData = AppStorage.Resolve(exe, appData, isPortableBuild: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(useAppData, Is.True);
                Assert.That(Directory.EnumerateFileSystemEntries(exe), Is.Empty);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Portable_сборка_на_чистом_старте_игнорирует_чужой_профиль_в_AppData()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "usb", "SpaceSnoop");
        var otherExe = Path.Combine(root, "other", "SpaceSnoop");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            var table = new TomlTable
            {
                [AppStorage.StorageModeKey] = AppStorage.AppDataModeValue,
                [AppStorage.AppDataDirectoriesKey] = new TomlArray { otherExe },
            };
            TomlSettingsFile.Save(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), table);

            var useAppData = AppStorage.Resolve(exe, appData, isPortableBuild: true);

            Assert.That(useAppData, Is.False);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestCase(false, "portable", false, TestName = "Обычная сборка с storage = portable рядом с exe выбирает Portable")]
    [TestCase(false, "appdata", true, TestName = "Обычная сборка с storage = appdata рядом с exe выбирает AppData")]
    [TestCase(true, "portable", false, TestName = "Portable-сборка с storage = portable рядом с exe выбирает Portable")]
    [TestCase(true, "appdata", true, TestName = "Portable-сборка с storage = appdata рядом с exe выбирает AppData")]
    public void Явный_ключ_storage_рядом_с_exe_определяет_режим(bool isPortableBuild, string mode, bool expectedUseAppData)
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);
            TomlSettingsFile.Save(
                Path.Combine(exe, TomlSettingsFile.PrimaryFileName),
                new TomlTable { [AppStorage.StorageModeKey] = mode });

            Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild), Is.EqualTo(expectedUseAppData));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Существующий_settings_toml_рядом_с_exe_без_ключа_storage_сохраняет_Portable()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.PrimaryFileName), "_system_theme = \"dark\"\n");

            Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: false), Is.False);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestCase(false, TestName = "Переключение AppData -> Portable с оставлением файлов создаёт storage = portable и снимает каталог из AppData")]
    [TestCase(true, TestName = "Переключение AppData -> Portable с переносом файлов записывает storage = portable и снимает каталог из AppData")]
    public void Переключение_из_AppData_в_Portable_фиксирует_режим_в_settings_toml(bool migrate)
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var otherExe = Path.Combine(root, "other");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            var initialAppData = new TomlTable
            {
                [AppStorage.StorageModeKey] = AppStorage.AppDataModeValue,
                [AppStorage.AppDataDirectoriesKey] = new TomlArray { exe, otherExe },
            };
            TomlSettingsFile.SetString(initialAppData, "_system_theme", "dark");
            TomlSettingsFile.Save(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), initialAppData);

            if (migrate)
            {
                AppStorage.Migrate(appData, exe);
            }

            AppStorage.SetUseAppData(exe, appData, useAppData: false, migrated: migrate);

            var portableTable = TomlSettingsFile.Read(Path.Combine(exe, TomlSettingsFile.PrimaryFileName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: false), Is.False);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.False);
                Assert.That(TomlSettingsFile.GetString(portableTable, AppStorage.StorageModeKey), Is.EqualTo(AppStorage.PortableModeValue));
                Assert.That(portableTable.ContainsKey(AppStorage.AppDataDirectoriesKey), Is.False);

                if (migrate)
                {
                    Assert.That(TomlSettingsFile.GetString(portableTable, "_system_theme"), Is.EqualTo("dark"));
                }

                var appDataTable = TomlSettingsFile.Read(Path.Combine(appData, TomlSettingsFile.PrimaryFileName));
                var directories = ((TomlArray)appDataTable[AppStorage.AppDataDirectoriesKey]).Cast<string>().ToList();
                Assert.That(directories, Is.EqualTo([Path.TrimEndingDirectorySeparator(Path.GetFullPath(otherExe))]));
                Assert.That(AppStorage.Resolve(otherExe, appData, isPortableBuild: true), Is.True);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Переключение_Portable_в_AppData_без_переноса_обновляет_оба_файла_и_оставляет_приёмник_свежим_профилем()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "portable");
        var secondPortable = Path.Combine(root, "usb", "portable");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(secondPortable);
            Directory.CreateDirectory(appData);

            var initialPortable = new TomlTable
            {
                [AppStorage.StorageModeKey] = AppStorage.PortableModeValue,
            };
            TomlSettingsFile.SetString(initialPortable, "_system_theme", "tarkov");
            TomlSettingsFile.Save(Path.Combine(exe, TomlSettingsFile.PrimaryFileName), initialPortable);

            AppStorage.SetUseAppData(exe, appData, useAppData: true, migrated: false);

            var exeTable = TomlSettingsFile.Read(Path.Combine(exe, TomlSettingsFile.PrimaryFileName));
            var appDataTable = TomlSettingsFile.Read(Path.Combine(appData, TomlSettingsFile.PrimaryFileName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(TomlSettingsFile.GetString(exeTable, AppStorage.StorageModeKey), Is.EqualTo(AppStorage.AppDataModeValue));
                Assert.That(TomlSettingsFile.GetString(exeTable, "_system_theme"), Is.EqualTo("tarkov"));
                Assert.That(TomlSettingsFile.GetString(appDataTable, AppStorage.StorageModeKey), Is.EqualTo(AppStorage.AppDataModeValue));
                Assert.That(AppStorage.HasSettings(exe), Is.True);
                Assert.That(AppStorage.HasSettings(appData), Is.False);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.True);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: false), Is.True);
                Assert.That(AppStorage.Resolve(secondPortable, appData, isPortableBuild: true), Is.False);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Переключение_Portable_в_AppData_с_переносом_сохраняет_привязку_другой_копии_и_изолирует_третью()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "portable-one") + Path.DirectorySeparatorChar;
        var priorBoundExe = Path.Combine(root, "prior-bound");
        var otherExe = Path.Combine(root, "usb", "SpaceSnoop") + Path.DirectorySeparatorChar;
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(priorBoundExe);
            Directory.CreateDirectory(otherExe);
            Directory.CreateDirectory(appData);

            TomlSettingsFile.Save(
                Path.Combine(appData, TomlSettingsFile.PrimaryFileName),
                new TomlTable
                {
                    [AppStorage.StorageModeKey] = AppStorage.AppDataModeValue,
                    [AppStorage.AppDataDirectoriesKey] = new TomlArray { priorBoundExe },
                });

            var initialPortable = new TomlTable();
            TomlSettingsFile.SetString(initialPortable, "_system_theme", "dark");
            TomlSettingsFile.Save(Path.Combine(exe, TomlSettingsFile.PrimaryFileName), initialPortable);

            AppStorage.Migrate(exe, appData);
            AppStorage.SetUseAppData(exe, appData, useAppData: true, migrated: true);

            var exeSameCaseVariant = exe.ToUpperInvariant().TrimEnd(Path.DirectorySeparatorChar);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(Path.Combine(exe, TomlSettingsFile.PrimaryFileName)), Is.False);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.True);
                Assert.That(AppStorage.Resolve(exeSameCaseVariant, appData, isPortableBuild: true), Is.True);
                Assert.That(AppStorage.Resolve(priorBoundExe, appData, isPortableBuild: true), Is.True);
                Assert.That(AppStorage.Resolve(otherExe, appData, isPortableBuild: true), Is.False);
                Assert.That(TomlSettingsFile.GetString(TomlSettingsFile.Read(Path.Combine(appData, TomlSettingsFile.PrimaryFileName)), "_system_theme"), Is.EqualTo("dark"));
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Переключение_Portable_в_AppData_при_оставшемся_settings_toml_обновляет_его_до_appdata()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "portable");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            TomlSettingsFile.Save(
                Path.Combine(exe, TomlSettingsFile.PrimaryFileName),
                new TomlTable { [AppStorage.StorageModeKey] = AppStorage.PortableModeValue });

            AppStorage.SetUseAppData(exe, appData, useAppData: true, migrated: true);

            var remainingTable = TomlSettingsFile.Read(Path.Combine(exe, TomlSettingsFile.PrimaryFileName));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(TomlSettingsFile.GetString(remainingTable, AppStorage.StorageModeKey), Is.EqualTo(AppStorage.AppDataModeValue));
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.True);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Неисправный_TOML_рядом_с_exe_сохраняет_переносимый_режим_без_переименования_на_этапе_выбора()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.PrimaryFileName), "storage = [broken");
            File.WriteAllText(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), "appdata_directories = [broken");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: false), Is.False);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.False);
                Assert.That(File.Exists(Path.Combine(exe, TomlSettingsFile.PrimaryFileName)), Is.True);
                Assert.That(File.Exists(Path.Combine(exe, TomlSettingsFile.PrimaryFileName + TomlSettingsFile.CorruptSuffix)), Is.False);
                Assert.That(File.Exists(Path.Combine(appData, TomlSettingsFile.PrimaryFileName + TomlSettingsFile.CorruptSuffix)), Is.False);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Список_appdata_directories_устойчив_к_неверным_типам_и_нормализует_разделители()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "Apps", "SpaceSnoop");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            var variantPath = Path.Combine(root, "apps", "sub", "..", "SpaceSnoop") + "/";
            File.WriteAllText(
                Path.Combine(appData, TomlSettingsFile.PrimaryFileName),
                $"""
                appdata_directories = [123, "", "bad\u0000path", "{variantPath.Replace("\\", "\\\\", StringComparison.Ordinal)}"]
                """);

            Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.True);

            File.WriteAllText(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), "appdata_directories = \"not-an-array\"\n");
            Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.False);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Закрытие_хранилища_после_миграции_не_воскрешает_удалённый_settings_toml_в_источнике()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");
        var exeToml = Path.Combine(exe, TomlSettingsFile.PrimaryFileName);

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);

            using var store = new SettingsStore(exeToml);
            ((ISettingsStore)store).SetBool(SettingsKeys.WelcomePending, false);
            store.Flush();

            AppStorage.Migrate(exe, appData);
            AppStorage.SetUseAppData(exe, appData, useAppData: true, migrated: true);

            store.DiscardPending();
            store.Close();

            ((ISettingsStore)store).SetInt(SettingsKeys.WindowLeft, 120);
            store.Close();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(exeToml), Is.False);
                Assert.That(AppStorage.Resolve(exe, appData, isPortableBuild: true), Is.True);
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void Сбой_записи_settings_toml_оставляет_settings_txt_и_выбирает_AppData()
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");
        const string legacy = "wpf.shell.font_scale 1.25\n";

        try
        {
            Directory.CreateDirectory(Path.Combine(exe, TomlSettingsFile.PrimaryFileName));
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), legacy);

            var useAppData = AppStorage.Resolve(exe, appData, isPortableBuild: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(useAppData, Is.True);
                Assert.That(File.ReadAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName)), Is.EqualTo(legacy));
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestCase("wpf.shell.font_scale 1.25\nwpf.scan.multithreading True\n", false, true, TestName = "Свой settings.txt без профиля в AppData – каталог переносной, файл мигрирует")]
    [TestCase("\n  wpf.shell.font_scale 1.25\r\n", false, true, TestName = "Свой settings.txt с отступом и CRLF – каталог переносной")]
    [TestCase("volume 7\nlanguage ru\n", false, false, TestName = "Чужой settings.txt – каталог не переносной, миграции нет")]
    [TestCase("", false, false, TestName = "Пустой settings.txt – каталог не переносной")]
    [TestCase("wpf.shell.font_scale 1.25\n", true, false, TestName = "Свой settings.txt при профиле в AppData – остаётся AppData, миграции нет")]
    public void Старый_settings_txt_рядом_с_exe(string legacy, bool appDataProfile, bool expectedPortable)
    {
        var root = CreateTempRoot();
        var exe = Path.Combine(root, "exe");
        var appData = Path.Combine(root, "appdata");

        try
        {
            Directory.CreateDirectory(exe);
            Directory.CreateDirectory(appData);
            File.WriteAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName), legacy);

            if (appDataProfile)
            {
                File.WriteAllText(Path.Combine(appData, TomlSettingsFile.PrimaryFileName), string.Empty);
            }

            var portable = AppStorage.HasLegacyPortableSettings(exe, appData);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(portable, Is.EqualTo(expectedPortable));
                Assert.That(File.Exists(Path.Combine(exe, TomlSettingsFile.PrimaryFileName)), Is.EqualTo(expectedPortable));
                Assert.That(File.ReadAllText(Path.Combine(exe, TomlSettingsFile.LegacyFileName)), Is.EqualTo(legacy));
            }
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestCase(@"D:\Portable\SpaceSnoop-v2.9.0-portable.exe", @"C:\Users\admin\AppData\Local\Temp\.net\SpaceSnoop.Wpf\abc\", @"D:\Portable\", TestName = "Single-file с самораспаковкой выбирает папку реального exe вместо Temp")]
    [TestCase(@"D:\SpaceSnoop.exe", @"C:\Users\admin\AppData\Local\Temp\.net\SpaceSnoop.Wpf\abc\", @"D:\", TestName = "Исполняемый файл в корне тома не дублирует разделитель")]
    [TestCase(@"C:\Program Files\SpaceSnoop\SpaceSnoop.Wpf.exe", @"C:\Program Files\SpaceSnoop\", @"C:\Program Files\SpaceSnoop\", TestName = "Обычный apphost сохраняет папку исполняемого файла со слэшем")]
    [TestCase(@"C:\Program Files\dotnet\dotnet.exe", @"C:\Repo\bin\Debug\net10.0-windows\", @"C:\Repo\bin\Debug\net10.0-windows\", TestName = "Запуск через dotnet оставляет BaseDirectory сборки")]
    [TestCase(@"C:\NuGet\microsoft.testplatform.testhost\testhost.exe", @"C:\Repo\bin\Debug\net10.0-windows\", @"C:\Repo\bin\Debug\net10.0-windows\", TestName = "Тестовый хост testhost оставляет BaseDirectory тестовой сборки")]
    [TestCase(@"C:\VS\Common7\IDE\Extensions\TestPlatform\vstest.console.exe", @"C:\Repo\bin\Debug\net10.0-windows\", @"C:\Repo\bin\Debug\net10.0-windows\", TestName = "Консоль vstest оставляет BaseDirectory тестовой сборки")]
    [TestCase("", @"C:\Repo\bin\Debug\net10.0-windows\", @"C:\Repo\bin\Debug\net10.0-windows\", TestName = "Пустой ProcessPath откатывается к BaseDirectory")]
    [TestCase(null, @"C:\Repo\bin\Debug\net10.0-windows\", @"C:\Repo\bin\Debug\net10.0-windows\", TestName = "Отсутствующий ProcessPath откатывается к BaseDirectory")]
    public void Каталог_приложения_определяется_по_реальному_exe_а_не_папке_распаковки_бандла(string? processPath, string baseDirectory, string expected)
    {
        Assert.That(AppStorage.ResolveExecutableDirectory(processPath, baseDirectory), Is.EqualTo(expected));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "SpaceSnoop.Tests", Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
