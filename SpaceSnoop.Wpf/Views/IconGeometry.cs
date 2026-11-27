using MahApps.Metro.IconPacks;
using System.Collections.Concurrent;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SpaceSnoop.Wpf.Views;

public static class IconGeometry
{
    private static readonly ConcurrentDictionary<PackIconLucideKind, Geometry> Cache = new();
    private static readonly ScaleTransform FlipY = CreateFlipTransform();

    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind",
        typeof(PackIconLucideKind),
        typeof(IconGeometry),
        new(PackIconLucideKind.None, OnKindChanged));

    public static PackIconLucideKind GetKind(DependencyObject element)
    {
        return (PackIconLucideKind)element.GetValue(KindProperty);
    }

    public static void SetKind(DependencyObject element, PackIconLucideKind value)
    {
        element.SetValue(KindProperty, value);
    }

    public static Geometry Get(PackIconLucideKind kind)
    {
        return kind == PackIconLucideKind.None
            ? Geometry.Empty
            : Cache.GetOrAdd(kind, Build);
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Path path && e.NewValue is PackIconLucideKind kind)
        {
            path.Data = Get(kind);
        }
    }

    private static Geometry Build(PackIconLucideKind kind)
    {
        if (!PackIconDataFactory<PackIconLucideKind>.DataIndex.Value.TryGetValue(kind, out var data)
            || string.IsNullOrEmpty(data))
        {
            return Geometry.Empty;
        }

        var parsed = Geometry.Parse(data);
        var flipped = parsed.Clone();
        flipped.Transform = FlipY;
        flipped.Freeze();
        return flipped;
    }

    private static ScaleTransform CreateFlipTransform()
    {
        var transform = new ScaleTransform(1, -1);
        transform.Freeze();
        return transform;
    }
}

[MarkupExtensionReturnType(typeof(Geometry))]
public sealed class IconGeometryExtension : MarkupExtension
{
    public IconGeometryExtension()
    {
    }

    public IconGeometryExtension(PackIconLucideKind kind)
    {
        Kind = kind;
    }

    [ConstructorArgument("kind")]
    public PackIconLucideKind Kind { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return IconGeometry.Get(Kind);
    }
}
