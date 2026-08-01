using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class ViewCapture
{
    public const string FolderName = "shots";
    public const string FilePrefix = "view-";
    public const string FileExtension = ".png";

    private const double BaseDpi = 96;

    public static string DirectoryPath { get; } = Path.Combine(AppStorage.DataDirectory, FolderName);

    public static string FileName(string label, DateTimeOffset takenAt)
    {
        return $"{FilePrefix}{takenAt:yyyyMMdd-HHmmss-fff}-{Slug(label)}{FileExtension}";
    }

    public static FrameworkElement? Find(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            if (Find(VisualTreeHelper.GetChild(root, index), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> Names(DependencyObject root, int limit)
    {
        var names = new List<string>();
        Collect(root, limit, names);

        return names;
    }

    public static (int Width, int Height) Save(FrameworkElement element, string path, double scale = 1)
    {
        return Save(element, null, default, path, scale);
    }

    public static (int Width, int Height) Save(
        FrameworkElement element,
        FrameworkElement? overlay,
        Point overlayOffset,
        string path,
        double scale = 1)
    {
        scale = Math.Clamp(scale, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax);
        var size = new Size(element.ActualWidth, element.ActualHeight);
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);

        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(element), null, new(size));

            if (overlay is { ActualWidth: > 0, ActualHeight: > 0 })
            {
                context.DrawRectangle(new VisualBrush(overlay),
                    null,
                    new(overlayOffset, new Size(overlay.ActualWidth, overlay.ActualHeight)));
            }
        }

        var dpi = BaseDpi * scale;
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? DirectoryPath);

        using var stream = File.Create(path);
        encoder.Save(stream);

        return (width, height);
    }

    public static Point OffsetBetween(Visual outer, Visual inner)
    {
        var dpi = VisualTreeHelper.GetDpi(outer);
        var outerOrigin = outer.PointToScreen(new(0, 0));
        var innerOrigin = inner.PointToScreen(new(0, 0));

        return new((innerOrigin.X - outerOrigin.X) / dpi.DpiScaleX, (innerOrigin.Y - outerOrigin.Y) / dpi.DpiScaleY);
    }

    public static void DropObsolete(string directory, int keep, Action<Exception, string> onFailure)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Obsolete(Directory.GetFiles(directory, $"{FilePrefix}*{FileExtension}"), keep))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                onFailure(exception, file);
            }
        }
    }

    internal static IReadOnlyList<string> Obsolete(IEnumerable<string> files, int keep)
    {
        return [.. files.OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Skip(Math.Max(keep, 0))];
    }

    internal static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);

        foreach (var symbol in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(symbol))
            {
                builder.Append(symbol);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "view";
    }

    private static void Collect(DependencyObject root, int limit, List<string> names)
    {
        if (names.Count >= limit)
        {
            return;
        }

        if (root is FrameworkElement { Name.Length: > 0 } element
            && !element.Name.StartsWith("PART_", StringComparison.Ordinal)
            && !names.Contains(element.Name, StringComparer.Ordinal))
        {
            names.Add(element.Name);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            Collect(VisualTreeHelper.GetChild(root, index), limit, names);
        }
    }
}
