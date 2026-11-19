using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
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
        PathSuggest.ClearCacheForTests();

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

    [Test]
    public void Ввод_двойного_слэша_и_корня_сервера_открывает_список_без_изменения_текста()
    {
        var prevHosts = PathSuggest.HostReader;
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        PathSuggest.HostReader = static () => ["filesrv", "nas"];
        PathSuggest.KnownShareReader = static _ => [];
        PathSuggest.ShareReader = static _ => ["archive", "media"];

        try
        {
            var box = new TextBox();
            using var host = VisualTestHost.Show(new() { Content = box }, 400, 150, true);
            box.Focus();

            TypeInto(box, @"\", @"\");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PathSuggest.IsPopupOpen(box), Is.True);
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { @"\\filesrv", @"\\nas" }));
                Assert.That(PathSuggest.PopupSelectedIndex(box), Is.EqualTo(-1));
                Assert.That(box.Text, Is.EqualTo(@"\\"));
                Assert.That(box.SelectedText, Is.Empty);
            }

            box.Text = @"\\nas";
            box.CaretIndex = box.Text.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, @"\"));

            Assert.That(PumpUntil(() => PathSuggest.PopupItems(box).Count == 2, TimeSpan.FromSeconds(5)), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(PathSuggest.IsPopupOpen(box), Is.True);
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { @"\\nas\archive", @"\\nas\media" }));
                Assert.That(PathSuggest.PopupSelectedIndex(box), Is.EqualTo(-1));
                Assert.That(box.Text, Is.EqualTo(@"\\nas\"));
                Assert.That(box.SelectedText, Is.Empty);
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.HostReader = prevHosts;
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
        }
    }

    [Test]
    public void Префикс_хоста_и_шары_дополняет_ввод_inline_и_открывает_список()
    {
        var prevHosts = PathSuggest.HostReader;
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        PathSuggest.HostReader = static () => ["backup", "nas"];
        PathSuggest.KnownShareReader = static _ => [];
        PathSuggest.ShareReader = static _ => ["media", "music"];

        try
        {
            var box = new TextBox();
            using var host = VisualTestHost.Show(new() { Content = box }, 400, 150, true);
            box.Focus();

            TypeInto(box, "//n", "a");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(box.Text, Is.EqualTo(@"\\nas"));
                Assert.That(box.SelectedText, Is.EqualTo("s"));
                Assert.That(PathSuggest.IsPopupOpen(box), Is.True);
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { @"\\nas" }));
            }

            box.Text = @"\\nas\m";
            box.CaretIndex = box.Text.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, "e"));

            Assert.That(PumpUntil(() => box.Text == @"\\nas\media", TimeSpan.FromSeconds(5)), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(box.SelectedText, Is.EqualTo("dia"));
                Assert.That(PathSuggest.IsPopupOpen(box), Is.True);
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { @"\\nas\media" }));
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.HostReader = prevHosts;
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
        }
    }

    [Test]
    public void Навигация_стрелками_фиксация_хоста_и_Escape_управляют_списком()
    {
        var prevHosts = PathSuggest.HostReader;
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        PathSuggest.HostReader = static () => ["alpha", "nas"];
        PathSuggest.KnownShareReader = static _ => [];
        PathSuggest.ShareReader = static server => server == "nas" ? ["media"] : [];

        try
        {
            var box = new TextBox();
            using var host = VisualTestHost.Show(new() { Content = box }, 400, 150, true);
            box.Focus();

            TypeInto(box, @"\", @"\");
            Assert.That(PathSuggest.IsPopupOpen(box), Is.True);

            RaiseKey(box, Key.Down);
            RaiseKey(box, Key.Down);
            Assert.That(PathSuggest.PopupSelectedIndex(box), Is.EqualTo(1));
            Assert.That(box.Text, Is.EqualTo(@"\\"));

            var enterAccept = RaiseKey(box, Key.Enter);
            Assert.That(enterAccept.Handled, Is.True);
            Assert.That(box.Text, Is.EqualTo(@"\\nas\"));
            Assert.That(PumpUntil(() => PathSuggest.IsPopupOpen(box) && PathSuggest.PopupItems(box).Contains(@"\\nas\media"), TimeSpan.FromSeconds(5)), Is.True);

            var escapeFirst = RaiseKey(box, Key.Escape);
            var escapeSecond = RaiseKey(box, Key.Escape);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(escapeFirst.Handled, Is.True);
                Assert.That(escapeSecond.Handled, Is.False);
                Assert.That(PathSuggest.IsPopupOpen(box), Is.False);
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.HostReader = prevHosts;
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
        }
    }

    [Test]
    public void Отказ_NetShareEnum_дополняется_локально_известными_шарами_и_не_кэшируется_навсегда()
    {
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        var server = "srv-" + Guid.NewGuid().ToString("N")[..6];
        IReadOnlyList<string> netShares = [];

        PathSuggest.KnownShareReader = s => string.Equals(s, server, StringComparison.OrdinalIgnoreCase) ? ["cached-share"] : [];
        PathSuggest.ShareReader = _ => netShares;

        try
        {
            var box = new TextBox();
            using var host = VisualTestHost.Show(new() { Content = box }, 400, 150, true);
            box.Focus();

            TypeInto(box, $@"\\{server}", @"\");
            PumpFor(TimeSpan.FromMilliseconds(80));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PathSuggest.IsPopupOpen(box), Is.True);
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { $@"\\{server}\cached-share" }));
            }

            netShares = ["live-share"];
            box.Text = $@"\\{server}";
            box.CaretIndex = box.Text.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box, @"\"));

            Assert.That(PumpUntil(() => PathSuggest.PopupItems(box).Count == 2, TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { $@"\\{server}\cached-share", $@"\\{server}\live-share" }));

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
        }
    }

    [Test]
    public void Найденные_шары_дополняют_список_с_уже_подставленным_сетевым_путём()
    {
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        var server = "srv-" + Guid.NewGuid().ToString("N")[..6];
        var prefix = $@"\\{server}\";
        var known = prefix + "music";
        var discovered = prefix + "media";
        using var release = new ManualResetEventSlim();

        PathSuggest.KnownShareReader = _ => ["music"];
        PathSuggest.ShareReader = _ =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return ["media"];
        };

        try
        {
            var box = new TextBox();
            using var host = VisualTestHost.Show(new() { Content = box }, 400, 150, true);
            box.Focus();

            TypeInto(box, prefix, "m");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(box.Text, Is.EqualTo(known));
                Assert.That(box.SelectedText, Is.EqualTo("usic"));
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { known }));
            }

            release.Set();
            Assert.That(PumpUntil(() => PathSuggest.PopupItems(box).Count == 2, TimeSpan.FromSeconds(5)), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(PathSuggest.PopupItems(box), Is.EqualTo(new[] { discovered, known }));
                Assert.That(PathSuggest.PopupSelectedIndex(box), Is.EqualTo(1));
                Assert.That(box.Text, Is.EqualTo(known));
                Assert.That(box.SelectedText, Is.EqualTo("usic"));
            }

            PathSuggest.SetEnabled(box, false);
        }
        finally
        {
            release.Set();
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
        }
    }

    [Test]
    public void Ожидание_шар_уведомляет_оба_поля_сбрасывает_очередь_при_ошибке_и_истекает_по_TTL()
    {
        var prevShares = PathSuggest.ShareReader;
        var prevKnown = PathSuggest.KnownShareReader;
        var prevClock = PathSuggest.ClockMs;
        var server = "srv-" + Guid.NewGuid().ToString("N")[..6];
        long now = 1_000;
        var calls = 0;
        var shouldThrow = false;
        IReadOnlyList<string> resultShares = ["alpha"];
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        PathSuggest.ClockMs = () => Volatile.Read(ref now);
        PathSuggest.KnownShareReader = static _ => [];
        PathSuggest.ShareReader = _ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));

            if (shouldThrow)
            {
                throw new InvalidOperationException("unexpected");
            }

            return resultShares;
        };

        try
        {
            var box1 = new TextBox();
            var box2 = new TextBox();
            using var host1 = VisualTestHost.Show(new() { Content = box1 }, 400, 150, true);
            using var host2 = VisualTestHost.Show(new() { Content = box2 }, 400, 150, true);

            box1.Focus();
            TypeInto(box1, $@"\\{server}", @"\");
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            box2.Focus();
            TypeInto(box2, $@"\\{server}", @"\");

            release.Set();

            Assert.That(PumpUntil(() => PathSuggest.PopupItems(box2).SequenceEqual([$@"\\{server}\alpha"]), TimeSpan.FromSeconds(5)), Is.True);
            box1.Focus();
            Assert.That(Volatile.Read(ref calls), Is.EqualTo(1));

            Volatile.Write(ref now, now + (AppDefaults.NetworkDiscoveryCacheSeconds * 1000L) + 1);
            shouldThrow = true;

            box1.Text = $@"\\{server}";
            box1.CaretIndex = box1.Text.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box1, @"\"));
            Assert.That(PumpUntil(() => Volatile.Read(ref calls) == 2, TimeSpan.FromSeconds(5)), Is.True);
            PumpFor(TimeSpan.FromMilliseconds(50));

            shouldThrow = false;
            resultShares = ["beta"];
            box1.Text = $@"\\{server}";
            box1.CaretIndex = box1.Text.Length;
            TextCompositionManager.StartComposition(new(InputManager.Current, box1, @"\"));

            Assert.That(PumpUntil(() => PathSuggest.PopupItems(box1).SequenceEqual([$@"\\{server}\beta"]), TimeSpan.FromSeconds(5)), Is.True);

            PathSuggest.SetEnabled(box1, false);
            PathSuggest.SetEnabled(box2, false);
        }
        finally
        {
            release.Set();
            PathSuggest.ShareReader = prevShares;
            PathSuggest.KnownShareReader = prevKnown;
            PathSuggest.ClockMs = prevClock;
        }
    }

    private static KeyEventArgs RaiseKey(TextBox box, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        box.RaiseEvent(args);
        return args;
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
