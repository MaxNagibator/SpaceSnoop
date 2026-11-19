using KeepShell.Testing;
using SpaceSnoop.Wpf.Views.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class PathSuggestTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSuggest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "Projects"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Pictures"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Music"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Ввод_сетевого_пути_в_поле_не_блокирует_поток_интерфейса_и_отбрасывает_устаревшую_подсказку()
    {
        var previous = PathSuggest.DirectoryReader;
        var uiThread = Environment.CurrentManagedThreadId;
        var readerThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        PathSuggest.DirectoryReader = (directory, prefix) =>
        {
            Interlocked.Exchange(ref readerThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return [directory + prefix + "jects"];
        };

        try
        {
            var box = new TextBox();
            PathSuggest.SetEnabled(box, true);

            const string initial = @"\\hung-server\share\Pr";
            box.Text = initial;
            box.CaretIndex = initial.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, "o"));

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref readerThread), Is.Not.EqualTo(uiThread));

            box.Text = @"\\hung-server\share\Other";
            box.CaretIndex = box.Text.Length;

            release.Set();
            PumpFor(TimeSpan.FromMilliseconds(60));

            Assert.That(box.Text, Is.EqualTo(@"\\hung-server\share\Other"));
            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            release.Set();
            PathSuggest.DirectoryReader = previous;
        }
    }

    [TestCase("M", "u", "sic")]
    [TestCase("mu", "s", "ic")]
    public void Единственное_совпадение_дополняет_ввод_без_учёта_регистра(string initial, string typed, string suffix)
    {
        var box = new TextBox();
        TypeInto(box, Path.Combine(_tempDir, initial), typed);

        Assert.That(PumpUntil(() => box.SelectionLength > 0, TimeSpan.FromSeconds(5)), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(box.Text, Is.EqualTo(Path.Combine(_tempDir, "Music")));
            Assert.That(box.SelectedText, Is.EqualTo(suffix));
        }
    }

    [Test]
    public void Завершение_фонового_чтения_применяет_подсказку_если_ввод_не_менялся()
    {
        var previous = PathSuggest.DirectoryReader;
        PathSuggest.DirectoryReader = (directory, prefix) => [directory + prefix + "jects"];

        try
        {
            var box = new TextBox();
            PathSuggest.SetEnabled(box, true);

            const string initial = @"\\fast-server\share\Pr";
            box.Text = initial;
            box.CaretIndex = initial.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, "o"));

            var completed = PumpUntil(() => box.SelectionLength > 0, TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(completed, Is.True);
                Assert.That(box.Text, Is.EqualTo(@"\\fast-server\share\Projects"));
                Assert.That(box.SelectedText, Is.EqualTo("jects"));
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.DirectoryReader = previous;
        }
    }

    [TestCase(@"\\server\", ExpectedResult = "server")]
    [TestCase("//server/", ExpectedResult = "server")]
    [TestCase(@"\\server\share\", ExpectedResult = null)]
    [TestCase(@"C:\", ExpectedResult = null)]
    [TestCase(@"\\", ExpectedResult = null)]
    public string? Корень_сервера_отличается_от_обычного_каталога(string directory)
    {
        return PathSuggest.ServerName(directory);
    }

    [Test]
    public void Первый_запрос_к_недоступному_серверу_не_блокирует_ввод()
    {
        var box = new TextBox();
        var server = Guid.NewGuid().ToString("N");
        var initial = $@"\\{server}\s";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        TypeInto(box, initial, "h");
        stopwatch.Stop();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
            Assert.That(box.Text, Is.EqualTo($@"\\{server}\sh"));
            Assert.That(box.SelectedText, Is.Empty);
        }

        PathSuggest.SetEnabled(box, false);
    }

    [Test]
    public void Стрелка_перебирает_подсказки_в_алфавитном_порядке()
    {
        var box = new TextBox();
        using var host = VisualTestHost.Show(new() { Content = box }, 400, 150);
        TypeInto(box, _tempDir + Path.DirectorySeparatorChar, "P");

        Assert.That(PumpUntil(() => box.SelectionLength > 0, TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(box.Text, Is.EqualTo(Path.Combine(_tempDir, "Pictures")));
        box.RaiseEvent(
            new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, Key.Down)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            });
        Assert.That(box.Text, Is.EqualTo(Path.Combine(_tempDir, "Projects")));
    }

    [TestCase("nosep")]
    [TestCase("emptyprefix")]
    [TestCase("nomatch")]
    public void Совпадений_нет(string scenario)
    {
        var (initial, typed) = scenario switch
        {
            "nosep" => ("Musi", "c"),
            "emptyprefix" => (_tempDir, Path.DirectorySeparatorChar.ToString()),
            _ => (Path.Combine(_tempDir, "zz"), "z"),
        };
        var box = new TextBox();
        TypeInto(box, initial, typed);

        PumpFor(TimeSpan.FromMilliseconds(60));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(box.Text, Is.EqualTo(initial + typed));
            Assert.That(box.SelectedText, Is.Empty);
        }
    }

    private static void TypeInto(TextBox box, string initial, string typed)
    {
        box.Text = initial;
        box.CaretIndex = initial.Length;
        PathSuggest.SetEnabled(box, true);
        TextCompositionManager.StartComposition(new(InputManager.Current, box, typed));
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            duration,
            DispatcherPriority.Background,
            (_, _) => frame.Continue = false,
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        if (condition())
        {
            return true;
        }

        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(10),
            DispatcherPriority.Background,
            (_, _) =>
            {
                if (condition() || DateTime.UtcNow >= deadline)
                {
                    frame.Continue = false;
                }
            },
            Dispatcher.CurrentDispatcher);

        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        return condition();
    }
}
