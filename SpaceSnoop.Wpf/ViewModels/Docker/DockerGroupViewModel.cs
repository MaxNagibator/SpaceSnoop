using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Docker;

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

        Preview = new(ordered.Take(PreviewCount));
        Rest = new(ordered.Skip(PreviewCount));
    }

    public string Title { get; }

    public PackIconLucideKind IconKind { get; }

    public int Count => Preview.Count + Rest.Count;

    public long TotalBytes { get; private set; }

    public string TotalSize { get; private set; }

    public ObservableCollection<DockerObjectViewModel> Preview { get; }

    public ObservableCollection<DockerObjectViewModel> Rest { get; }

    public bool HasRest => Rest.Count > 0;

    public string ToggleLabel => IsExpanded ? "Свернуть" : $"Показать ещё {Rest.Count}";

    public PackIconLucideKind ToggleIconKind => IsExpanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;

    public bool Remove(DockerObjectViewModel row)
    {
        if (!Preview.Remove(row) && !Rest.Remove(row))
        {
            return false;
        }

        if (Preview.Count < PreviewCount && Rest.Count > 0)
        {
            Preview.Add(Rest[0]);
            Rest.RemoveAt(0);
        }

        TotalBytes -= row.Model.SizeBytes;
        TotalSize = SizeFormatter.Format(TotalBytes);

        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasRest));
        OnPropertyChanged(nameof(ToggleLabel));

        return true;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
