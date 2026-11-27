using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Behaviors;
using SpaceSnoop.Wpf.Views.Scan;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanTargetUncTests
{
    private string _root = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-unc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _window = _services.GetRequiredService<MainWindow>();
        _shell = _services.GetRequiredService<ShellViewModel>();
        _host = VisualTestHost.Show(_window, 1100, 720);
        _host.Settle();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Поле_пути_синхронизируется_с_выбором_и_неполный_UNC_отключает_пуск_без_обращения_к_диску()
    {
        using var sink = BindingErrorSink.Attach();
        var scan = _services.GetRequiredService<ScanViewModel>();
        var previousExists = ScanTargetProbe.DirectoryExists;
        var probed = 0;

        ScanTargetProbe.DirectoryExists = path =>
        {
            Interlocked.Increment(ref probed);
            return false;
        };

        var originalDrive = scan.SelectedDrive;
        var originalFirstRun = scan.FirstRun.IsVisible;

        try
        {
            scan.FirstRun.IsVisible = false;
            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True);
            _host.Settle();

            var picker = Descendant<ScanTargetPicker>(_window) ?? throw new InvalidOperationException("ScanTargetPicker не найден.");
            var pathBox = picker.FindName("TargetPathBox") as TextBox ?? throw new InvalidOperationException("TargetPathBox не найден.");

            Assert.That(PathSuggest.GetEnabled(pathBox), Is.EqualTo(scan.OperationPreferences.SyncPathSuggest));

            var volume = scan.Drives.Volumes[0].Path;
            scan.SelectTargetCommand.Execute(volume);
            _host.Settle();
            Assert.That(pathBox.Text, Is.EqualTo(volume));

            foreach (var incomplete in new[] { @"\\", @"\\nas", @"\\nas\" })
            {
                pathBox.Text = incomplete;
                _host.Settle();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(scan.SelectedDrive, Is.EqualTo(incomplete));
                    Assert.That(scan.IsIncompleteUncTarget, Is.True);
                    Assert.That(scan.StartCommand.CanExecute(null), Is.False, incomplete);
                    Assert.That(scan.StartCaption, Is.EqualTo(@"Укажите \\сервер\шару"), incomplete);
                }
            }

            pathBox.Text = @"\\nas\media";
            _host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scan.IsIncompleteUncTarget, Is.False);
                Assert.That(scan.HasTarget, Is.True);
                Assert.That(scan.StartCommand.CanExecute(null), Is.True);
                Assert.That(probed, Is.Zero, "Неполный или набираемый UNC не должен вызывать Directory.Exists до старта.");
                Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
            }
        }
        finally
        {
            ScanTargetProbe.DirectoryExists = previousExists;
            scan.FirstRun.IsVisible = originalFirstRun;
            scan.SelectedDrive = originalDrive;
            _host.Settle();
        }
    }

    [Test]
    public void Enter_в_поле_пути_при_закрытых_подсказках_запускает_сканирование()
    {
        var scan = _services.GetRequiredService<ScanViewModel>();
        var probeTarget = Path.Combine(_root, "enter-target");
        Directory.CreateDirectory(probeTarget);
        File.WriteAllText(Path.Combine(probeTarget, "sample.txt"), "ok");

        var originalDrive = scan.SelectedDrive;
        var originalFirstRun = scan.FirstRun.IsVisible;

        try
        {
            scan.FirstRun.IsVisible = false;
            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True);
            _host.Settle();

            var picker = Descendant<ScanTargetPicker>(_window) ?? throw new InvalidOperationException("ScanTargetPicker не найден.");
            var pathBox = picker.FindName("TargetPathBox") as TextBox ?? throw new InvalidOperationException("TargetPathBox не найден.");

            pathBox.Text = probeTarget;
            _host.Settle();

            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(pathBox)!, 0, Key.Enter)
            {
                RoutedEvent = UIElement.KeyDownEvent,
            };

            VisualTestHost.Run(async () =>
            {
                pathBox.RaiseEvent(args);

                if (scan.StartCommand.ExecutionTask is { } running)
                {
                    await running.WaitAsync(TimeSpan.FromSeconds(5));
                }
            });
            _host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(scan.HasResult, Is.True);
                Assert.That(scan.Summary.ResultPath, Is.EqualTo(probeTarget));
                Assert.That(scan.Drives.RecentDirectories.Select(static item => item.Path), Does.Contain(probeTarget));
            }
        }
        finally
        {
            scan.FirstRun.IsVisible = originalFirstRun;
            scan.SelectedDrive = originalDrive;
            scan.Drives.RemoveDrive(probeTarget);
            _host.Settle();
        }
    }

    [Test]
    public void Отмена_автоматического_скана_прерывает_ожидание_проверки_корня()
    {
        var scan = _services.GetRequiredService<ScanViewModel>();
        var originalDrive = scan.SelectedDrive;
        var originalRefusal = ScanTargetProbe.RootRefusal;
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? running = null;

        ScanTargetProbe.RootRefusal = _ =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            finished.TrySetResult();
            return null;
        };

        try
        {
            VisualTestHost.Run(async () =>
            {
                running = scan.ScanFromAutomationAsync(_root, cancellation.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                await running.WaitAsync(TimeSpan.FromSeconds(2));

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(scan.ScanWasCancelled, Is.True);
                    Assert.That(scan.Drives.HasDrive(_root), Is.False);
                }
            });
        }
        finally
        {
            release.TrySetResult();

            VisualTestHost.Run(async () =>
            {
                if (running is not null)
                {
                    await running.WaitAsync(TimeSpan.FromSeconds(5));
                }

                if (entered.Task.IsCompleted)
                {
                    await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
            });

            ScanTargetProbe.RootRefusal = originalRefusal;
            scan.Drives.RemoveDrive(_root);
            scan.SelectedDrive = originalDrive;
            _host.Settle();
        }
    }

    [Test]
    public void Ошибка_доступа_UNC_форматируется_с_адресной_диагностикой()
    {
        var message = ScanTargetProbe.DescribeScanError(new Win32Exception(5), @"\\nas\media");
        Assert.That(message, Does.Contain(@"\\nas\media"));
        Assert.That(message, Does.Contain("Диспетчере учётных данных"));
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                return match;
            }

            if (Descendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
