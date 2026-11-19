using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using KeepShell.Views;
using KeepShell.Views.Modal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.Views.Dialogs;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [Test]
    public void Раскрытие_причины_обновляется_из_модели_и_обратно()
    {
        var issue = CleanupIssue.From("«C:\\Temp\\файл.tmp»: файл изменился после обхода");
        var expander = new Expander();
        try
        {
            expander.SetBinding(Expander.IsExpandedProperty, new Binding(nameof(CleanupIssue.IsExpanded))
            {
                Source = issue,
                Mode = BindingMode.TwoWay,
            });

            issue.IsExpanded = true;
            Settle();
            Assert.That(expander.IsExpanded, Is.True);

            expander.SetCurrentValue(Expander.IsExpandedProperty, false);
            Assert.That(issue.IsExpanded, Is.False);
        }
        finally
        {
            BindingOperations.ClearAllBindings(expander);
        }
    }

    [TestCase(AppThemes.LightKey, 1, 900, 600, false)]
    [TestCase(AppThemes.DarkKey, 1, 900, 600, false)]
    [TestCase(AppThemes.LightKey, 1.6, 900, 600, false)]
    [TestCase(AppThemes.DarkKey, 1.6, 900, 600, false)]
    [TestCase(AppThemes.LightKey, 1.6, 880, 584, false)]
    [TestCase(AppThemes.DarkKey, 1.6, 880, 584, false)]
    [TestCase(AppThemes.LightKey, 1.6, 880, 584, true)]
    [TestCase(AppThemes.DarkKey, 1.6, 880, 584, true)]
    [TestCase(AppThemes.LightKey, 1.6, 900, 1100, false)]
    [TestCase(AppThemes.DarkKey, 1.6, 900, 1100, false)]
    public void Итог_частичной_очистки_показывает_причины_без_недоделанной_полосы(string theme, double scale, double testWidth, double testHeight, bool failure)
    {
        using var transition = ThemeTransitionOverlay.Suppress();
        _shell.Toasts.Toasts.Clear();
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var originalScale = preferences.FontScale;
        var (width, height) = (_window.Width, _window.Height);
        var modals = _services.GetRequiredService<ModalHostViewModel>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        VisualTestHost.Run(async () =>
        {
            await GalleryStates.SettleCleanupAsync(_services);
            page.Docker.CancelCommand?.Execute(null);
            await GalleryStates.QuiesceAsync(() => page.Docker.IsBusy, "опрос Docker");
        });

        page.Windows.ResetForAutomation();
        page.SelectedSectionIndex = 0;
        Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
        var directory = Path.Combine(_fixture.Root, "partial-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var kept = Path.Combine(directory, "занят-приложением.tmp");
        var removed = Path.Combine(directory, "свободен.tmp");
        File.WriteAllText(kept, "keep");
        File.WriteAllText(removed, "remove");
        FileStream? locked = null;
        CleanupProgressDialogViewModel? vm = null;
        try
        {
            locked = File.Open(kept, FileMode.Open, FileAccess.Read, FileShare.Read);
            preferences.FontScale = scale;
            ThemeManager.Apply(theme);
            (_window.Width, _window.Height) = (testWidth, testHeight);
            var target = new CleanupTarget
            {
                Id = "PartialResult",
                Name = "Временные файлы фикстуры",
                Description = "Один свободный и один занятый файл",
                Kind = CleanupTargetKind.Directory,
                Path = directory,
                MinimumAge = TimeSpan.Zero,
            };

            ILogger logger = failure ? new FailingCleanupStartLogger() : NullLogger.Instance;
            vm = new(
                new([target], 10, 2),
                new(),
                new FakeUiDispatcher(),
                logger,
                _services.GetRequiredService<PerformanceOperations>());

            _ = modals.ShowAsync(vm);
            Settle();
            var view = Descendant<CleanupProgressDialogView>(_window)!;
            VisualTestHost.Run(() => vm.StartCommand.ExecuteAsync(null));
            Settle();
            var details = Descendants<Expander>(view).Single(x => Equals(x.Header, "Что осталось"));
            var failureDetails = Descendants<Expander>(view).Single(x => Equals(x.Header, "Сведения об ошибке"));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(vm.HasFailure, Is.EqualTo(failure));
                Assert.That(vm.ResultText, Does.StartWith(failure ? "Ошибка:" : "Завершено с пропусками:"));
                Assert.That(Descendants<ProgressBar>(view).Any(x => x.IsVisible), Is.False);
                Assert.That(Descendants<TextBlock>(view).Any(x => x.IsVisible && x.Text == vm.CountText), Is.False);
                Assert.That(details.IsVisible, Is.EqualTo(!failure));
                Assert.That(failureDetails.IsVisible, Is.EqualTo(failure));
                Assert.That(details.IsExpanded, Is.False);
                Assert.That(failureDetails.IsExpanded, Is.False);
                Assert.That(Descendants<TextBlock>(view).Any(x => x.IsVisible && x.Text == vm.ResultText), Is.False);
                Assert.That(Descendants<TextBlock>(view).Any(x => x.IsVisible && x.Text.Contains(kept, StringComparison.Ordinal)), Is.False);
                Assert.That(Descendants<TextBlock>(view).Any(x => x.IsVisible && x.Text == vm.ResultHeading), Is.True);
                Assert.That(File.Exists(kept), Is.True);
                Assert.That(File.Exists(removed), Is.EqualTo(failure));
                Assert.That(page.Docker.IsBusy, Is.False);
                Assert.That(page.Windows.IsBusy, Is.False);
            }

            SaveCleanupResultFrame(_window, $"summary-{theme}-{(int)(scale * 100)}-{testWidth}x{testHeight}-{failure}");
            details.IsExpanded = !failure;
            failureDetails.IsExpanded = failure;
            Settle();
            var close = Descendants<Button>(view).Single(x => x.IsVisible && Equals(x.Content, "Закрыть"));
            var surface = Descendants<Border>(Ancestor<ModalHost>(view)!).Single(x => x.Name == "Surface");
            var closeBounds = close.TransformToAncestor(surface).TransformBounds(new(close.RenderSize));
            var body = (ScrollViewer)view.FindName("Body");
            var bodyBounds = body.TransformToAncestor(surface).TransformBounds(new(body.RenderSize));
            using (Assert.EnterMultipleScope())
            {
                if (failure)
                {
                    Assert.That(body.ScrollableHeight, Is.GreaterThan(0));
                }
                else
                {
                    Assert.That(Descendants<TextBlock>(details).Any(x => x.IsVisible && x.Text == Path.GetFileName(kept)), Is.True);
                    Assert.That(Descendants<TextBlock>(details).Any(x => x.IsVisible && x.Text.Contains(kept, StringComparison.Ordinal)), Is.False);
                    var itemDetails = Descendants<Expander>(details).Single(x => Equals(x.Header, "Путь и подробности"));
                    itemDetails.IsExpanded = true;
                    Settle();
                    Assert.That(Descendants<TextBlock>(itemDetails).Any(x => x.IsVisible && x.Text.Contains(kept, StringComparison.Ordinal)), Is.True);
                    itemDetails.IsExpanded = false;
                }

                if (testHeight > 1000)
                {
                    Assert.That(body.ViewportHeight, Is.GreaterThan(180));
                }

                Assert.That(closeBounds.Top, Is.GreaterThanOrEqualTo(0));
                Assert.That(bodyBounds.Bottom, Is.LessThanOrEqualTo(closeBounds.Top));
                Assert.That(closeBounds.Bottom, Is.LessThanOrEqualTo(surface.ActualHeight));
                Assert.That(closeBounds.Right, Is.LessThanOrEqualTo(surface.ActualWidth));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }

            if (failure)
            {
                SaveCleanupResultFrame(_window, $"partial-{theme}-{(int)(scale * 100)}-{testWidth}x{testHeight}-failure-details");
                failureDetails.IsExpanded = false;
            }

            Settle();
            SaveCleanupResultFrame(_window, $"partial-{theme}-{(int)(scale * 100)}-{testWidth}x{testHeight}-{failure}");
        }
        finally
        {
            if (vm is not null)
            {
                VisualTestHost.Run(() => vm.StopAsync());
            }

            modals.RequestCancelCommand.Execute(null);
            locked?.Dispose();
            Directory.Delete(directory, true);
            preferences.FontScale = originalScale;
            (_window.Width, _window.Height) = (width, height);
            ThemeManager.Apply(AppThemes.LightKey);
            Settle();
        }
    }

    private static void SaveCleanupResultFrame(FrameworkElement view, string name, [CallerFilePath] string caller = "")
    {
        if (!TestContext.Parameters.Get("CaptureCleanupResult", false))
        {
            return;
        }

        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
        var directory = Path.Combine(root, ".kc", "scratch", "cleanup-partial-result", "frames");
        Directory.CreateDirectory(directory);
        ViewCapture.Save(view, Path.Combine(directory, name + ".png"));
    }

    private sealed class FailingCleanupStartLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == "CleanupRunStarted")
            {
                throw new IOException(string.Join(" ", Enumerable.Repeat("Не удалось прочитать данные для очистки. Повторите операцию после восстановления доступа.", 30)));
            }
        }
    }
}
