using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Tests;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class CleanupProgressPollTests
{
    private const int FilesPerTarget = 60;
    private const int Targets = 2;

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopCleanupPoll_{Guid.NewGuid():N}");

        for (var target = 0; target < Targets; target++)
        {
            var directory = Path.Combine(_root, $"цель-{target}");
            Directory.CreateDirectory(directory);

            for (var index = 0; index < FilesPerTarget; index++)
            {
                File.WriteAllText(Path.Combine(directory, $"файл-{index:D3}.tmp"), new('о', 512));
            }
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [SetCulture("ru-RU")]
    [TestCase(false, false, 0, "", ExpectedResult = "Завершено: {0}.")]
    [TestCase(false, true, 0, "", ExpectedResult = "Завершено: {0}. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(true, true, 0, "", ExpectedResult = "Отменено: {0}. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(
        false,
        true,
        2,
        "Нет доступа",
        ExpectedResult = "Завершено с пропусками: {0}. Пропущено 2 – Нет доступа. Корзину Windows отмена не остановила: этот шаг не прерывается.")]
    [TestCase(false, false, 2, "", ExpectedResult = "Завершено с пропусками: {0}. Пропущено 2.")]
    [TestCase(true, false, 2, "Нет доступа", ExpectedResult = "Отменено: {0}. Пропущено 2 – Нет доступа")]
    [TestCase(true, false, 0, "Каталог не найден.", ExpectedResult = "Отменено: {0}. Каталог не найден.")]
    public string Итог_очистки_не_прячет_ошибку_за_фразой_про_отмену(bool cancelled, bool binCleared, int skipped, string firstError)
    {
        var summary = CleanupProgressDialogViewModel.Summarize(5, 2048, cancelled, binCleared, skipped, firstError);

        return summary.Replace("удалено 5, освобождено 2 КБ", "{0}");
    }

    [Test]
    public async Task Недоступная_цель_без_удалённых_файлов_не_называется_частично_очищенной()
    {
        using var monitor = TestDiagnostics.Monitor();
        var dialog = new CleanupProgressDialogViewModel(
            new([Target(Path.Combine(_root, "missing"))], 1, 1),
            new(),
            new FakeUiDispatcher(),
            NullLogger.Instance,
            new(monitor));

        await dialog.StartCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.Deleted, Is.Zero);
            Assert.That(dialog.Skipped, Is.EqualTo(1));
            Assert.That(dialog.ResultText, Does.StartWith("Не удалось очистить:"));
            Assert.That(dialog.HasErrors, Is.True);
        }
    }

    [Test]
    public async Task Отмена_на_обходе_цели_не_теряет_непрочитанный_каталог_в_итоге()
    {
        var target = Path.Combine(_root, "отмена");
        var kept = Path.Combine(target, "a-open", "keep.tmp");
        var locked = Path.Combine(target, "m-locked");
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        Directory.CreateDirectory(Path.Combine(target, "z-open"));
        Directory.CreateDirectory(locked);
        File.WriteAllText(kept, "k");

        TestAcl.DenyEnumeration(locked);

        try
        {
            using var cancel = new CancellationTokenSource();
            CleanupRequest request = new([Target(target)], 1, 1, cancel.Token);
            using var monitor = TestDiagnostics.Monitor();
            var operations = new PerformanceOperations(monitor);
            var dialog = new CleanupProgressDialogViewModel(request, new(new CancelOnSkippedDirectory(cancel)), new FakeUiDispatcher(), NullLogger.Instance, operations);

            await dialog.StartCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.WasCancelled, Is.True);
                Assert.That(operations.Current, Is.Null);
                Assert.That(operations.LastCleanup?.Cancelled, Is.True);
                Assert.That(operations.LastCleanup?.Skipped, Is.EqualTo(1));
                Assert.That(operations.LastCleanup?.ReportedErrors, Is.Not.Empty);
                Assert.That(File.Exists(kept), Is.True, "Отмена на обходе не остановила удаление.");
                Assert.That(dialog.StatusText, Is.EqualTo($"Отменено: удалено 0, освобождено {SizeFormatter.Format(0)}. Пропущено 1 – «{locked}»: каталог не читается"));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(locked);
        }
    }

    [Test]
    public async Task Очистка_сохраняет_итог_и_не_снимает_чужую_фазу()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        var scan = new PerformanceOperation("Сканирование", 0, 0, TimeSpan.Zero);
        operations.TryReport(scan, null);
        var dialog = CreateDialog(new(), operations);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(operations.Current, Is.SameAs(scan));
            Assert.That(operations.LastCleanup?.Deleted, Is.EqualTo(FilesPerTarget * Targets));
            Assert.That(operations.LastCleanup?.LogicalDeletedBytes, Is.EqualTo(dialog.FreedBytes));
            Assert.That(operations.LastCleanup?.Cancelled, Is.False);
        }

        operations.Release(scan);
    }

    [TestCase(1, 1, "В списке 1 причина.")]
    [TestCase(2, 2, "В списке 2 причины.")]
    [TestCase(5, 5, "В списке 5 причин.")]
    [TestCase(11, 11, "В списке 11 причин.")]
    [TestCase(21, 21, "В списке 21 причина.")]
    [TestCase(50, 101, "В списке 50 причин. Без подробностей: 51 пропуск.")]
    [TestCase(50, 102, "В списке 50 причин. Без подробностей: 52 пропуска.")]
    [TestCase(50, 105, "В списке 50 причин. Без подробностей: 55 пропусков.")]
    public void Подпись_причин_склоняет_числа_и_не_обещает_хронологический_порядок(int shown, int skipped, string expected)
    {
        Assert.That(CleanupProgressDialogViewModel.FormatErrorDetails(shown, skipped), Is.EqualTo(expected));
    }

    [TestCase("«C:\\Temp\\файл.tmp»: файл изменился после обхода", "файл.tmp", "Изменился после замера")]
    [TestCase("«C:\\Temp\\каталог»: каталог не читается", "каталог", "Не удалось прочитать каталог")]
    [TestCase("«C:\\Temp\\каталог\\»: каталог не удалён – ошибка ввода-вывода", "каталог", "Не удалось удалить каталог")]
    [TestCase("«C:\\Temp\\файл.tmp»: The process cannot access the file 'C:\\Temp\\файл.tmp'", "файл.tmp", "Не удалось удалить")]
    [TestCase("Новая причина без пути", "Ошибка очистки", "Не удалось очистить объект")]
    public void Запись_пропуска_отделяет_имя_от_полного_сообщения(string raw, string name, string reason)
    {
        var issue = CleanupIssue.From(raw);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issue.Name, Is.EqualTo(name));
            Assert.That(issue.Reason, Is.EqualTo(reason));
            Assert.That(issue.Details, Is.EqualTo(raw));
        }
    }

    [TestCase(CleanupAvailability.None, "Не замерена")]
    [TestCase(CleanupAvailability.Missing, "Каталога нет")]
    [TestCase(CleanupAvailability.NeedsAdmin, "Нужны права администратора")]
    [TestCase(CleanupAvailability.Unsupported, "Удаляется штатным средством Windows")]
    [TestCase(CleanupAvailability.Unsafe, "Путь ведёт в корень тома или наружу по ссылке – очистка запрещена")]
    [TestCase(CleanupAvailability.Failed, "Не удалось опросить")]
    public void Запись_недоступной_цели_сохраняет_причину_Core(CleanupAvailability availability, string expected)
    {
        const string target = "Кэш Windows / NuGet";
        var raw = $"«{target}»: {CleanupText.Availability(availability)}";
        var issue = CleanupIssue.From(raw);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(issue.Name, Is.EqualTo(target));
            Assert.That(issue.Reason, Is.EqualTo(expected));
            Assert.That(issue.Details, Is.EqualTo(raw));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Пропуски_сохраняют_причины_и_показывают_общий_предел_списка(bool allTargets)
    {
        var files = allTargets
            ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            : Directory.GetFiles(Path.Combine(_root, "цель-0"));

        var locked = files
            .Select(path => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            .ToList();

        try
        {
            using var monitor = TestDiagnostics.Monitor();
            var operations = new PerformanceOperations(monitor);
            var dialog = CreateDialog(new(), operations);
            await dialog.StartCommand.ExecuteAsync(null);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.IsFinished, Is.True);
                Assert.That(dialog.HasErrors, Is.True);
                Assert.That(dialog.Deleted, Is.EqualTo(allTargets ? 0 : FilesPerTarget));
                Assert.That(dialog.Skipped, Is.EqualTo(files.Length));
                Assert.That(dialog.StatusText, Does.StartWith(allTargets ? "Не удалось очистить:" : "Завершено с пропусками:"));
                Assert.That(dialog.HasErrorDetails, Is.True);
                Assert.That(dialog.ErrorDetailsText.Split(Environment.NewLine), Has.Length.EqualTo(AppDefaults.CleanupReportedErrorsLimit));
                Assert.That(operations.LastCleanup!.ReportedErrors, Has.Count.EqualTo(AppDefaults.CleanupReportedErrorsLimit));
                Assert.That(
                    dialog.ErrorDetailsCaption,
                    Does.Contain($"В списке {AppDefaults.CleanupReportedErrorsLimit} причин")
                        .And.Contain($"Без подробностей: {files.Length - AppDefaults.CleanupReportedErrorsLimit} пропусков"));
            }
        }
        finally
        {
            locked.ForEach(stream => stream.Dispose());
        }
    }

    [TestCase(CleanupTargetKind.RecycleBin, 5, 0, true, ExpectedResult = true)]
    [TestCase(CleanupTargetKind.RecycleBin, 0, 0, true, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.RecycleBin, 0, 5, true, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.RecycleBin, 5, 0, false, ExpectedResult = false)]
    [TestCase(CleanupTargetKind.Directory, 5, 0, true, ExpectedResult = false)]
    public bool Фраза_про_неделимый_шаг_только_когда_корзина_очищена(CleanupTargetKind kind, int deleted, int skipped, bool cancelRequested)
    {
        var target = new CleanupTarget
        {
            Id = "t",
            Name = "t",
            Description = "Тест",
            Kind = kind,
            Path = _root,
        };

        string[] errors = skipped > 0 ? ["Корзина занята другим процессом"] : [];

        return CleanupProgressDialogViewModel.ClearedDespiteCancel(target, new(deleted * 512L, deleted, skipped, errors), cancelRequested);
    }

    [Test]
    public async Task Очистка_показывает_итоговый_прогресс_всех_целей_без_обновления_на_каждом_файле()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = CreateDialog(dispatcher);
        var updates = Watch(dialog);

        await dialog.StartCommand.ExecuteAsync(null);

        var total = FilesPerTarget * Targets;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.Deleted, Is.EqualTo(total), "Очистка не дошла до всех файлов – проверять нечего.");
            Assert.That(updates.Value, Is.EqualTo(1), $"Экран обновился {updates.Value} раз на {total} файлах – прогресс снова пушится на каждый объект.");
            Assert.That(dialog.CountText, Is.EqualTo($"{total:N0} / {total:N0}"), "Счётчик второй цели начался заново вместо продолжения первой.");
            Assert.That(dispatcher.Timers.Any(static timer => timer.IsRunning), Is.False, "Опрос продолжился после очистки.");
        }
    }

    private CleanupProgressDialogViewModel CreateDialog(FakeUiDispatcher dispatcher, PerformanceOperations? operations = null)
    {
        var targets = Directory.GetDirectories(_root)
            .Order(StringComparer.Ordinal)
            .Select(Target)
            .ToList();

        CleanupRequest request = new(targets, 512L * FilesPerTarget * Targets, FilesPerTarget * Targets);

        return new(request, new(), dispatcher, NullLogger.Instance, operations ?? new(TestDiagnostics.Monitor()));
    }

    private static CleanupTarget Target(string path)
    {
        return new()
        {
            Id = Path.GetFileName(path),
            Name = Path.GetFileName(path),
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Path = path,
            MinimumAge = TimeSpan.Zero,
        };
    }

    private static StrongBox<int> Watch(CleanupProgressDialogViewModel dialog)
    {
        var updates = new StrongBox<int>(0);

        dialog.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(dialog.CountText))
            {
                updates.Value++;
            }
        };

        return updates;
    }

    private sealed class CancelOnSkippedDirectory(CancellationTokenSource cancel) : ILogger<CleanupService>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == "CleanupDirectorySkipped")
            {
                cancel.Cancel();
            }
        }
    }
}
