using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class DeleteDialogTests
{
    private static readonly string[] Paths = [@"C:\a", @"C:\b", @"C:\c", @"C:\d", @"C:\e"];

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
    public void Список_диалога_удаления_виртуализирован()
    {
        var markup = File.ReadAllText(Path.Combine(RepositoryRoot(), "SpaceSnoop.Wpf", "Views", "Dialogs", "DeleteProgressDialogView.xaml"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(markup, Does.Contain("<VirtualizingStackPanel"), "Панель списка по умолчанию не виртуализирует – 4314 строк раскладываются при открытии.");
            Assert.That(markup, Does.Contain("VirtualizingPanel.IsVirtualizing=\"True\""));
            Assert.That(markup, Does.Contain("VirtualizingPanel.VirtualizationMode=\"Recycling\""));
            Assert.That(markup, Does.Contain("CanContentScroll=\"True\""), "Без CanContentScroll панель получает бесконечную высоту и виртуализация не включается.");
            Assert.That(markup, Does.Not.Contain("IsSharedSizeScope"), "SharedSizeScope связывает пере-измерение всех строк списка.");
            Assert.That(markup, Does.Not.Contain("SharedSizeGroup"));
        }
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
}
