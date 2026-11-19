using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Controls;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Schedule;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Overview;
using SpaceSnoop.Wpf.Views.Scan;
using SpaceSnoop.Wpf.Views.Schedule;
using SpaceSnoop.Wpf.Views.Sync;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCase(1000, false)]
    [TestCase(620, true)]
    public void Направление_синхронизации_остаётся_между_полями_при_переносе(double width, bool wraps)
    {
        var row = new SyncEndpointsRow
        {
            DataContext = _services.GetRequiredService<SyncViewModel>(),
        };
        using var host = VisualTestHost.Show(new Window { Content = row }, width, 240);
        var fields = Descendants<TextBox>(row).Where(static box => box.IsVisible).ToArray();
        Assert.That(fields, Has.Length.EqualTo(2));
        var left = fields.Single(static box => Equals(box.ToolTip, "Каталог слева"));
        var right = fields.Single(static box => Equals(box.ToolTip, "Каталог справа"));
        var direction = Descendants<Button>(row).Single(static button =>
            AutomationProperties.GetName(button)?.StartsWith("Направление:", StringComparison.Ordinal) == true);

        var leftBounds = left.TransformToAncestor(row).TransformBounds(new(left.RenderSize));
        var rightBounds = right.TransformToAncestor(row).TransformBounds(new(right.RenderSize));
        var directionBounds = direction.TransformToAncestor(row).TransformBounds(new(direction.RenderSize));

        using (Assert.EnterMultipleScope())
        {
            if (wraps)
            {
                Assert.That(directionBounds.Top, Is.GreaterThanOrEqualTo(leftBounds.Bottom));
                Assert.That(directionBounds.Bottom, Is.LessThanOrEqualTo(rightBounds.Top));
            }
            else
            {
                Assert.That(directionBounds.Left, Is.GreaterThanOrEqualTo(leftBounds.Right));
                Assert.That(directionBounds.Right, Is.LessThanOrEqualTo(rightBounds.Left));
                Assert.That(leftBounds.Top, Is.EqualTo(rightBounds.Top).Within(1));
            }

            Assert.That(direction.IsEnabled, Is.True);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Обзор_при_ширине_1280_и_идущем_сравнении_переносит_кнопки_сводки_единой_группой()
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);

        try
        {
            _window.Width = 1280;
            _window.Height = 800;
            preferences.FontScale = FontScaleManager.DefaultScale;

            Assert.That(_shell.TryNavigate(SectionKey.Overview), Is.True, "Страница «Все профили» не открылась.");
            VisualTestHost.Run(() => GalleryStates.ApplyPageAsync(SectionKey.Overview, GalleryStates.Busy, _services, _fixture));
            Settle();

            var view = Descendant<OverviewView>(_window) ?? throw new InvalidOperationException("Страница обзора не найдена.");
            var summaryPanel = Descendant<StretchWrapPanel>(view) ?? throw new InvalidOperationException("Сводка обзора не найдена.");
            var actionsPanel = Descendant<WrapPanel>(summaryPanel) ?? throw new InvalidOperationException("Группа кнопок сводки не найдена.");
            var buttons = Descendants<Button>(actionsPanel).Where(static button => button.IsVisible).ToArray();
            var bounds = buttons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons, Has.Length.EqualTo(5), "Во время сравнения в сводке должны быть видны все пять кнопок.");
                Assert.That(
                    bounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count(),
                    Is.EqualTo(1),
                    "На ширине 1280 px кнопки сводки разъехались по строкам – «Синхронизировать всё» не должна отрываться от группы.");

                Assert.That(
                    bounds.Where(rect => rect.Left < -0.5 || rect.Right > (view.ActualWidth + 1)),
                    Is.Empty,
                    "Кнопка сводки выходит за ширину страницы обзора.");

                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            GalleryStates.ResetPage(SectionKey.Overview, GalleryStates.Busy, _services);
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [Test]
    public void Расписание_шапка_Профили_при_880x584_и_160_процентах_читается_и_не_обрезает_кнопки()
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var schedule = _services.GetRequiredService<ScheduleViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);

        try
        {
            Assume.That(schedule.HasProfiles, Is.True, "В фикстуре расписания должны быть профили для отображения кнопки «Выбрать все».");
            Assert.That(_shell.TryNavigate(SectionKey.Schedule), Is.True, "Страница «Расписание» не открылась.");

            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            preferences.FontScale = FontScaleManager.MaxScale;
            Settle();

            var view = Descendant<ScheduleView>(_window) ?? throw new InvalidOperationException("Страница расписания не найдена.");
            var headerPanel = Descendant<StretchWrapPanel>(view) ?? throw new InvalidOperationException("Адаптивная шапка «Профили» не найдена.");
            var heading = Descendants<TextBlock>(headerPanel).First(static text => text.Text == "Профили");
            var actionsPanel = Descendant<WrapPanel>(headerPanel) ?? throw new InvalidOperationException("Группа кнопок шапки «Профили» не найдена.");
            var narrowButtons = Descendants<Button>(actionsPanel).Where(static button => button.IsVisible).ToArray();
            var narrowBounds = narrowButtons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(narrowButtons, Has.Length.EqualTo(3), "В шапке «Профили» должны быть видны все три кнопки.");
                Assert.That(heading.ActualWidth, Is.GreaterThanOrEqualTo(80), "Заголовок «Профили» сжался в узком окне.");
                Assert.That(heading.ActualHeight, Is.LessThan(heading.FontSize * 2.0), "Заголовок «Профили» перенёсся на несколько строк.");
                Assert.That(
                    narrowBounds.Where(rect => rect.Left < -0.5 || rect.Right > (view.ActualWidth + 1)),
                    Is.Empty,
                    "Кнопка шапки «Профили» выходит за пределы страницы в окне 880x584 при 160 %.");
            }

            _window.Width = 1280;
            _window.Height = 800;
            preferences.FontScale = FontScaleManager.DefaultScale;
            Settle();

            var headingBounds = heading.TransformToAncestor(view).TransformBounds(new(heading.RenderSize));
            var wideButtons = Descendants<Button>(actionsPanel).Where(static button => button.IsVisible).ToArray();
            var wideBounds = wideButtons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    wideBounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count(),
                    Is.EqualTo(1),
                    "На ширине 1280 px кнопки шапки «Профили» не должны переноситься на вторую строку.");
                Assert.That(
                    headingBounds.Right,
                    Is.LessThanOrEqualTo(wideBounds[0].Left + 1),
                    "На ширине 1280 px заголовок и кнопки шапки «Профили» должны стоять в одной строке.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [Test]
    public void Сегментированные_переключатели_вне_настроек_при_крупном_шрифте_в_узком_окне_не_уходят_за_край()
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var settings = _services.GetRequiredService<ISettingsStore>();
        var sync = _services.GetRequiredService<SyncViewModel>();
        var overview = _services.GetRequiredService<OverviewViewModel>();
        var schedule = _services.GetRequiredService<ScheduleViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);
        var oldSyncMode = sync.Setup.SelectedModeIndex;
        var oldBatchMode = settings.GetInt(SettingsKeys.BatchMode, settings.GetInt(SettingsKeys.SyncMode));
        var overviewRow = overview.Rows.Rows[0];
        var oldOverviewMode = overviewRow.SelectedModeIndex;

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            preferences.FontScale = FontScaleManager.MaxScale;

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
            Settle();
            var scanView = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
            AssertSegmentedWithin(scanView, expectedCount: 1, "Сканирование");

            Assert.That(_shell.TryNavigate(SectionKey.Sync), Is.True, "Страница «Синхронизация» не открылась.");
            sync.Setup.SelectedModeIndex = 2;
            Settle();
            var moreToggle = (ToggleButton)(ViewCapture.Find(_window, "MoreToggle") ?? throw new InvalidOperationException("Кнопка «Ещё» тулбара синхронизации не найдена."));
            moreToggle.IsChecked = true;
            Settle();

            try
            {
                var morePopup = (Popup)(moreToggle.FindName("MorePopup") ?? throw new InvalidOperationException("Всплывающая панель «Ещё» не найдена."));
                var moreContent = (FrameworkElement)(morePopup.Child ?? throw new InvalidOperationException("Содержимое панели «Ещё» не построено."));
                moreContent.UpdateLayout();
                Settle();
                AssertSegmentedWithin(moreContent, expectedCount: 2, "Синхронизация – Ещё");
            }
            finally
            {
                moreToggle.IsChecked = false;
                Settle();
            }

            Assert.That(_shell.TryNavigate(SectionKey.Overview), Is.True, "Страница «Все профили» не открылась.");
            overviewRow.SelectedModeIndex = 2;
            Settle();
            var overviewView = Descendant<OverviewView>(_window) ?? throw new InvalidOperationException("Страница обзора не найдена.");
            var rowView = Descendant<OverviewRow>(overviewView) ?? throw new InvalidOperationException("Строка профиля на обзоре не найдена.");
            var rowToggle = (ToggleButton)(rowView.FindName("RowMoreToggle") ?? throw new InvalidOperationException("Кнопка настроек строки профиля не найдена."));
            rowToggle.IsChecked = true;
            Settle();

            try
            {
                var rowPopup = Descendant<Popup>(rowView) ?? throw new InvalidOperationException("Всплывающая панель строки профиля не найдена.");
                var rowPopupContent = (FrameworkElement)(rowPopup.Child ?? throw new InvalidOperationException("Содержимое всплывающей панели строки профиля не построено."));
                rowPopupContent.UpdateLayout();
                Settle();
                AssertSegmentedWithin(rowPopupContent, expectedCount: 2, "Обзор – строка профиля");
            }
            finally
            {
                rowToggle.IsChecked = false;
                Settle();
            }

            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True, "Страница «Очистка» не открылась.");
            VisualTestHost.Run(() => GalleryStates.ApplyPageAsync(SectionKey.Cleanup, GalleryStates.Idle, _services, _fixture));
            Settle();
            var cleanupView = Descendant<CleanupView>(_window) ?? throw new InvalidOperationException("Страница очистки не найдена.");
            AssertSegmentedWithin(cleanupView, expectedCount: 1, "Очистка – вкладки");

            VisualTestHost.Run(() => GalleryStates.ApplyPageAsync(SectionKey.Cleanup, GalleryStates.Development, _services, _fixture));
            Settle();
            AssertSegmentedWithin(cleanupView, expectedCount: 2, "Очистка – разработка");
            GalleryStates.ResetPage(SectionKey.Cleanup, GalleryStates.Development, _services);
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Schedule), Is.True, "Страница «Расписание» не открылась.");
            Settle();
            Assume.That(schedule.Profiles.Count, Is.GreaterThanOrEqualTo(2), "В фикстуре расписания меньше двух профилей.");
            schedule.Profiles[0].EditCommand.Execute(null);
            schedule.Profiles[0].SelectedModeIndex = 2;
            schedule.Profiles[1].IsSelected = true;
            Settle();

            try
            {
                var scheduleView = Descendant<ScheduleView>(_window) ?? throw new InvalidOperationException("Страница расписания не найдена.");
                AssertSegmentedWithin(scheduleView, expectedCount: 4, "Расписание – пакетная панель и карточка профиля");
            }
            finally
            {
                schedule.Profiles[0].CancelCommand.Execute(null);
                schedule.Bulk.ClearSelectionCommand.Execute(null);
                Settle();
            }

            using var batchDialog = new BatchCreateProfilesDialogViewModel(
                settings,
                _services.GetRequiredService<IFilePicker>(),
                _services.GetRequiredService<IUiDispatcher>())
            {
                SelectedModeIndex = 2,
            };

            _ = _shell.Modal.ShowAsync(batchDialog);
            Settle();

            try
            {
                var batchView = Descendant<BatchCreateProfilesDialogView>(_window) ?? throw new InvalidOperationException("Диалог создания пачки профилей не показан.");
                var dialogBounds = batchView.TransformToAncestor(_window).TransformBounds(new(batchView.RenderSize));
                Assert.That(dialogBounds.Right, Is.LessThanOrEqualTo(_window.ActualWidth + 1), "Диалог пачки профилей выходит за правый край окна.");
                AssertSegmentedWithin(batchView, expectedCount: 2, "Диалог создания пачки профилей");
            }
            finally
            {
                _shell.Modal.RequestCancel();
                Settle();
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            _shell.Modal.RequestCancel();
            GalleryStates.ResetPage(SectionKey.Cleanup, GalleryStates.Development, _services);
            sync.Setup.SelectedModeIndex = oldSyncMode;
            overviewRow.SelectedModeIndex = oldOverviewMode;
            settings.SetInt(SettingsKeys.BatchMode, oldBatchMode);
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    private static void AssertSegmentedWithin(FrameworkElement container, int expectedCount, string scope)
    {
        var segmentedStyle = (Style)container.FindResource("ListBox.Segmented");
        var compactStyle = (Style)container.FindResource("ListBox.SegmentedCompact");
        var listBoxes = Descendants<ListBox>(container)
            .Where(listBox => listBox.IsVisible && (ReferenceEquals(listBox.Style, segmentedStyle) || ReferenceEquals(listBox.Style, compactStyle)))
            .ToArray();

        Assert.That(listBoxes, Has.Length.EqualTo(expectedCount), $"Неожиданное число сегментированных переключателей в области «{scope}».");

        foreach (var listBox in listBoxes)
        {
            var name = AutomationProperties.GetName(listBox);
            var label = string.IsNullOrEmpty(name) ? scope : $"{scope} ({name})";
            var bounds = listBox.TransformToAncestor(container).TransformBounds(new(listBox.RenderSize));

            Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(-0.5), $"Переключатель «{label}» уходит за левый край.");
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(container.ActualWidth + 1), $"Переключатель «{label}» выходит за правый край ({bounds.Right:F1} > {container.ActualWidth:F1}).");

            var items = Descendants<ListBoxItem>(listBox).Where(static item => item.IsVisible).ToArray();
            Assert.That(items, Is.Not.Empty, $"У переключателя «{label}» не отрисованы сегменты.");

            foreach (var item in items)
            {
                var itemBounds = item.TransformToAncestor(container).TransformBounds(new(item.RenderSize));
                Assert.That(itemBounds.Left, Is.GreaterThanOrEqualTo(-0.5), $"Сегмент переключателя «{label}» уходит за левый край.");
                Assert.That(itemBounds.Right, Is.LessThanOrEqualTo(container.ActualWidth + 1), $"Сегмент переключателя «{label}» выходит за правый край ({itemBounds.Right:F1} > {container.ActualWidth:F1}).");
            }
        }
    }
}
