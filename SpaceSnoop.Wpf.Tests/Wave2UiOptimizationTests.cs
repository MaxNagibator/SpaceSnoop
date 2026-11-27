using KeepShell.Bootstrap;
using KeepShell.Testing;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Converters;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Behaviors;
using SpaceSnoop.Wpf.Views.Scan;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PathShape = System.Windows.Shapes.Path;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class Wave2UiOptimizationTests
{
    [Test]
    public void PriorityRowPanel_обновляет_план_при_смене_ширины_и_работает_сверх_64_элементов()
    {
        var name = new Border { MinWidth = 96 };
        var files = new Border { Width = 90 };
        var share = new Border { Width = 75 };
        var bar = new Border { MinWidth = 96, MaxWidth = 172, Child = new Border { Width = 60 } };

        var panel = new PriorityRowPanel
        {
            Children = { name, files, share, bar },
        };

        panel.Measure(new(300, 20));
        panel.Arrange(new(0, 0, 300, 20));
        Assert.That(LayoutInformation.GetLayoutSlot(files).Width, Is.Zero);
        Assert.That(LayoutInformation.GetLayoutSlot(share).Width, Is.Zero);

        panel.Measure(new(433, 20));
        panel.Arrange(new(0, 0, 433, 20));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(LayoutInformation.GetLayoutSlot(files).Width, Is.EqualTo(90));
            Assert.That(LayoutInformation.GetLayoutSlot(share).Width, Is.EqualTo(75));
            Assert.That(LayoutInformation.GetLayoutSlot(bar).Width, Is.EqualTo(172));
        }

        var widePanel = new PriorityRowPanel();
        widePanel.Children.Add(new Border { MinWidth = 40 });

        for (var i = 0; i < 66; i++)
        {
            widePanel.Children.Add(new Border { Width = 10 });
        }

        widePanel.Children.Add(new Border { MinWidth = 40, MaxWidth = 40 });

        widePanel.Measure(new(120, 20));
        widePanel.Arrange(new(0, 0, 120, 20));

        var keptCount = 0;

        for (var i = 1; i <= 66; i++)
        {
            if (LayoutInformation.GetLayoutSlot((FrameworkElement)widePanel.Children[i]).Width > 0)
            {
                keptCount++;
            }
        }

        Assert.That(keptCount, Is.EqualTo(4));
    }

    [Test]
    public void PathTrimmer_пропускает_ручное_усечение_односегментного_имени_при_включённом_TextTrimming()
    {
        var veryLongName = new string('x', 200);
        var trimmedNative = new TextBlock
        {
            Width = 60,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        trimmedNative.Measure(new(60, 30));
        trimmedNative.Arrange(new(0, 0, 60, 30));
        PathTrimmer.SetFullPath(trimmedNative, veryLongName);

        Assert.That(trimmedNative.Text, Is.EqualTo(veryLongName));

        trimmedNative.TextTrimming = TextTrimming.None;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(trimmedNative.Text, Does.Contain("…"));
            Assert.That(trimmedNative.Text.Length, Is.LessThan(veryLongName.Length));
        }

        trimmedNative.TextTrimming = TextTrimming.CharacterEllipsis;
        Assert.That(trimmedNative.Text, Is.EqualTo(veryLongName));

        var manualTrim = new TextBlock
        {
            Width = 60,
            TextTrimming = TextTrimming.None,
        };

        manualTrim.Measure(new(60, 30));
        manualTrim.Arrange(new(0, 0, 60, 30));
        PathTrimmer.SetFullPath(manualTrim, veryLongName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(manualTrim.Text, Does.Contain("…"));
            Assert.That(manualTrim.Text.Length, Is.LessThan(veryLongName.Length));
        }
    }

    [Test]
    public void PathTrimmer_сокращает_длинный_путь_посередине_и_расширяет_при_увеличении_ширины()
    {
        const string fullPath = @"C:\Users\Administrator\Documents\Projects\SpaceSnoop\SpaceSnoop.Wpf\Views\Scan\ScanTreeRowTemplate.xaml";

        var block = new TextBlock
        {
            Width = 180,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        block.Measure(new(180, 30));
        block.Arrange(new(0, 0, 180, 30));
        PathTrimmer.SetFullPath(block, fullPath);

        var narrowText = block.Text;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(narrowText, Does.StartWith(@"C:\…\"));
            Assert.That(narrowText, Does.EndWith("ScanTreeRowTemplate.xaml"));
        }

        block.Width = 2000;
        block.Measure(new(2000, 30));
        block.Arrange(new(0, 0, 2000, 30));
        block.UpdateLayout();

        Assert.That(block.Text, Is.EqualTo(fullPath));
    }

    [TestCase(PackIconLucideKind.File)]
    [TestCase(PackIconLucideKind.Folder)]
    [TestCase(PackIconLucideKind.EyeOff)]
    [TestCase(PackIconLucideKind.Files)]
    [TestCase(PackIconLucideKind.HardDrive)]
    public void IconGeometry_кэширует_замороженную_геометрию_для_каждой_иконки_строки(PackIconLucideKind kind)
    {
        var first = IconGeometry.Get(kind);
        var second = IconGeometry.Get(kind);

        var path = new PathShape();
        IconGeometry.SetKind(path, kind);
        var fromMarkup = new IconGeometryExtension(kind).ProvideValue(null!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.SameAs(Geometry.Empty));
            Assert.That(first.IsFrozen, Is.True);
            Assert.That(second, Is.SameAs(first));
            Assert.That(path.Data, Is.SameAs(first));
            Assert.That(fromMarkup, Is.SameAs(first));
            Assert.That(first.Bounds.Top, Is.LessThan(0));
        }
    }

    [Test]
    public void HeatBrushConverter_кэширует_замороженные_кисти_и_сбрасывается_при_смене_палитры()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        var converter = new HeatBrushConverter();

        try
        {
            HeatColor.Palette.Update(null);

            var first = (SolidColorBrush)converter.Convert(
                [0.5, AppDefaults.IntensityDefault, HeatColor.Palette.Revision],
                typeof(Brush),
                null,
                CultureInfo.InvariantCulture);

            var second = (SolidColorBrush)converter.Convert(
                [0.5, AppDefaults.IntensityDefault, HeatColor.Palette.Revision],
                typeof(Brush),
                null,
                CultureInfo.InvariantCulture);

            var dimmed = HeatBrushConverter.GetBrush(0.5, AppDefaults.IntensityDefault, dimmed: true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(first.IsFrozen, Is.True);
                Assert.That(second, Is.SameAs(first));
                Assert.That(dimmed.IsFrozen, Is.True);
                Assert.That(dimmed.Opacity, Is.EqualTo(0.5));
                Assert.That(dimmed, Is.Not.SameAs(first));
            }

            HeatColor.Palette.Update(
            [
                new(0.0, Color.FromRgb(0x10, 0x20, 0x30)),
                new(1.0, Color.FromRgb(0xF0, 0x40, 0x50)),
            ]);

            var updated = (SolidColorBrush)converter.Convert(
                [1.0, AppDefaults.IntensityDefault, HeatColor.Palette.Revision],
                typeof(Brush),
                null,
                CultureInfo.InvariantCulture);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(updated.IsFrozen, Is.True);
                Assert.That(updated.Color, Is.EqualTo(Color.FromRgb(0xF0, 0x40, 0x50)));
                Assert.That(updated, Is.Not.SameAs(first));
            }
        }
        finally
        {
            HeatColor.SyncFromActiveTheme();
        }
    }

    [Test]
    public void TreemapView_рендерится_повторно_и_троттлит_перепозиционирование_тултипа()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        var tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTreemapUi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var space = new DirectorySpace(tempDir, null, DateTime.Now, DateTime.Now);
            var folder = new DirectorySpace("sub", space, DateTime.Now, DateTime.Now);
            var payload = Path.Combine(tempDir, "payload.bin");
            File.WriteAllBytes(payload, new byte[2048]);
            folder.AddFile(new(payload));
            space.Add(folder);

            var loose = Path.Combine(tempDir, "loose.bin");
            File.WriteAllBytes(loose, new byte[1024]);
            space.AddFile(new(loose));

            var settings = new MemorySettings();
            var factory = new ScanNodeFactory(new(settings), new(settings), new FakeShellLauncher());
            var root = factory.CreateRoot(space, new());
            root.EnsureLoaded();
            root.Children[0].IsMarkedDeleted = true;

            var treemap = new ScanTreemapViewModel([root]);
            treemap.SetRoot(root);

            var mutableBrush = new SolidColorBrush(Colors.Coral);
            var view = new TreemapView
            {
                ItemsSource = treemap.TreemapTiles,
                SelectedItem = root.Children[0],
                Width = 400,
                Height = 240,
            };
            view.Resources["Fg.Primary"] = mutableBrush;

            using var host = VisualTestHost.Show(
                new()
                {
                    Content = view,
                },
                420,
                260);

            host.Settle();
            Assert.That(mutableBrush.IsFrozen, Is.False);
            mutableBrush.Color = Colors.Teal;
            view.InvalidateVisual();
            host.Settle();

            var tip = view.ShowTooltipForAutomation(new(50, 50));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tip, Is.Not.Null);
                Assert.That(view.ShouldRepositionTooltip(new(51, 50.5)), Is.False);
                Assert.That(view.ShouldRepositionTooltip(new(52, 50)), Is.True);
                Assert.That(view.ShouldRepositionTooltip(new(52.5, 50.5)), Is.False);
                Assert.That(view.ShouldRepositionTooltip(new(52, 52.5)), Is.True);
            }

            view.HideTooltipForAutomation();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
