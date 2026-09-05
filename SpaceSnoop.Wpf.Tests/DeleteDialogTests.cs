using KeepShell.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

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
            Progress(results),
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
            Progress(results),
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
            Progress(results),
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
            Progress(results),
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

    [TestCase(3, false, "это 3 объекта")]
    [TestCase(25, false, "это 25 объектов")]
    [TestCase(60, false, "это 25 объектов")]
    [TestCase(3, true, "после текущего объекта")]
    public void Подсказка_отмены_называет_пачку_которая_бывает(int count, bool permanent, string expected)
    {
        var dialog = Dialog(new FakeUiDispatcher(), Items(count, static _ => false), permanent);

        Assert.That(
            dialog.CancelHint,
            Does.Contain(expected),
            $"Помечено объектов: {count}, а подсказка отмены обещает другое: {dialog.CancelHint}");
    }

    [TestCase("StatusText")]
    [TestCase("CountText")]
    [TestCase("BatchCaption")]
    [TestCase("ChunkElapsedText")]
    public void Живой_текст_диалога_удаления_объявлен_экранному_диктору(string property)
    {
        var block = XDocument.Parse(Markup())
            .Descendants()
            .SingleOrDefault(element => (string?)element.Attribute("Text") == $"{{Binding {property}}}");

        Assert.That(block, Is.Not.Null, $"В разметке нет строки с текстом {property} – проверять нечего.");

        Assert.That(
            (string?)block!.Attribute("AutomationProperties.LiveSetting"),
            Is.EqualTo("Polite"),
            $"Текст {property} меняется по ходу удаления, но экранный диктор о смене не узнает.");
    }

    [Test]
    public void Вращение_значка_идущей_строки_спрашивает_системную_настройку_анимаций()
    {
        var spin = XDocument.Parse(Markup())
            .Descendants()
            .Single(static element => element.Name.LocalName == "BeginStoryboard");

        var trigger = spin.Ancestors().First(static element => element.Name.LocalName.EndsWith("Trigger", StringComparison.Ordinal));

        var conditions = trigger.Descendants()
            .Where(static element => element.Name.LocalName == "Condition")
            .Select(static element => $"{(string?)element.Attribute("Binding")} = {(string?)element.Attribute("Value")}")
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                trigger.Name.LocalName,
                Is.EqualTo("MultiDataTrigger"),
                $"Вращение значка висит на «{trigger.Name.LocalName}» – второму условию, системной настройке анимаций, места нет.");

            Assert.That(conditions, Does.Contain("{Binding State} = Deleting"), "Вращение перестало зависеть от того, идёт ли строка.");

            Assert.That(
                conditions,
                Does.Contain("{Binding Source={x:Static SystemParameters.ClientAreaAnimation}} = True"),
                "Значок крутится, не спрашивая, разрешены ли анимации в системе.");
        }
    }

    [Test]
    public async Task Отказ_всей_операции_записан_своим_событием_с_идентификатором_прогона()
    {
        var journal = Path.Combine(AppStorage.DataDirectory, AppInfo.DeletionLogFileName);
        var spy = new DeleteLogSpy();

        using (new FileStream(journal, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            var dialog = Dialog(new FakeUiDispatcher(), Items(3, static _ => false), logger: spy);

            await dialog.StartCommand.ExecuteAsync(null);
        }

        var started = spy.Entries.SingleOrDefault(entry => entry.Event == 1100);
        var failed = spy.Entries.SingleOrDefault(entry => entry.Event == 1108);

        Assert.That(
            failed,
            Is.Not.Null,
            $"Оборванный прогон записан событиями {string.Join(", ", spy.Entries.Select(static entry => entry.Event))} – своего события у отказа операции нет.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                spy.Entries.Select(static entry => entry.Event),
                Has.No.Member(1101),
                "Отказ всей операции записан как отказ объекта – названный в записи путь к причине отношения не имеет.");

            Assert.That(failed!.Level, Is.EqualTo(LogLevel.Error), "Оборванный прогон записан не ошибкой.");
            Assert.That(failed.Failure, Is.InstanceOf<IOException>(), "В записи нет исключения, оборвавшего прогон.");

            Assert.That(
                failed.Fields.GetValueOrDefault("Run"),
                Is.EqualTo(started?.Fields.GetValueOrDefault("Run")).And.Not.Empty,
                "Отказ операции не назвал прогон – в журнале его не связать со стартом и итогом.");
        }
    }

    [Test]
    public void Пульс_включает_затянувшаяся_пачка_а_не_сам_факт_пачки()
    {
        var dialog = Dialog(new FakeUiDispatcher(), Items(3, static _ => true));
        var notified = 0;

        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DeleteProgressDialogViewModel.IsChunkStalled))
            {
                notified++;
            }
        };

        Assert.That(dialog.IsChunkStalled, Is.False, "Диалог до старта уже показывает пульс.");

        dialog.IsChunkRunning = true;

        Assert.That(
            dialog.IsChunkStalled,
            Is.False,
            "Пачка идёт почти всё время прогона, поэтому пульс по одному этому признаку не даст процентам вернуться никогда.");

        dialog.ChunkElapsedText = "идёт 3 с";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.IsChunkStalled, Is.True, "Пачка идёт дольше секунды, а полоса всё ещё считает проценты по завершённым объектам.");

            Assert.That(
                notified,
                Is.EqualTo(2),
                $"Об изменении пульса сообщено {notified} раз(а) из двух – привязка полосы не узнает о переключении.");
        }

        dialog.ChunkElapsedText = null;
        dialog.IsChunkRunning = false;

        Assert.That(dialog.IsChunkStalled, Is.False, "Пачка закончилась, а полоса осталась в пульсе.");
    }

    [Test]
    public void Разметка_диалога_удаления_держит_пульс_и_причину_отказа()
    {
        var markup = Markup();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                markup,
                Does.Contain("IsIndeterminate=\"{Binding IsChunkStalled"),
                "Полоса перестала уходить в пульс – на неделимой операции снова не движется ни один индикатор.");

            Assert.That(
                markup,
                Does.Not.Contain("IsIndeterminate=\"{Binding IsChunkRunning"),
                "Пачка идёт почти всё время прогона, поэтому пульс по ней означает, что проценты не вернутся никогда.");

            Assert.That(markup, Does.Contain("Content=\"{Binding CancelCaption}\""), "Кнопка отмены снова молчит о том, что нажатие услышано.");
            Assert.That(markup, Does.Contain("ToolTip=\"{Binding CancelHint}\""), "Подсказка отмены не называет момент срабатывания.");

            Assert.That(
                markup,
                Does.Not.Contain("IsEnabled"),
                "Кнопка отмены обязана оставаться нажимаемой – её выключение отняло бы единственное работающее действие.");

            Assert.That(markup, Does.Contain("ToolTip=\"{Binding ToolTipText}\""), "Подсказка строки снова несёт только путь.");
            Assert.That(markup, Does.Not.Contain("ToolTip=\"{Binding Path}\""), "Причина отказа строки опять никуда не выведена.");
            Assert.That(markup, Does.Contain("{Binding ErrorSummary}"), "Текст отказа не показан второй строкой под именем.");
        }
    }

    [Test]
    public void Список_диалога_удаления_виртуализирован()
    {
        var markup = Markup();

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
        var trigger = new TriggerOnSizeRead(items[stopAt].Name, items[stopAt].Parent!, () => started!.RequestStop());
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

    [TestCase(5, new[] { 0 }, new[] { 4 })]
    [TestCase(2, new[] { 0, 2, 4 }, new[] { 1, 3, 4 })]
    [TestCase(1, new[] { 0, 1, 2, 3, 4 }, new[] { 0, 1, 2, 3, 4 })]
    public void Каждая_пачка_объявлена_парой_с_номером_и_диапазоном(int chunkSize, int[] firsts, int[] lasts)
    {
        var trace = new ChunkTrace();

        DeleteBatch.Run(
            Paths,
            chunkSize,
            new(static _ => true, static _ => { }, static _ => { }),
            trace.Reports(),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace.Started.Select(static chunk => chunk.Ordinal), Is.EqualTo(Enumerable.Range(1, firsts.Length)));
            Assert.That(trace.Started.Select(static chunk => chunk.Total), Is.All.EqualTo(firsts.Length));
            Assert.That(trace.Started.Select(static chunk => chunk.FirstIndex), Is.EqualTo(firsts));
            Assert.That(trace.Started.Select(static chunk => chunk.LastIndex), Is.EqualTo(lasts));
            Assert.That(trace.Finished.Select(static outcome => outcome.Chunk), Is.EqualTo(trace.Started), "Итог пачки разошёлся с её началом.");
            Assert.That(trace.Finished.Select(static outcome => outcome.RetriedOneByOne), Is.All.False);
        }
    }

    [TestCase(5, false, false)]
    [TestCase(5, true, true)]
    [TestCase(1, true, false)]
    public void Итог_пачки_называет_переигровку_по_одному(int chunkSize, bool chunkFails, bool retried)
    {
        var trace = new ChunkTrace();

        DeleteBatchCallbacks callbacks = new(
            static _ => true,
            _ =>
            {
                if (chunkFails)
                {
                    throw new IOException("пачка не прошла");
                }
            },
            static _ => { });

        DeleteBatch.Run(Paths, chunkSize, callbacks, trace.Reports(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace.Finished, Has.Count.EqualTo(trace.Started.Count));
            Assert.That(trace.Finished.Select(static outcome => outcome.RetriedOneByOne), Is.All.EqualTo(retried));
            Assert.That(trace.Finished.Select(static outcome => outcome.ElapsedMs), Is.All.GreaterThanOrEqualTo(0));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Отмена_закрывает_пачку_ровно_одним_итогом(bool duringRetry)
    {
        using var cancellation = new CancellationTokenSource();
        var trace = new ChunkTrace();

        DeleteBatchCallbacks callbacks = new(
            static _ => true,
            _ =>
            {
                if (duringRetry)
                {
                    throw new IOException("пачка не прошла");
                }

                cancellation.Cancel();
            },
            _ => cancellation.Cancel());

        Assert.Throws<OperationCanceledException>(() => DeleteBatch.Run(
            Paths,
            2,
            callbacks,
            trace.Reports(),
            cancellation.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trace.Started, Has.Count.EqualTo(1), "Отмена сработала не на границе пачки.");
            Assert.That(trace.Finished, Has.Count.EqualTo(1), "Итог оборванной пачки потерян или задвоен.");
            Assert.That(trace.Finished[0].Chunk, Is.EqualTo(trace.Started[0]));
            Assert.That(trace.Finished[0].RetriedOneByOne, Is.EqualTo(duringRetry));
        }
    }

    [TestCase(0, 24, 4314, ExpectedResult = "объекты 1–25 из 4314")]
    [TestCase(3000, 3024, 4314, ExpectedResult = "объекты 3001–3025 из 4314")]
    [TestCase(4313, 4313, 4314, ExpectedResult = null)]
    public string? Подпись_называет_пачку_диапазоном_а_одиночную_оставляет_пути(int firstIndex, int lastIndex, int total)
    {
        return DeleteProgressDialogViewModel.DescribeBatch(firstIndex, lastIndex, total);
    }

    [TestCase(new[] { DeleteRowState.Done, DeleteRowState.Failed, DeleteRowState.Deleting, DeleteRowState.Pending }, 0, 3, ExpectedResult = 2)]
    [TestCase(new[] { DeleteRowState.Pending, DeleteRowState.Pending, DeleteRowState.Pending }, 1, 2, ExpectedResult = 1)]
    [TestCase(new[] { DeleteRowState.Done, DeleteRowState.Done }, 0, 1, ExpectedResult = 1)]
    [TestCase(new DeleteRowState[0], 0, 0, ExpectedResult = -1)]
    public int Следование_показывает_первую_незакрытую_строку_пачки(DeleteRowState[] states, int firstIndex, int lastIndex)
    {
        return DeleteProgressDialogViewModel.FirstUnfinished(RowsWith(states), firstIndex, lastIndex);
    }

    [Test]
    public async Task Пульс_объявляет_идущую_пачку_и_гаснет_в_итоге()
    {
        const int stopAt = 3;

        var dispatcher = new FakeUiDispatcher();
        DeleteProgressDialogViewModel? started = null;
        var running = false;
        var follow = int.MinValue;
        string? caption = null;

        var items = Items(RunRows, static _ => true).ToList();

        var trigger = new TriggerOnSizeRead(
            items[stopAt].Name,
            items[stopAt].Parent!,
            () =>
            {
                dispatcher.Timers[0].Tick();
                running = started!.IsChunkRunning;
                follow = started.FollowIndex;
                caption = started.BatchCaption;
            });

        items[stopAt] = trigger;

        var dialog = Dialog(dispatcher, items);
        started = dialog;
        trigger.Armed = true;

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(running, Is.True, "Пачка шла, а пульс объявлен погасшим.");
            Assert.That(follow, Is.EqualTo(stopAt), "Следование показало не первую незакрытую строку пачки.");
            Assert.That(caption, Is.Null, "Пачка из одного объекта названа диапазоном вместо пути.");
            Assert.That(dialog.IsChunkRunning, Is.False, "Пульс пережил операцию.");
            Assert.That(dialog.FollowIndex, Is.EqualTo(-1), "Следовать больше не за чем, а индекс остался.");
            Assert.That(dialog.BatchCaption, Is.Null);
        }
    }

    [TestCase("Отказано в доступе", ExpectedResult = "Отказано в доступе")]
    [TestCase("Первая строка\r\nвторая строка", ExpectedResult = "Первая строка")]
    [TestCase("   ", ExpectedResult = null)]
    public string? Причина_отказа_сжата_до_одной_строки(string error)
    {
        return new DeleteRowViewModel(Space("объект")) { Error = error }.ErrorSummary;
    }

    [Test]
    public void Причина_отказа_доезжает_до_подсказки_строки()
    {
        var row = new DeleteRowViewModel(Space("объект"));
        var changed = new List<string?>();

        row.PropertyChanged += (_, arguments) => changed.Add(arguments.PropertyName);

        var before = row.ToolTipText;
        row.Error = "Файл занят другим процессом";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(row.Path), "Без отказа в подсказке стоит путь.");
            Assert.That(row.ToolTipText, Does.StartWith(row.Path).And.Contains("Файл занят другим процессом"));
            Assert.That(changed, Does.Contain(nameof(DeleteRowViewModel.ToolTipText)));
            Assert.That(changed, Does.Contain(nameof(DeleteRowViewModel.ErrorSummary)));
        }
    }

    [Test]
    public void Длинная_причина_отказа_обрезана_многоточием()
    {
        var row = new DeleteRowViewModel(Space("объект")) { Error = new string('я', 150) };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.ErrorSummary, Has.Length.EqualTo(100));
            Assert.That(row.ErrorSummary, Does.EndWith("…"));
            Assert.That(row.ToolTipText, Does.Contain(new string('я', 150)), "Подсказка обязана нести причину целиком.");
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

    private static DeleteProgressDialogViewModel Dialog(
        FakeUiDispatcher dispatcher,
        IReadOnlyList<SpaceBase> items,
        bool permanent = true,
        ILogger? logger = null)
    {
        return new(
            items,
            permanent,
            new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance),
            new PerformanceRunTracker(),
            new ShellPreferences(new MemorySettings()),
            dispatcher,
            logger ?? NullLogger.Instance);
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

    private static string Markup()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf", "Views", "Dialogs", "DeleteProgressDialogView.xaml"));
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }

    private static DeleteItemStatus Status(List<DeleteItemResult> results, int index)
    {
        return results.Single(result => result.Index == index).Status;
    }

    private static DirectorySpace Space(string name)
    {
        var root = new DirectorySpace(@"C:\корень", null, DateTime.Now, DateTime.Now);

        return new(name, root, DateTime.Now, DateTime.Now);
    }

    private static List<DeleteRowViewModel> RowsWith(DeleteRowState[] states)
    {
        var rows = new List<DeleteRowViewModel>(states.Length);

        for (var index = 0; index < states.Length; index++)
        {
            rows.Add(new(Space($"объект-{index}")) { State = states[index] });
        }

        return rows;
    }

    private static DeleteBatchProgress Progress(List<DeleteItemResult> results)
    {
        return new(static _ => { }, results.Add, static _ => { }, static _ => { });
    }

    private static List<DeleteItemResult> Run(DeleteBatchCallbacks callbacks, int chunkSize)
    {
        var results = new List<DeleteItemResult>();

        DeleteBatch.Run(Paths, chunkSize, callbacks, Progress(results), CancellationToken.None);

        return results;
    }

    private sealed record DeleteLogEntry(int Event, LogLevel Level, Exception? Failure, IReadOnlyDictionary<string, string?> Fields);

    private sealed class DeleteLogSpy : ILogger
    {
        public List<DeleteLogEntry> Entries { get; } = [];

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
            var fields = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];

            Entries.Add(new(
                eventId.Id,
                logLevel,
                exception,
                fields.ToDictionary(static field => field.Key, static field => field.Value?.ToString())));
        }
    }

    private sealed class ChunkTrace
    {
        public List<DeleteChunkInfo> Started { get; } = [];

        public List<DeleteChunkOutcome> Finished { get; } = [];

        public List<DeleteItemResult> Results { get; } = [];

        public DeleteBatchProgress Reports()
        {
            return new(static _ => { }, Results.Add, Started.Add, Finished.Add);
        }
    }

    private sealed class TriggerOnSizeRead(string name, SpaceBase parent, Action hook)
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
                    hook();
                }

                return base.TotalSize;
            }
        }
    }
}
