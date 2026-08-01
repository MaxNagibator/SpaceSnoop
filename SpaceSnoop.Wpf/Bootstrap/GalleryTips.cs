using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Views.Scan;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class GalleryTips
{
    public const string TreemapCenter = "treemap-center";
    public const string TreemapCorner = "treemap-corner";
    public const string HostName = "Treemap";

    private const double EdgeInset = 8;

    public static IReadOnlyList<string> All { get; } = [TreemapCenter, TreemapCorner];

    public static string Section(string key)
    {
        return key switch
        {
            TreemapCenter or TreemapCorner => SectionKey.Scan,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестная подсказка галереи."),
        };
    }

    public static void Prepare(string key, IServiceProvider services)
    {
        switch (key)
        {
            case TreemapCenter:
            case TreemapCorner:
                services.GetRequiredService<ScanViewModel>().ShowTreemap = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестная подсказка галереи.");
        }
    }

    public static Point Point(string key, FrameworkElement host)
    {
        return key switch
        {
            TreemapCenter => new(host.ActualWidth / 2, host.ActualHeight / 2),
            TreemapCorner => new(Math.Max(0, host.ActualWidth - EdgeInset), Math.Max(0, host.ActualHeight - EdgeInset)),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Неизвестная подсказка галереи."),
        };
    }

    public static TreemapView Host(FrameworkElement root)
    {
        return ViewCapture.Find(root, HostName) as TreemapView
            ?? throw new InvalidOperationException($"Карта диска «{HostName}» не найдена – подсказку негде показать.");
    }
}
