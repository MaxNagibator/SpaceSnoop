using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class DockerGroupViewModel : ObservableObject
{
    private const int PreviewCount = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleLabel))]
    [NotifyPropertyChangedFor(nameof(ToggleIconKind))]
    private bool _isExpanded;

    public DockerGroupViewModel(DockerObjectKind kind, IReadOnlyList<DockerObject> items)
    {
        Title = kind switch
        {
            DockerObjectKind.Image => "Образы",
            DockerObjectKind.Container => "Контейнеры",
            DockerObjectKind.Volume => "Тома",
            _ => "Объекты",
        };

        IconKind = DockerObjectViewModel.IconFor(kind);

        TotalBytes = items.Sum(i => i.SizeBytes);
        TotalSize = SizeFormatter.Format(TotalBytes);

        var ordered = items
            .OrderByDescending(i => i.SizeBytes)
            .Select(i => new DockerObjectViewModel(i))
            .ToList();

        Preview = ordered.Take(PreviewCount).ToList();
        Rest = ordered.Skip(PreviewCount).ToList();
    }

    public string Title { get; }

    public PackIconLucideKind IconKind { get; }

    public int Count => Preview.Count + Rest.Count;

    public long TotalBytes { get; }

    public string TotalSize { get; }

    public IReadOnlyList<DockerObjectViewModel> Preview { get; }

    public IReadOnlyList<DockerObjectViewModel> Rest { get; }

    public bool HasRest => Rest.Count > 0;

    public string ToggleLabel => IsExpanded ? "Свернуть" : $"Показать ещё {Rest.Count}";

    public PackIconLucideKind ToggleIconKind => IsExpanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
