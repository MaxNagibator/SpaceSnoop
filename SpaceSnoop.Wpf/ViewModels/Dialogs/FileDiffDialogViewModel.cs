using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.Diff;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class FileDiffDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly ISettingsStore _settings;
    private readonly IReadOnlyList<DiffLine> _lines;
    private readonly FileComparison _file;
    private readonly string? _unavailable;
    private readonly HashSet<int> _expanded = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSideBySideHeaders))]
    [NotifyPropertyChangedFor(nameof(UnifiedHint))]
    private bool _unified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollapseAllText))]
    [NotifyPropertyChangedFor(nameof(CollapseAllIconKind))]
    [NotifyPropertyChangedFor(nameof(CollapseAllHint))]
    private bool _collapseUnchanged;

    public FileDiffDialogViewModel(ISettingsStore settings, FileComparison file, string leftPath, string rightPath, IReadOnlyList<DiffLine> lines, int added, int removed, string? unavailable)
    {
        _settings = settings;
        _file = file;
        _unavailable = unavailable;
        Name = file.Name;
        LeftPath = leftPath;
        RightPath = rightPath;
        _lines = lines;
        Added = added;
        Removed = removed;
        Summary = BuildSummary(file, unavailable, Added > 0 || Removed > 0);

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

    public bool LinesUnavailable => _unavailable is not null;

    public bool HasLineView => !LinesUnavailable;

    public bool ShowSideBySideHeaders => !Unified && !LinesUnavailable;

    public bool IsIdentical => !HasChanges && !LinesUnavailable && _file.Status == ComparisonStatus.Identical;

    public string Summary { get; }

    public bool HasSummary => Summary.Length > 0;

    public ObservableCollection<object> Items { get; } = [];

    public string CollapseAllText => CollapseUnchanged ? "Развернуть всё" : "Свернуть всё";

    public string CollapseAllHint => CollapseUnchanged
        ? "Показать все скрытые неизменные строки целиком"
        : "Свернуть неизменные строки, оставив контекст вокруг изменений";

    public string UnifiedHint => Unified
        ? "Вернуться к двум колонкам – слева и справа"
        : "Объединить в один столбец с пометками «+/−» (unified diff)";

    public PackIconLucideKind CollapseAllIconKind => CollapseUnchanged ? PackIconLucideKind.UnfoldVertical : PackIconLucideKind.FoldVertical;

    public static string DescribeHiddenDifference(FileComparison file)
    {
        var sizeDelta = Math.Abs((file.LeftSize ?? 0) - (file.RightSize ?? 0));
        var timeDelta = file is { LeftModified: { } left, RightModified: { } right } ? (left - right).Duration() : TimeSpan.Zero;
        var timeDiffers = timeDelta > DirectoryComparer.FatTimestampTolerance;

        if (sizeDelta > 0 && timeDiffers)
        {
            return $"Размер отличается на {SizeFormatter.Format(sizeDelta)}, время – на {SyncNodeText.FormatDelta(timeDelta)}.";
        }

        if (sizeDelta > 0)
        {
            return $"Размер отличается на {SizeFormatter.Format(sizeDelta)} при совпадающих строках – вероятно, разные переводы строк (CRLF/LF), BOM или кодировка.";
        }

        if (timeDiffers)
        {
            return $"Размер совпадает, отличается только время изменения (Δ {SyncNodeText.FormatDelta(timeDelta)}) – копирование меняет метку, на содержимое не влияет.";
        }

        return "Размер и строки совпадают – различие в служебных метаданных файла.";
    }

    public static string DescribeOneSided(FileComparison file)
    {
        return file.Status switch
        {
            ComparisonStatus.LeftOnly => $"Файл есть только слева, справа его нет – показано всё содержимое левой стороны ({SizeFormatter.Format(file.LeftSize ?? 0)}).",
            ComparisonStatus.RightOnly => $"Файл есть только справа, слева его нет – показано всё содержимое правой стороны ({SizeFormatter.Format(file.RightSize ?? 0)}).",
            _ => string.Empty,
        };
    }

    private static string BuildSummary(FileComparison file, string? unavailable, bool hasChanges)
    {
        var oneSided = DescribeOneSided(file);

        if (unavailable is not null)
        {
            var reason = oneSided.Length > 0 ? oneSided : DescribeHiddenDifference(file);
            return reason.Length == 0 ? unavailable : $"{unavailable} {reason}";
        }

        if (oneSided.Length > 0)
        {
            return oneSided;
        }

        if (!hasChanges && file.Status != ComparisonStatus.Identical)
        {
            return DescribeHiddenDifference(file);
        }

        return string.Empty;
    }

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
