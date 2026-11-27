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
        var subs = dir.SubDirectoriesArray;
        var files = dir.FileEntries;
        var totalRaw = subs.Length + files.Length;
        var queue = new PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)>(
            Math.Min(totalRaw, limit),
            ScanTopSelector.MinTopWeightComparer.Instance);
        var visibleCount = 0;
        var index = 0;

        for (var i = 0; i < subs.Length; i++)
        {
            ConsiderTopChild(subs[i], node, limit, queue, ref visibleCount, ref index);
        }

        if (files.Length > 0)
        {
            if (node.HasIgnoredItems)
            {
                if (node.IsVisible(dir))
                {
                    if (node.HasIgnoredFileIn(dir))
                    {
                        for (var i = 0; i < files.Length; i++)
                        {
                            ConsiderTopChild(dir.GetFile(i), node, limit, queue, ref visibleCount, ref index);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < files.Length; i++)
                        {
                            ConsiderTopFile(i, files[i].Size, limit, queue, ref visibleCount, ref index);
                        }
                    }
                }
            }
            else
            {
                for (var i = 0; i < files.Length; i++)
                {
                    ConsiderTopFile(i, files[i].Size, limit, queue, ref visibleCount, ref index);
                }
            }
        }

        var topSpaces = new (SpaceBase? Space, int FileIndex, long Size)[queue.Count];

        for (var i = topSpaces.Length - 1; i >= 0; i--)
        {
            topSpaces[i] = queue.Dequeue();
        }

        var max = topSpaces.Length > 0 ? topSpaces[0].Size : 0;
        var top = new ScanInspectorChild[topSpaces.Length];

        for (var i = 0; i < topSpaces.Length; i++)
        {
            var (child, fileIndex, size) = topSpaces[i];
            var name = child is not null ? child.Name : dir.GetFileName(fileIndex);
            top[i] = new(
                name,
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
        PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)> queue,
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
            queue.Enqueue((candidate, -1, size), (size, currentIndex));
        }
        else if (queue.TryPeek(out _, out var minKey) && size > minKey.Weight)
        {
            queue.DequeueEnqueue((candidate, -1, size), (size, currentIndex));
        }
    }

    private static void ConsiderTopFile(
        int fileIndex,
        long size,
        int limit,
        PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)> queue,
        ref int visibleCount,
        ref int index)
    {
        var currentIndex = index++;
        visibleCount++;

        if (limit <= 0)
        {
            return;
        }

        if (queue.Count < limit)
        {
            queue.Enqueue((null, fileIndex, size), (size, currentIndex));
        }
        else if (queue.TryPeek(out _, out var minKey) && size > minKey.Weight)
        {
            queue.DequeueEnqueue((null, fileIndex, size), (size, currentIndex));
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
