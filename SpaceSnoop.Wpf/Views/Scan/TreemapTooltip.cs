namespace SpaceSnoop.Wpf.Views.Scan;

internal enum TooltipSide
{
    None = 0,
    Below = 1,
    Above = 2,
    RightOf = 3,
    LeftOf = 4,
}

internal readonly record struct TooltipPlacement(TooltipSide Side, Point DeviceTopLeft);

internal static class TreemapTooltip
{
    public const double BeakTipOffset = 24;
    public const double BeakGap = 4;

    public static TooltipPlacement Place(Point cursorDip, Size popupDevice, Rect boundsDip, DpiScale dpi)
    {
        var popup = new Size(popupDevice.Width / dpi.DpiScaleX, popupDevice.Height / dpi.DpiScaleY);
        var side = Choose(cursorDip, popup, boundsDip);
        var topLeft = TopLeftFor(side, cursorDip, popup);

        return new(side, new(topLeft.X * dpi.DpiScaleX, topLeft.Y * dpi.DpiScaleY));
    }

    public static Point TopLeftFor(TooltipSide side, Point cursor, Size popup)
    {
        return side switch
        {
            TooltipSide.Above => new(cursor.X - BeakTipOffset, cursor.Y - BeakGap - popup.Height),
            TooltipSide.RightOf => new(cursor.X + BeakGap, cursor.Y - BeakTipOffset),
            TooltipSide.LeftOf => new(cursor.X - BeakGap - popup.Width, cursor.Y - BeakTipOffset),
            _ => new(cursor.X - BeakTipOffset, cursor.Y + BeakGap),
        };
    }

    private static TooltipSide Choose(Point cursor, Size popup, Rect bounds)
    {
        foreach (var candidate in (ReadOnlySpan<TooltipSide>)[TooltipSide.Below, TooltipSide.Above, TooltipSide.RightOf, TooltipSide.LeftOf])
        {
            var point = TopLeftFor(candidate, cursor, popup);

            if (point.X >= bounds.X
                && point.Y >= bounds.Y
                && point.X + popup.Width <= bounds.Right
                && point.Y + popup.Height <= bounds.Bottom)
            {
                return candidate;
            }
        }

        return TooltipSide.Below;
    }
}
