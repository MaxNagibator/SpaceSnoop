using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed class DockerObjectViewModel(DockerObject model)
{
    public DockerObject Model => model;

    public string Name => model.Name;

    public string Size => model.Size;

    public string Detail => model.Detail;

    public bool InUse => model.InUse;

    public PackIconLucideKind IconKind => model.Kind switch
    {
        DockerObjectKind.Image => PackIconLucideKind.Layers,
        DockerObjectKind.Container => PackIconLucideKind.Box,
        DockerObjectKind.Volume => PackIconLucideKind.Database,
        _ => PackIconLucideKind.File,
    };
}
