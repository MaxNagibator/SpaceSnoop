using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed partial class DockerObjectViewModel(DockerObject model) : ObservableObject
{
    [ObservableProperty]
    private bool _confirmingDelete;

    public DockerObject Model => model;

    public string Name => model.Name;

    public string Size => SizeFormatter.Format(model.SizeBytes);

    public string Detail => model.Detail;

    public bool InUse => model.InUse;

    public PackIconLucideKind IconKind => IconFor(model.Kind);

    public static PackIconLucideKind IconFor(DockerObjectKind kind)
    {
        return kind switch
        {
            DockerObjectKind.Image => PackIconLucideKind.Layers,
            DockerObjectKind.Container => PackIconLucideKind.Box,
            DockerObjectKind.Volume => PackIconLucideKind.Database,
            _ => PackIconLucideKind.File,
        };
    }

    [RelayCommand]
    private void ArmDelete()
    {
        ConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ConfirmingDelete = false;
    }
}
