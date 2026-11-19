namespace SpaceSnoop.Wpf.Views.Behaviors;

public static class ToolTipOwnerWatch
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnOpened));
    }

    private static void OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ToolTip { PlacementTarget: { } owner } toolTip)
        {
            return;
        }

        if (!owner.IsVisible)
        {
            toolTip.SetCurrentValue(ToolTip.IsOpenProperty, false);
            return;
        }

        DependencyPropertyChangedEventHandler? hidden = null;
        RoutedEventHandler? closed = null;

        hidden = (_, args) =>
        {
            if (args.NewValue is false)
            {
                toolTip.SetCurrentValue(ToolTip.IsOpenProperty, false);
            }
        };

        closed = (_, _) =>
        {
            owner.IsVisibleChanged -= hidden;
            toolTip.Closed -= closed;
        };

        owner.IsVisibleChanged += hidden;
        toolTip.Closed += closed;
    }
}
