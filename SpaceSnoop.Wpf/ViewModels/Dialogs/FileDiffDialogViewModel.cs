using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.Diff;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class FileDiffDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IReadOnlyList<DiffLine> _lines;
    private readonly HashSet<int> _expanded = [];

    [ObservableProperty]
    private bool _unified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseAllText))]
    [NotifyPropertyChangedFor(nameof(CollapseAllIconKind))]
    private bool _collapseUnchanged;

    public FileDiffDialogViewModel(ISettingsStore settings, string name, string leftPath, string rightPath, IReadOnlyList<DiffLine> lines, int added, int removed)
    {
        _settings = settings;
        Name = name;
        LeftPath = leftPath;
        RightPath = rightPath;
        _lines = lines;
        Added = added;
        Removed = removed;

        _unified = settings.GetBool(SettingsKeys.SyncDiffUnified);
        _collapseUnchanged = settings.GetBool(SettingsKeys.SyncDiffCollapse, AppDefaults.SyncDiffCollapseDefault);

        RebuildItems();
    }

    public event EventHandler<bool>? RequestClose;

    public string Title => "Сравнение содержимого";

    public string Name { get; }

    public string LeftPath { get; }

    public string RightPath { get; }

    public int Added { get; }

    public int Removed { get; }

    public bool HasChanges => Added > 0 || Removed > 0;

    public ObservableCollection<object> Items { get; } = [];

    public string CollapseAllText => CollapseUnchanged ? "Развернуть всё" : "Свернуть всё";

    public PackIconLucideKind CollapseAllIconKind => CollapseUnchanged ? PackIconLucideKind.UnfoldVertical : PackIconLucideKind.FoldVertical;

    [RelayCommand]
    private void ToggleCollapseAll()
    {
        _expanded.Clear();
        CollapseUnchanged = !CollapseUnchanged;
    }

    [RelayCommand]
    private void ExpandGap(DiffGap? gap)
    {
        if (gap is null || !_expanded.Add(gap.Start))
        {
            return;
        }

        RebuildItems();
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    partial void OnUnifiedChanged(bool value)
    {
        _settings.SetValue(SettingsKeys.SyncDiffUnified, value ? "true" : "false");
        RebuildItems();
    }

    partial void OnCollapseUnchangedChanged(bool value)
    {
        _settings.SetValue(SettingsKeys.SyncDiffCollapse, value ? "true" : "false");
        RebuildItems();
    }

    private void RebuildItems()
    {
        Items.Clear();

        var segments = CollapseUnchanged
            ? TextDiff.Collapse(_lines, AppDefaults.DiffContextLines, _expanded)
            : [new(false, 0, _lines.Count)];

        foreach (var segment in segments)
        {
            if (segment.IsGap)
            {
                Items.Add(new DiffGap(segment.Start, segment.Count));
            }
            else if (Unified)
            {
                for (var i = segment.Start; i < segment.Start + segment.Count; i++)
                {
                    Items.Add(_lines[i]);
                }
            }
            else
            {
                var slice = new DiffLine[segment.Count];

                for (var i = 0; i < segment.Count; i++)
                {
                    slice[i] = _lines[segment.Start + i];
                }

                foreach (var row in TextDiff.ToSideBySide(slice))
                {
                    Items.Add(row);
                }
            }
        }
    }
}
