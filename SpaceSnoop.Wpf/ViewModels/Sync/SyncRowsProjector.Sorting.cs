using System.Buffers;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static partial class SyncRowsProjector
{
    private static readonly Comparison<DirectoryComparison> DirByNameAsc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    private static readonly Comparison<DirectoryComparison> DirByNameDesc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(right.Name, left.Name);
    private static readonly Comparison<DirectoryComparison> DirByStatusAsc = static (left, right) =>
    {
        var cmp = ((int)left.Status).CompareTo((int)right.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    };
    private static readonly Comparison<DirectoryComparison> DirByStatusDesc = static (left, right) =>
    {
        var cmp = ((int)right.Status).CompareTo((int)left.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    };

    private static readonly Comparison<FileComparison> FileByPathAsc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    private static readonly Comparison<FileComparison> FileByPathDesc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(right.RelativePath, left.RelativePath);
    private static readonly Comparison<FileComparison> FileBySizeAsc = static (left, right) => FileSize(left).CompareTo(FileSize(right));
    private static readonly Comparison<FileComparison> FileBySizeDesc = static (left, right) => FileSize(right).CompareTo(FileSize(left));
    private static readonly Comparison<FileComparison> FileByStatusAsc = static (left, right) =>
    {
        var cmp = ((int)left.Status).CompareTo((int)right.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByStatusDesc = static (left, right) =>
    {
        var cmp = ((int)right.Status).CompareTo((int)left.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByModifiedAsc = static (left, right) =>
    {
        var cmp = FileModified(left).CompareTo(FileModified(right));
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByModifiedDesc = static (left, right) =>
    {
        var cmp = FileModified(right).CompareTo(FileModified(left));
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };

    private static long FileSize(FileComparison file)
    {
        return Math.Max(file.LeftSize ?? 0, file.RightSize ?? 0);
    }

    private static DateTime FileModified(FileComparison file)
    {
        var left = file.LeftModified ?? DateTime.MinValue;
        var right = file.RightModified ?? DateTime.MinValue;

        return left > right ? left : right;
    }

    private static void SortDirectoriesInPlace(List<DirectoryComparison> dirs, SyncRowsRequest request)
    {
        var descending = request.RowSortDescending;

        switch (request.RowSort)
        {
            case SyncSortField.Size:
                var cache = request.DirSizeCache;
                StableSort(dirs, (left, right) =>
                {
                    var leftSizes = cache?.GetValueOrDefault(left);
                    var rightSizes = cache?.GetValueOrDefault(right);
                    var leftSize = leftSizes is null ? 0L : Math.Max(leftSizes.Value.Left, leftSizes.Value.Right);
                    var rightSize = rightSizes is null ? 0L : Math.Max(rightSizes.Value.Left, rightSizes.Value.Right);
                    var cmp = leftSize.CompareTo(rightSize);
                    return descending ? -cmp : cmp;
                });
                break;

            case SyncSortField.Status:
                StableSort(dirs, descending ? DirByStatusDesc : DirByStatusAsc);
                break;

            default:
                StableSort(dirs, descending ? DirByNameDesc : DirByNameAsc);
                break;
        }
    }

    private static void SortFilesInPlace(List<FileComparison> files, SyncSortField field, bool descending)
    {
        var comparison = field switch
        {
            SyncSortField.Size => descending ? FileBySizeDesc : FileBySizeAsc,
            SyncSortField.Status => descending ? FileByStatusDesc : FileByStatusAsc,
            SyncSortField.Modified => descending ? FileByModifiedDesc : FileByModifiedAsc,
            _ => descending ? FileByPathDesc : FileByPathAsc,
        };

        StableSort(files, comparison);
    }

    private static void StableSort<T>(List<T> items, Comparison<T> comparison)
    {
        var count = items.Count;

        if (count <= 1)
        {
            return;
        }

        if (count <= 16)
        {
            InsertionSort(items, 0, count, comparison);
            return;
        }

        var buffer = ArrayPool<T>.Shared.Rent(count);

        try
        {
            MergeSort(items, buffer, 0, count, comparison);
        }
        finally
        {
            Array.Clear(buffer, 0, count);
            ArrayPool<T>.Shared.Return(buffer);
        }
    }

    private static void MergeSort<T>(List<T> items, T[] buffer, int left, int right, Comparison<T> comparison)
    {
        var length = right - left;

        if (length <= 16)
        {
            InsertionSort(items, left, right, comparison);
            return;
        }

        var mid = left + (length >> 1);
        MergeSort(items, buffer, left, mid, comparison);
        MergeSort(items, buffer, mid, right, comparison);

        if (comparison(items[mid - 1], items[mid]) <= 0)
        {
            return;
        }

        var i = left;
        var j = mid;
        var k = left;

        while (i < mid && j < right)
        {
            if (comparison(items[i], items[j]) <= 0)
            {
                buffer[k++] = items[i++];
            }
            else
            {
                buffer[k++] = items[j++];
            }
        }

        while (i < mid)
        {
            buffer[k++] = items[i++];
        }

        while (j < right)
        {
            buffer[k++] = items[j++];
        }

        for (var index = left; index < right; index++)
        {
            items[index] = buffer[index];
        }
    }

    private static void InsertionSort<T>(List<T> items, int left, int right, Comparison<T> comparison)
    {
        for (var i = left + 1; i < right; i++)
        {
            var value = items[i];
            var j = i - 1;

            while (j >= left && comparison(items[j], value) > 0)
            {
                items[j + 1] = items[j];
                j--;
            }

            items[j + 1] = value;
        }
    }
}
