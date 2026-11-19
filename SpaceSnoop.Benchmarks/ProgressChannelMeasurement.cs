using KeepShell.Testing;
using KeepShell.ViewModels;
using SpaceSnoop.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SpaceSnoop.Benchmarks;

internal sealed class ProgressChannelMeasurement
{
    private const int UnpacedReports = 497_227;
    private const int ProductionRate = 2170;
    private const int PacedSeconds = 8;
    private const double LateThresholdMs = 100;

    private static readonly TimeSpan PollInterval = UiProgressPoller.DefaultInterval;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(50);
    private static readonly string[] Paths = [.. Enumerable.Range(0, 64).Select(static index => $@"C:\Sources\проект\узел-{index:D4}\файл-{index:D4}.bin")];

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public static void Run()
    {
        var thread = new Thread(RunScenarios);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static void RunScenarios()
    {
        var measurement = new ProgressChannelMeasurement();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(measurement._dispatcher));

        foreach (var rate in new[] { 0, ProductionRate })
        {
            foreach (var bound in new[] { false, true })
            {
                measurement.Poll(rate, bound);
                measurement.Push(rate, bound);
            }
        }

        measurement.Poll(50_000, true);

        foreach (var rate in new[] { 5_000, 10_000, 25_000, 50_000 })
        {
            measurement.Push(rate, true);
        }
    }

    private void Poll(int rate, bool bound)
    {
        var sink = new Sink();
        var state = new OperationProgressState();
        var timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => sink.Apply(state.CreateSnapshot()), _dispatcher);

        using var screen = Screen.For(sink, bound);

        var sample = Flood(state, rate, timer);

        sink.Apply(state.CreateSnapshot());

        Report("опрос таймером", rate, bound, sample, sink);
    }

    private void Push(int rate, bool bound)
    {
        var sink = new Sink();
        var progress = new Progress<OperationProgress>(sink.Apply);

        using var screen = Screen.For(sink, bound);

        var sample = Flood(progress, rate, null);

        Report("пуш на объект", rate, bound, sample, sink);
    }

    private static void Report(string channel, int rate, bool bound, Sample sample, Sink sink)
    {
        var pace = rate > 0 ? $"{rate:N0} объектов/с" : "без ограничения темпа";
        var screen = bound ? "с биндингами" : "без биндингов";

        Console.WriteLine(
            $"{channel}, {pace}, {screen}: отчётов {sample.Reports:N0} за {sample.ElapsedMs:N0} мс, "
            + $"обновлений UI {sink.Updates:N0}, наибольшее опоздание тика {sample.MaxDelayMs:N0} мс "
            + $"на {sample.MaxDelayAtMs:N0}-й мс, опозданий свыше 100 мс {sample.LateTicks:N0} из {sample.Ticks:N0}, "
            + $"последний счётчик {sink.LastCompleted:N0}.");
    }

    private Sample Flood(IProgress<OperationProgress> progress, int rate, DispatcherTimer? timer)
    {
        var total = rate > 0 ? rate * PacedSeconds : UnpacedReports;
        using var watchdog = new Watchdog(_dispatcher);
        var frame = new DispatcherFrame();
        var clock = Stopwatch.StartNew();

        watchdog.Start();
        timer?.Start();

        var worker = Task.Run(() => Produce(progress, total, rate));

        _ = worker.ContinueWith(
            produced => _dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)),
            TaskScheduler.Default);

        Dispatcher.PushFrame(frame);

        watchdog.Stop();
        timer?.Stop();
        worker.GetAwaiter().GetResult();

        return new(total, clock.Elapsed.TotalMilliseconds, watchdog.MaxDelayMs, watchdog.MaxDelayAtMs, watchdog.LateTicks, watchdog.Ticks);
    }

    private static void Produce(IProgress<OperationProgress> progress, int total, int rate)
    {
        var clock = Stopwatch.StartNew();

        for (var index = 1; index <= total; index++)
        {
            if (rate > 0)
            {
                var due = TimeSpan.FromSeconds((double)index / rate);

                while (clock.Elapsed < due)
                {
                    Thread.SpinWait(80);
                }
            }

            progress.Report(new(index, Paths[index % Paths.Length], (long)index * 4096));
        }
    }

    private readonly record struct Sample(int Reports, double ElapsedMs, double MaxDelayMs, double MaxDelayAtMs, int LateTicks, int Ticks);

    private sealed class Screen : IDisposable
    {
        private readonly VisualTestHost? _host;

        private Screen(VisualTestHost? host)
        {
            _host = host;
        }

        public static Screen For(Sink sink, bool bound)
        {
            if (!bound)
            {
                return new(null);
            }

            var panel = new StackPanel
            {
                DataContext = sink,
            };

            var path = new TextBlock();
            var count = new TextBlock();

            path.SetBinding(TextBlock.TextProperty, new Binding(nameof(Sink.CurrentPath)));
            count.SetBinding(TextBlock.TextProperty, new Binding(nameof(Sink.CountText)));
            panel.Children.Add(path);
            panel.Children.Add(count);

            var host = VisualTestHost.Show(
                new()
                {
                    Content = panel,
                },
                640,
                200);

            host.Settle();

            return new(host);
        }

        public void Dispose()
        {
            _host?.Dispose();
        }
    }

    private sealed class Sink : INotifyPropertyChanged
    {
        private string _currentPath = string.Empty;
        private string _countText = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Updates { get; private set; }

        public int LastCompleted { get; private set; }

        public string CurrentPath
        {
            get => _currentPath;
            private set
            {
                _currentPath = value;
                PropertyChanged?.Invoke(this, new(nameof(CurrentPath)));
            }
        }

        public string CountText
        {
            get => _countText;
            private set
            {
                _countText = value;
                PropertyChanged?.Invoke(this, new(nameof(CountText)));
            }
        }

        public void Apply(OperationProgress update)
        {
            CurrentPath = update.Current;
            CountText = $"{update.Completed:N0} · {SizeFormatter.Format(update.Bytes)}";
            LastCompleted = update.Completed;
            Updates++;
        }
    }

    private sealed class Watchdog : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly DispatcherTimer _timer;

        private double _last;

        public double MaxDelayMs { get; private set; }

        public double MaxDelayAtMs { get; private set; }

        public int LateTicks { get; private set; }

        public int Ticks { get; private set; }

        public Watchdog(Dispatcher dispatcher)
        {
            _timer = new(SampleInterval, DispatcherPriority.Background, (_, _) => OnTick(), dispatcher);
        }

        public void Start()
        {
            _last = _clock.Elapsed.TotalMilliseconds;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void Dispose()
        {
            _timer.Stop();
        }

        private void OnTick()
        {
            var now = _clock.Elapsed.TotalMilliseconds;

            var delay = Math.Max(0, now - _last - SampleInterval.TotalMilliseconds);

            if (delay > MaxDelayMs)
            {
                MaxDelayMs = delay;
                MaxDelayAtMs = now;
            }

            if (delay > LateThresholdMs)
            {
                LateTicks++;
            }

            _last = now;
            Ticks++;
        }
    }
}
