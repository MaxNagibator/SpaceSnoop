using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Docker;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Docker;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCase(AppThemes.LightKey)]
    [TestCase(AppThemes.DarkKey)]
    public void Холодный_опрос_Docker_и_его_отмена_не_показывают_ошибку_доступности(string theme)
    {
        ThemeManager.Apply(theme);
        var runner = new DockerViewModelTests.PausedRunner();
        var vm = new DockerViewModel(
            DockerViewModelTests.IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        var view = new DockerView { DataContext = vm, Margin = new(20) };
        var window = new Window { Content = view };
        window.SetResourceReference(Window.BackgroundProperty, "Bg.Base");
        using var sink = BindingErrorSink.Attach();
        using var host = VisualTestHost.Show(window, 900, 600);
        var unavailable = ((StackPanel)((ScrollViewer)view.Content).Content)
            .Children.OfType<DockerUnavailableView>().Single();
        try
        {
            VisualTestHost.Run(async () =>
            {
                var run = vm.EnsureLoadedAsync();
                try
                {
                    await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    host.Settle();
                    Assert.That(vm.IsBusy, Is.True);
                    Assert.That(vm.CanRun, Is.False);
                    Assert.That(unavailable.IsVisible, Is.False);
                    SaveDockerFrame(window, $"cold-poll-{theme}");
                }
                finally
                {
                    vm.CancelCommand?.Execute(null);
                    await run;
                }
                host.Settle();
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.CanRun, Is.False);
                Assert.That(unavailable.IsVisible, Is.False);
                Assert.That(vm.StatusText, Does.Contain("остановлен"));
                SaveDockerFrame(window, $"cold-canceled-{theme}");
                runner.Completion.TrySetResult(new(-1, string.Empty, "Docker offline"));
                await vm.EnsureLoadedAsync();
                host.Settle();
                Assert.That(unavailable.IsVisible, Is.True);
                Assert.That(vm.CanRun, Is.False);
                SaveDockerFrame(window, $"failed-poll-{theme}");
            });
            Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
        }
        finally
        {
            ThemeManager.Apply(AppThemes.LightKey);
        }
    }

    [Test]
    public void Итоговая_строка_Docker_видна_при_пустом_StatusText_и_скрыта_без_категорий()
    {
        var runner = new DockerViewModelTests.PausedRunner { Pause = false };
        var service = new DockerService(runner, administratorCheck: static () => true);
        var vm = new DockerViewModel(service, new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        var view = new DockerView { DataContext = vm, Margin = new(20) };
        var window = new Window { Content = view };
        window.SetResourceReference(Window.BackgroundProperty, "Bg.Base");
        using var sink = BindingErrorSink.Attach();
        using var host = VisualTestHost.Show(window, 900, 600);

        VisualTestHost.Run(() => vm.EnsureLoadedAsync());
        vm.StatusText = null;
        host.Settle();

        var bucketsView = Descendant<DockerBucketsView>(view)!;
        var expectedTotals = vm.TotalsText;
        Assert.That(expectedTotals, Is.Not.Null.And.Not.Empty);
        var totalsBlock = Descendants<TextBlock>(bucketsView).Single(x => x.Text == expectedTotals);
        Assert.That(totalsBlock.IsVisible, Is.True);

        var cleanupView = Descendant<DockerCleanupView>(view)!;
        var expander = Descendants<Expander>(cleanupView).Single(x => Equals(x.Header, "Вернуть место на диск Windows"));
        expander.IsExpanded = true;
        vm.IsBusy = true;
        host.Settle();

        var compactButton = Descendants<Button>(expander).Single(x => ReferenceEquals(x.Command, vm.Compact.RunCommand));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(compactButton.IsEnabled, Is.False);
            Assert.That(compactButton.ToolTip, Is.EqualTo("Дождитесь завершения текущей операции Docker"));
        }

        vm.IsBusy = false;
        vm.Buckets.Clear();
        host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.TotalsText, Is.Null);
            Assert.That(totalsBlock.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
        }
    }

    private static string ColdDockerDirectory([CallerFilePath] string caller = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
        return Path.Combine(root, ".kc", "scratch", "cleanup-second-review", "cold-docker");
    }

    private static void SaveDockerFrame(FrameworkElement element, string name, [CallerFilePath] string caller = "")
    {
        if (!TestContext.Parameters.Get("CaptureColdDocker", false))
        {
            return;
        }

        var directory = ColdDockerDirectory(caller);
        Directory.CreateDirectory(directory);
        ViewCapture.Save(element, Path.Combine(directory, name + ".png"));
    }
}
