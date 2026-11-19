using SpaceSnoop.Wpf.Views.Settings;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Views;

public partial class SettingsView : UserControl, IView<SettingsViewModel>
{
    private const double AnchorSlack = 4;

    private readonly List<FrameworkElement> _anchors = [];

    private SettingsSectionList? _sections;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is not TextBox)
        {
            e.Handled = true;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_sections is not null || DataContext is not SettingsViewModel model)
        {
            return;
        }

        _sections = model.Sections;
        _sections.ChildActivated += OnChildActivated;
        _sections.PropertyChanged += OnSectionsChanged;

        var child = _sections.SelectedChild;
        _ = Dispatcher.InvokeAsync(() => ScrollTo(child), DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_sections is null)
        {
            return;
        }

        _sections.ChildActivated -= OnChildActivated;
        _sections.PropertyChanged -= OnSectionsChanged;
        _sections = null;
    }

    private void OnChildActivated(object? sender, SettingsSubsection child)
    {
        ScrollTo(child);
    }

    private void OnSectionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSectionList.Selected))
        {
            Cards.ScrollToTop();
        }
    }

    private void OnCardsScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_sections?.Selected is not { Children.Count: > 0 } section || Cards.ScrollableHeight <= 0)
        {
            return;
        }

        _sections.SelectedChild = Cards.VerticalOffset >= (Cards.ScrollableHeight - AnchorSlack)
            ? LastAnchored(section)
            : Reached(section);
    }

    private SettingsSubsection Reached(SettingsSection section)
    {
        SettingsSubsection? reached = null;

        foreach (var child in section.Children)
        {
            if (Offset(child) is { } offset && offset <= (Cards.VerticalOffset + AnchorSlack))
            {
                reached = child;
            }
        }

        return reached ?? section.Children[0];
    }

    private SettingsSubsection LastAnchored(SettingsSection section)
    {
        for (var index = section.Children.Count - 1; index >= 0; index--)
        {
            if (Offset(section.Children[index]) is not null)
            {
                return section.Children[index];
            }
        }

        return section.Children[0];
    }

    private void ScrollTo(SettingsSubsection? child)
    {
        if (child is not null && Offset(child) is { } offset)
        {
            Cards.ScrollToVerticalOffset(offset);
        }
    }

    private double? Offset(SettingsSubsection child)
    {
        if (Find(child.Key) is not { } block)
        {
            return null;
        }

        return block.TransformToAncestor(Cards).Transform(default).Y + Cards.VerticalOffset;
    }

    private FrameworkElement? Find(string key)
    {
        if (_anchors.Count == 0)
        {
            Collect(CardHost);
        }

        return _anchors.FirstOrDefault(anchor => anchor.IsVisible
            && string.Equals(SettingsBlock.GetKey(anchor), key, StringComparison.Ordinal));
    }

    private void Collect(DependencyObject root)
    {
        if (root is FrameworkElement element && SettingsBlock.GetKey(element) is { Length: > 0 })
        {
            _anchors.Add(element);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            Collect(VisualTreeHelper.GetChild(root, index));
        }
    }
}
