using System.Text;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanInspectorViewModel
{
    internal static long CalculateCombinedBytes(IReadOnlyList<ScanNodeViewModel> topNodes)
    {
        var allIgnored = topNodes.Count > 0 && topNodes.All(static n => n.IsIgnored);
        var total = 0L;

        foreach (var node in topNodes)
        {
            if (node.Space is null)
            {
                continue;
            }

            if (allIgnored)
            {
                total += node.Space.TotalSize;
            }
            else if (!node.IsIgnored)
            {
                total += node.VisibleSize(node.Space);
            }
        }

        return total;
    }

    internal void CopyPaths(IEnumerable<ScanNodeViewModel> topNodes)
    {
        TryCopy(string.Join(Environment.NewLine, topNodes.Select(static n => n.AbsolutePath)));
    }

    private static string ResolveMultiSelectionKind(int dirCount, int fileCount)
    {
        if (dirCount > 0 && fileCount > 0)
        {
            return "Папки и файлы";
        }

        return dirCount > 0 ? "Каталоги" : "Файлы";
    }

    private void TryCopy(string? value)
    {
        _clipboard.TrySetText(value ?? string.Empty);
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (!HasSelection)
        {
            return;
        }

        if (IsMultiSelection)
        {
            CopyPaths(_selectedTopNodes);
            return;
        }

        TryCopy(Path);
    }

    [RelayCommand]
    private void CopyDetails()
    {
        if (!HasSelection)
        {
            return;
        }

        TryCopy(BuildDetails());
    }

    [RelayCommand]
    private void SelectChild(ScanInspectorChild? child)
    {
        if (child is null || Node is not { IsDirectory: true } current)
        {
            return;
        }

        current.EnsureLoaded();
        current.IsExpanded = true;

        var target = current.Children.FirstOrDefault(
            candidate => candidate.IsDirectory == child.IsDirectory
                && string.Equals(candidate.Name, child.Name, StringComparison.Ordinal));

        if (target is not null)
        {
            ChildSelected?.Invoke(target);
        }
    }

    private void ApplyShares(ScanNodeViewModel node)
    {
        ShareOfParent = node.Share;
        ShareOfParentText = ShareFormatter.Format(node.Share);
        ShareOfRoot = node.ShareOfRoot;
        ShareOfRootText = ShareFormatter.Format(node.ShareOfRoot);

        ShowParentShare = !node.IsRoot;
        ShowRootShare = ShowParentShare && !string.Equals(ShareOfParentText, ShareOfRootText, StringComparison.Ordinal);

        ApplyDriveShare(node);

        HasShares = ShowParentShare || ShowRootShare || ShowDriveShare;
    }

    private void ApplyDriveShare(ScanNodeViewModel node)
    {
        ShowDriveShare = node.ShowsDriveShare;
        DriveShare = node.DriveShare;
        DriveShareText = ShowDriveShare ? ShareFormatter.Format(node.DriveShare) : string.Empty;
        DriveShareHint = node.DriveHint;
    }

    private void BuildTopChildren(DirectorySpace dir, ScanNodeViewModel node)
    {
        var limit = AppDefaults.TopChildrenLimit;
        var totalRaw = dir.SubDirectories.Count + dir.Files.Count;
        var queue = new PriorityQueue<(SpaceBase Space, long Size), (double Weight, int Index)>(
            Math.Min(totalRaw, limit),
            ScanTopSelector.MinTopWeightComparer.Instance);
        var visibleCount = 0;
        var index = 0;

        foreach (var sub in dir.SubDirectories)
        {
            ConsiderTopChild(sub, node, limit, queue, ref visibleCount, ref index);
        }

        foreach (var file in dir.Files)
        {
            ConsiderTopChild(file, node, limit, queue, ref visibleCount, ref index);
        }

        var topSpaces = new (SpaceBase Space, long Size)[queue.Count];

        for (var i = topSpaces.Length - 1; i >= 0; i--)
        {
            topSpaces[i] = queue.Dequeue();
        }

        var max = topSpaces.Length > 0 ? topSpaces[0].Size : 0;
        var top = new ScanInspectorChild[topSpaces.Length];

        for (var i = 0; i < topSpaces.Length; i++)
        {
            var (child, size) = topSpaces[i];
            top[i] = new(
                child.Name,
                SizeFormatter.Format(size),
                ShareFormatter.Format(node.Weight > 0 ? size / node.Weight : 0),
                max > 0 ? Math.Clamp((double)size / max, 0, 1) : 0,
                child is DirectorySpace);
        }

        TopChildren.ReplaceAll(top);

        MoreChildrenCount = Math.Max(0, visibleCount - limit);
        HasMoreChildren = MoreChildrenCount > 0;
        MoreChildrenText = HasMoreChildren ? $"…ещё {MoreChildrenCount}" : string.Empty;

        OnPropertyChanged(nameof(HasTopChildren));
    }

    private static void ConsiderTopChild(
        SpaceBase candidate,
        ScanNodeViewModel node,
        int limit,
        PriorityQueue<(SpaceBase Space, long Size), (double Weight, int Index)> queue,
        ref int visibleCount,
        ref int index)
    {
        var currentIndex = index++;

        if (!node.IsVisible(candidate))
        {
            return;
        }

        visibleCount++;

        if (limit <= 0)
        {
            return;
        }

        var size = node.VisibleSize(candidate);

        if (queue.Count < limit)
        {
            queue.Enqueue((candidate, size), (size, currentIndex));
        }
        else if (queue.TryPeek(out _, out var minKey) && size > minKey.Weight)
        {
            queue.DequeueEnqueue((candidate, size), (size, currentIndex));
        }
    }

    private void ClearTopChildren()
    {
        TopChildren.ReplaceAll([]);
        MoreChildrenCount = 0;
        HasMoreChildren = false;
        MoreChildrenText = string.Empty;
        OnPropertyChanged(nameof(HasTopChildren));
    }

    private string BuildDetails()
    {
        var builder = new StringBuilder();

        if (IsMultiSelection)
        {
            builder.AppendLine($"Выбрано: {Title}");
            builder.AppendLine($"Тип: {KindText}");
            builder.AppendLine($"Общий размер: {TotalSizeText}");
            builder.AppendLine($"Файлов: {FileCountText}");
            builder.AppendLine($"Каталогов: {DirCountText}");
            builder.AppendLine(string.Join(Environment.NewLine, _selectedTopNodes.Select(static n => n.AbsolutePath)));
            return builder.ToString();
        }

        builder.AppendLine($"Название: {Title}");
        builder.AppendLine($"Путь: {Path}");
        builder.AppendLine($"Тип: {KindText}");
        builder.AppendLine($"Общий размер: {TotalSizeText}");

        if (HasOwnSize)
        {
            builder.AppendLine($"Свои файлы: {OwnSizeText}");
        }

        if (ShowParentShare)
        {
            builder.AppendLine($"Доля родителя: {ShareOfParentText}");
        }

        if (ShowRootShare)
        {
            builder.AppendLine($"Доля корня: {ShareOfRootText}");
        }

        if (ShowDriveShare)
        {
            builder.AppendLine($"Доля диска: {DriveShareText} ({DriveShareHint})");
        }

        if (IsDirectory)
        {
            builder.AppendLine($"Файлов: {FileCountText}");
            builder.AppendLine($"Подкаталогов: {DirCountText}");
        }

        builder.AppendLine($"Создан: {CreationDateText}");
        builder.AppendLine($"Последний доступ: {LastAccessText}");

        return builder.ToString();
    }
}

public sealed record ScanInspectorChild(string Name, string SizeText, string ShareText, double Fraction, bool IsDirectory);
