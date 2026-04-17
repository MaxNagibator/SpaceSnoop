using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ScanNodeViewModel : ObservableObject
{
    private static readonly ScanNodeViewModel Dummy = new();

    private readonly ScanSortState? _sort;
    private readonly ILogger _logger;
    private readonly ScanNodeFactory? _factory;
    private bool _loaded;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isMarkedDeleted;

    internal ScanNodeViewModel(SpaceBase space, double siblingMax, double parentTotal, ScanSortState sort, ILogger logger, ScanNodeFactory factory)
    {
        Space = space;
        Fraction = siblingMax;
        Share = parentTotal;
        _sort = sort;
        _logger = logger;
        _factory = factory;
        _isMarkedDeleted = space.IsDeleted;

        if (HasChildren)
        {
            Children.Add(Dummy);
        }
    }

    private ScanNodeViewModel()
    {
        _logger = NullLogger.Instance;
    }

    public RangeObservableCollection<ScanNodeViewModel> Children { get; } = [];

    public bool IsDirectory => Space is DirectorySpace;

    public bool HasChildren => Space is DirectorySpace dir && dir.SubDirectories.Count + dir.Files.Count > 0;

    public string Name => Space?.Name ?? string.Empty;

    public string SizeText => Space is DirectorySpace dir ? dir.TotalSizeText : Space?.SizeText ?? string.Empty;

    public double Fraction
    {
        get
        {
            if (Space is null)
            {
                return 0;
            }

            if (field > 0)
            {
                return Math.Clamp(Space.TotalSize / field, 0, 1);
            }

            return 1;
        }
    }

    public double Share => Space is null || field <= 0
        ? 0
        : Math.Clamp(Space.TotalSize / field, 0, 1);

    public string ShareText => $"{Share * 100:0}%";

    public string AbsolutePath => Space?.AbsolutePath ?? string.Empty;

    public string Tooltip => Space?.GetTooltipText() ?? string.Empty;

    public SpaceBase? Space { get; }

    public void EnsureLoaded()
    {
        if (_loaded || Space is not DirectorySpace || _sort is null)
        {
            return;
        }

        _loaded = true;
        ReloadChildren();
    }

    public void ReloadChildren()
    {
        if (Space is not DirectorySpace dir || _sort is null)
        {
            return;
        }

        _loaded = true;

        var items = dir.SubDirectories
            .Cast<SpaceBase>()
            .Concat(dir.Files)
            .ToList();

        var localMax = items.Count > 0 ? items.Max(static x => x.TotalSize) : 0;

        var ordered = items
            .OrderBy(static x => x, _sort)
            .Select(item => _factory!.Create(item, localMax, dir.TotalSize, _sort));

        Children.ReplaceAll(ordered);
    }

    public void NotifyPropertiesChanged()
    {
        OnPropertyChanged(string.Empty);
    }

    public void Resort()
    {
        if (!_loaded || _sort is null)
        {
            return;
        }

        var ordered = Children.OrderBy(static child => child.Space!, _sort).ToList();
        Children.ReplaceAll(ordered);

        foreach (var child in Children)
        {
            child.Resort();
        }
    }

    private static IEnumerable<SpaceBase> EnumerateChildren(DirectorySpace dir)
    {
        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files);
    }

    private static void RestoreRecursive(SpaceBase space)
    {
        if (space.IsDeleted)
        {
            space.Restore();
        }

        if (space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var sub in dir.SubDirectories)
        {
            RestoreRecursive(sub);
        }

        foreach (var file in dir.Files)
        {
            if (file.IsDeleted)
            {
                file.Restore();
            }
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            EnsureLoaded();
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (Space is null)
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", Space.AbsolutePath);
        }
        catch (Exception ex)
        {
            _logger.OpenExplorerFailed(ex, Space.AbsolutePath);
        }
    }

    [RelayCommand]
    private void MarkDeleted()
    {
        if (Space is null || Space.IsDeleted)
        {
            return;
        }

        Space.Delete();
        RefreshMarkRecursive();
    }

    [RelayCommand]
    private void UnmarkDeleted()
    {
        if (Space is null)
        {
            return;
        }

        RestoreRecursive(Space);
        RefreshMarkRecursive();
    }

    [RelayCommand]
    private void MarkContentsDeleted()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var child in EnumerateChildren(dir))
        {
            child.Delete();
        }

        RefreshMarkRecursive();
    }

    [RelayCommand]
    private void UnmarkContents()
    {
        if (Space is not DirectorySpace dir)
        {
            return;
        }

        foreach (var child in EnumerateChildren(dir))
        {
            RestoreRecursive(child);
        }

        RefreshMarkRecursive();
    }

    private void RefreshMarkRecursive()
    {
        if (Space is not null)
        {
            IsMarkedDeleted = Space.IsDeleted;
        }

        foreach (var child in Children)
        {
            if (!ReferenceEquals(child, Dummy))
            {
                child.RefreshMarkRecursive();
            }
        }
    }
}
