using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.IO;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DeleteDialogTests
{
    private const int Rows = 4314;

    private const int RunRows = 120;

    private static readonly string[] Paths = [@"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e"];

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spacesnoop-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Пачка_уходит_одним_вызовом_и_закрывает_все_строки()
    {
        var chunks = new List<int>();
        var singles = 0;

        var results = Run(
            new(static _ => true, chunk => chunks.Add(chunk.Count), _ => singles++),
            chunkSize: 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chunks, Is.EqualTo(new[] { 5 }));
            Assert.That(singles, Is.Zero);
            Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Removed));
        }
    }

    [Test]
    public void Исчезнувший_путь_в_пачку_не_попадает_и_считается_пропажей()
    {
        var sent = new List<string>();

        var results = Run(
            new(path => path != @"C:\c", chunk => sent.AddRange(chunk), static _ => { }),
            chunkSize: 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Does.Not.Contain(@"C:\c"));
            Assert.That(Status(results, 2), Is.EqualTo(DeleteItemStatus.Missing));
            Assert.That(results.Count(static result => result.Status == DeleteItemStatus.Removed), Is.EqualTo(4));
        }
    }

    [Test]
    public void Упавшая_пачка_переигрывается_пофайлово_и_называет_виноватого()
    {
        var singles = new List<string>();

        var results = Run(
            new(
                static _ => true,
                static _ => throw new IOException("пачка не прошла"),
                path =>
                {
                    singles.Add(path);

                    if (path == @"C:\d")
                    {
                        throw new UnauthorizedAccessException("файл занят");
                    }
                }),
            chunkSize: 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singles, Has.Count.EqualTo(5));
            Assert.That(Status(results, 3), Is.EqualTo(DeleteItemStatus.Failed));
            Assert.That(results[3].Failure?.Message, Is.EqualTo("файл занят"));
            Assert.That(results.Count(static result => result.Status == DeleteItemStatus.Removed), Is.EqualTo(4));
        }
    }

    [Test]
    public void Удалённое_упавшей_пачкой_остаётся_удалённым_а_не_пропажей()
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);
        var singles = new List<string>();

        var results = Run(
            new(
                path => !gone.Contains(path),
                chunk =>
                {
                    gone.Add(chunk[0]);
                    gone.Add(chunk[1]);
                    throw new IOException("часть объектов не удалилась");
                },
                singles.Add),
            chunkSize: 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singles, Is.EqualTo(new[] { @"C:\c", @"C:\d", @"C:\e" }));
            Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Removed));
        }
    }

    [Test]
    public void Пачка_из_одного_объекта_ошибку_не_переигрывает()
    {
        var singles = 0;

        var results = Run(
            new(static _ => true, static _ => throw new IOException("не вышло"), _ => singles++),
            chunkSize: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singles, Is.Zero);
            Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Failed));
            Assert.That(results, Has.Count.EqualTo(5));
        }
    }

    [Test]
    public void Отмена_между_пачками_оставляет_остаток_нетронутым()
    {
        using var cancellation = new CancellationTokenSource();
        var sent = new List<string>();
        var results = new List<DeleteItemResult>();

        DeleteBatchCallbacks callbacks = new(
            static _ => true,
            chunk =>
            {
                sent.AddRange(chunk);
                cancellation.Cancel();
            },
            static _ => { });

        Assert.Throws<OperationCanceledException>(() => DeleteBatch.Run(
            Paths,
            2,
            callbacks,
            static _ => { },
            results.Add,
            cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sent, Is.EqualTo(new[] { @"C:\a", @"C:\b" }));
            Assert.That(results, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Пачка_из_одного_объекта_исчезнувшего_на_сбое_считается_удалённой()
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);
        var singles = 0;

        var results = Run(
            new(
                path => !gone.Contains(path),
                chunk =>
                {
                    if (chunk[0] == @"C:\b")
                    {
                        gone.Add(chunk[0]);
                        throw new IOException("оборвалось");
                    }
                },
                _ => singles++),
            chunkSize: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singles, Is.Zero);
            Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Removed));
        }
    }

    [Test]
    public void Исчезнувший_до_безвозвратного_удаления_путь_остаётся_пропажей()
    {
        var results = Run(
            new(
                static _ => true,
                chunk =>
                {
                    if (chunk[0] == @"C:\b")
                    {
                        throw new FileNotFoundException("Путь не найден", chunk[0]);
                    }
                },
                static _ => { }),
            chunkSize: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Status(results, 1), Is.EqualTo(DeleteItemStatus.Missing));
            Assert.That(results.Count(static result => result.Status == DeleteItemStatus.Removed), Is.EqualTo(4));
        }
    }

    [Test]
    public void Сбой_переигровки_у_исчезнувшего_пути_считается_удалённым()
    {
        var gone = new HashSet<string>(StringComparer.Ordinal);

        var results = Run(
            new(
                path => !gone.Contains(path),
                static _ => throw new IOException("пачка не прошла"),
                path =>
                {
                    if (path == @"C:\c")
                    {
                        gone.Add(path);
                        throw new IOException("оболочка оборвала вызов");
                    }
                }),
            chunkSize: 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Status(results, 2), Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Removed));
        }
    }

    [Test]
    public void Отмена_посреди_переигровки_не_теряет_унесённое_пачкой()
    {
        using var cancellation = new CancellationTokenSource();
        var gone = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<DeleteItemResult>();

        DeleteBatchCallbacks callbacks = new(
            path => !gone.Contains(path),
            chunk =>
            {
                gone.Add(@"C:\a");
                gone.Add(@"C:\b");
                throw new IOException("часть объектов не удалилась");
            },
            path =>
            {
                if (path == @"C:\c")
                {
                    gone.Add(@"C:\d");
                    cancellation.Cancel();
                }
            });

        Assert.Throws<OperationCanceledException>(() => DeleteBatch.Run(
            Paths,
            5,
            callbacks,
            static _ => { },
            results.Add,
            cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Status(results, 0), Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(Status(results, 1), Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(Status(results, 2), Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(Status(results, 3), Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(Status(results, 4), Is.EqualTo(DeleteItemStatus.None));
        }
    }

    [Test]
    public void Отмена_во_время_последней_пачки_доезжает_отменой()
    {
        using var cancellation = new CancellationTokenSource();
        var results = new List<DeleteItemResult>();

        DeleteBatchCallbacks callbacks = new(
            static _ => true,
            _ => cancellation.Cancel(),
            static _ => { });

        Assert.Throws<OperationCanceledException>(() => DeleteBatch.Run(
            Paths,
            5,
            callbacks,
            static _ => { },
            results.Add,
            cancellation.Token));

        Assert.That(results.Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.Removed));
    }

    [Test]
    public void Отмена_посреди_переигровки_возвращает_строки_в_ожидание()
    {
        using var cancellation = new CancellationTokenSource();
        var results = new List<DeleteItemResult>();

        DeleteBatchCallbacks callbacks = new(
            static _ => true,
            static _ => throw new IOException("пачка не прошла"),
            _ => cancellation.Cancel());

        Assert.Throws<OperationCanceledException>(() => DeleteBatch.Run(
            Paths,
            5,
            callbacks,
            static _ => { },
            results.Add,
            cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Status, Is.EqualTo(DeleteItemStatus.Removed));
            Assert.That(results.Skip(1).Select(static result => result.Status), Is.All.EqualTo(DeleteItemStatus.None));
        }
    }

    [Test]
    public void Прогресс_сливает_повторные_обновления_строки_и_не_теряет_ошибку()
    {
        var progress = new DeleteProgressState(2);

        progress.Report(new(0, DeleteRowState.Deleting, null, 0, 0, 0));
        progress.Report(new(0, DeleteRowState.Failed, "занят", 0, 0, 1));
        progress.Report(new(1, DeleteRowState.Done, null, 200, 1, 1));

        var snapshot = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.Updates, Has.Count.EqualTo(2));
            Assert.That(snapshot.Updates.Single(update => update.Index == 0), Is.EqualTo(new DeleteTick(0, DeleteRowState.Failed, "занят", 0, 0, 1)));
            Assert.That(snapshot.Updates.Single(update => update.Index == 1), Is.EqualTo(new DeleteTick(1, DeleteRowState.Done, null, 200, 1, 1)));
            Assert.That(snapshot.CurrentIndex, Is.EqualTo(1));
            Assert.That(snapshot.Completed, Is.EqualTo(1));
            Assert.That(snapshot.Failed, Is.EqualTo(1));
            Assert.That(progress.CreateSnapshot().Updates, Is.Empty);
        }
    }

    [Test]
    public void Прогресс_держит_только_последнее_состояние_каждой_строки()
    {
        var progress = new DeleteProgressState(Rows);

        for (var index = 0; index < Rows; index++)
        {
            progress.Report(new(index, DeleteRowState.Deleting, null, index, index, 0));
            progress.Report(new(index, DeleteRowState.Done, null, index + 1, index + 1, 0));
        }

        var snapshot = progress.CreateSnapshot();
        var updates = snapshot.Updates.ToDictionary(static update => update.Index);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.Updates, Has.Count.EqualTo(Rows));
            Assert.That(updates[0], Is.EqualTo(new DeleteTick(0, DeleteRowState.Done, null, 1, 1, 0)));
            Assert.That(updates[Rows - 1], Is.EqualTo(new DeleteTick(Rows - 1, DeleteRowState.Done, null, Rows, Rows, 0)));
            Assert.That(snapshot.CurrentIndex, Is.EqualTo(Rows - 1));
        }
    }

    [Test]
    public void Список_диалога_удаления_виртуализирован()
    {
        var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf", "Views", "Dialogs", "DeleteProgressDialogView.xaml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markup, Does.Contain("<VirtualizingStackPanel"), "Панель списка по умолчанию не виртуализирует – 4314 строк раскладываются при открытии.");
            Assert.That(markup, Does.Contain("VirtualizingPanel.IsVirtualizing=\"True\""));
            Assert.That(markup, Does.Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\""));
            Assert.That(markup, Does.Contain("CanContentScroll=\"True\""), "Без CanContentScroll панель получает бесконечную высоту и виртуализация не включается.");
            Assert.That(markup, Does.Contain("DiagnosticsText"));
            Assert.That(markup, Does.Contain("AutomationProperties.Name=\"Диагностика удаления\""));
            Assert.That(markup, Does.Not.Contain("IsSharedSizeScope"), "SharedSizeScope связывает пере-измерение всех строк списка.");
            Assert.That(markup, Does.Not.Contain("SharedSizeGroup"));
        }
    }

    [Test]
    public async Task Экран_удаления_обновляется_тиком_таймера_а_не_каждым_объектом()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = Dialog(dispatcher, Items(RunRows, static index => index % 2 == 0));
        var updates = Watch(dialog);

        await dialog.StartCommand.ExecuteAsync(null);

        var done = dialog.Items.Where(static (_, index) => index % 2 == 0).ToList();
        var failed = dialog.Items.Where(static (_, index) => index % 2 != 0).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                updates.Value,
                Is.EqualTo(1),
                $"Экран обновился {updates.Value} раз на {RunRows} объектах – прогресс снова пушится на каждую строку.");

            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
            Assert.That(dialog.CountText, Is.EqualTo($"{RunRows / 2} / {RunRows}"), "Итоговый счёт до экрана не доехал.");
            Assert.That(done.Select(static row => row.State), Is.All.EqualTo(DeleteRowState.Done), "Единственный слив потерял удалённые строки.");
            Assert.That(failed.Select(static row => row.State), Is.All.EqualTo(DeleteRowState.Failed), "Единственный слив потерял отказавшие строки.");
            Assert.That(failed.Select(static row => row.Error), Is.All.EqualTo("Путь не найден"), "Единственный слив потерял текст ошибки строки.");
        }
    }

    [Test]
    public async Task Отменённое_удаление_оставляет_экран_согласованным()
    {
        const int stopAt = 3;

        var dispatcher = new FakeUiDispatcher();
        DeleteProgressDialogViewModel? started = null;
        var items = Items(RunRows, static _ => true).ToList();
        var trigger = new CancelOnSizeRead(items[stopAt].Name, items[stopAt].Parent!, () => started!.RequestStop());
        items[stopAt] = trigger;

        var dialog = Dialog(dispatcher, items);
        started = dialog;
        trigger.Armed = true;

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил отмену.");
            Assert.That(dialog.StatusText, Does.StartWith("Отменено."), "Отмена не доехала до итога – проверять нечего.");
            Assert.That(dialog.CountText, Is.EqualTo($"{stopAt + 1} / {RunRows}"), "Счётчик разошёлся с числом закрытых строк.");

            Assert.That(
                dialog.Items.Take(stopAt + 1).Select(static row => row.State),
                Is.All.EqualTo(DeleteRowState.Done),
                "Оборванный слив потерял строки, которые успели удалиться.");

            Assert.That(
                dialog.Items.Skip(stopAt + 1).Select(static row => row.State),
                Is.All.EqualTo(DeleteRowState.Pending),
                "Нетронутые отменой строки показаны закрытыми.");
        }
    }

    [Test]
    public async Task Отказ_каждого_объекта_не_надувает_счётчик_удалённого()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = Dialog(dispatcher, Items(RunRows, static _ => false));

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.CountText, Is.EqualTo($"0 / {RunRows}"), "Отказы посчитаны удалёнными.");
            Assert.That(dialog.ProgressValue, Is.EqualTo(1d), "Полоса не дошла до конца, хотя обработаны все объекты.");
            Assert.That(dialog.HasErrors, Is.True);
            Assert.That(dialog.Items.Select(static row => row.State), Is.All.EqualTo(DeleteRowState.Failed));
        }
    }

    private static StrongBox<int> Watch(DeleteProgressDialogViewModel dialog)
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

    private static DeleteProgressDialogViewModel Dialog(FakeUiDispatcher dispatcher, IReadOnlyList<SpaceBase> items)
    {
        return new(
            items,
            permanent: true,
            new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance),
            new PerformanceRunTracker(),
            new ShellPreferences(new MemorySettings()),
            dispatcher,
            NullLogger.Instance);
    }

    private IReadOnlyList<SpaceBase> Items(int count, Func<int, bool> create)
    {
        var root = new DirectorySpace(_root, null, DateTime.Now, DateTime.Now);
        var items = new List<SpaceBase>(count);

        for (var index = 0; index < count; index++)
        {
            var name = $"объект-{index:D4}";

            if (create(index))
            {
                Directory.CreateDirectory(Path.Combine(_root, name));
            }

            items.Add(new DirectorySpace(name, root, DateTime.Now, DateTime.Now));
        }

        return items;
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }

    private static DeleteItemStatus Status(List<DeleteItemResult> results, int index)
    {
        return results.Single(result => result.Index == index).Status;
    }

    private static List<DeleteItemResult> Run(DeleteBatchCallbacks callbacks, int chunkSize)
    {
        var results = new List<DeleteItemResult>();

        DeleteBatch.Run(Paths, chunkSize, callbacks, static _ => { }, results.Add, CancellationToken.None);

        return results;
    }

    private sealed class CancelOnSizeRead(string name, SpaceBase parent, Action stop)
        : DirectorySpace(name, parent, DateTime.Now, DateTime.Now)
    {
        public bool Armed { get; set; }

        public override long TotalSize
        {
            get
            {
                if (Armed)
                {
                    Armed = false;
                    stop();
                }

                return base.TotalSize;
            }
        }
    }
}
