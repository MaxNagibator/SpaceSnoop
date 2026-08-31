using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class ListNavigation
{
    private const double OffsetEpsilon = 0.5;

    private const double LeadDivisor = 3;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled",
        typeof(bool),
        typeof(ListNavigation),
        new(false, OnEnabledChanged));

    public static readonly DependencyProperty FollowIndexProperty = DependencyProperty.RegisterAttached("FollowIndex",
        typeof(int),
        typeof(ListNavigation),
        new(-1, OnFollowIndexChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State",
        typeof(ListNavigationState),
        typeof(ListNavigation),
        new(default(ListNavigationState)));

    public static bool GetEnabled(DependencyObject element)
    {
        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        element.SetValue(EnabledProperty, value);
    }

    public static int GetFollowIndex(DependencyObject element)
    {
        return (int)element.GetValue(FollowIndexProperty);
    }

    public static void SetFollowIndex(DependencyObject element, int value)
    {
        element.SetValue(FollowIndexProperty, value);
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list)
        {
            return;
        }

        Detach(list);

        if (e.NewValue is not true)
        {
            return;
        }

        list.SetValue(StateProperty, new ListNavigationState());
        list.Loaded += OnLoaded;
        list.KeyDown += OnKeyDown;
        list.PreviewMouseWheel += OnWheel;

        if (list.IsLoaded)
        {
            Bind(list);
        }
    }

    private static void OnFollowIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemsControl list && list.GetValue(StateProperty) is ListNavigationState state)
        {
            Follow(state, (int)e.NewValue);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ItemsControl list)
        {
            Bind(list);
        }
    }

    private static void Bind(ItemsControl list)
    {
        if (list.GetValue(StateProperty) is not ListNavigationState state || Descendant<ScrollViewer>(list) is not { } scroll)
        {
            return;
        }

        if (state.Scroll is { } previous)
        {
            if (ReferenceEquals(previous, scroll))
            {
                return;
            }

            previous.ScrollChanged -= OnScrollChanged;
            previous.ClearValue(StateProperty);
        }

        state.Scroll = scroll;
        scroll.SetValue(StateProperty, state);
        scroll.ScrollChanged += OnScrollChanged;

        Follow(state, GetFollowIndex(list));
    }

    private static void Detach(ItemsControl list)
    {
        list.Loaded -= OnLoaded;
        list.KeyDown -= OnKeyDown;
        list.PreviewMouseWheel -= OnWheel;

        if (list.GetValue(StateProperty) is not ListNavigationState state)
        {
            return;
        }

        if (state.Scroll is { } scroll)
        {
            scroll.ScrollChanged -= OnScrollChanged;
            scroll.ClearValue(StateProperty);
            state.Scroll = null;
        }

        list.ClearValue(StateProperty);
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll
            || scroll.GetValue(StateProperty) is not ListNavigationState state
            || e.VerticalChange == 0)
        {
            return;
        }

        var expected = state.Expected;
        state.Expected = null;

        if (expected is not { } offset || Math.Abs(e.VerticalOffset - offset) > OffsetEpsilon)
        {
            state.Suppressed = true;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ItemsControl list
            || list.GetValue(StateProperty) is not ListNavigationState { Scroll: { } scroll })
        {
            return;
        }

        var offset = e.Key switch
        {
            Key.PageUp => scroll.VerticalOffset - PageStep(scroll),
            Key.PageDown => scroll.VerticalOffset + PageStep(scroll),
            Key.Home => 0d,
            Key.End => scroll.ScrollableHeight,
            _ => double.NaN,
        };

        if (double.IsNaN(offset))
        {
            return;
        }

        scroll.ScrollToVerticalOffset(offset);
        e.Handled = true;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ItemsControl list
            || list.GetValue(StateProperty) is not ListNavigationState { Scroll: { } scroll }
            || e.Delta == 0)
        {
            return;
        }

        var notches = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset - (notches * PageStep(scroll)));
        e.Handled = true;
    }

    private static void Follow(ListNavigationState state, int index)
    {
        if (state.Suppressed || index < 0 || state.Scroll is not { } scroll)
        {
            return;
        }

        var viewport = scroll.ViewportHeight;

        if (viewport <= 0 || (index >= scroll.VerticalOffset && index < scroll.VerticalOffset + viewport))
        {
            return;
        }

        var target = Math.Clamp(index - Math.Floor(viewport / LeadDivisor), 0, scroll.ScrollableHeight);

        if (Math.Abs(target - scroll.VerticalOffset) <= OffsetEpsilon)
        {
            return;
        }

        state.Expected = target;
        scroll.ScrollToVerticalOffset(target);
    }

    private static double PageStep(ScrollViewer scroll)
    {
        return Math.Max(1, Math.Floor(scroll.ViewportHeight) - 1);
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T found)
            {
                return found;
            }

            if (Descendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class ListNavigationState
    {
        public ScrollViewer? Scroll { get; set; }

        public double? Expected { get; set; }

        public bool Suppressed { get; set; }
    }
}
