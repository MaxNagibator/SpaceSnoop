using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static partial class ScanTreeEditor
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    internal static bool HasMarkedSelfOrChild(SpaceBase space)
    {
        return space is DirectorySpace dir ? dir.HasMarkedSelfOrChild() : space.IsDeleted;
    }

    internal static List<SpaceBase> CollectMarked(IEnumerable<ScanNodeViewModel> roots, ScanNodeViewModel? unmarkedRoot = null)
    {
        var list = new List<SpaceBase>();

        foreach (var root in roots)
        {
            if (ReferenceEquals(root, unmarkedRoot) || root.Space is not DirectorySpace dir)
            {
                continue;
            }

            if (dir.IsDeleted)
            {
                list.Add(dir);
            }
            else
            {
                CollectMarked(dir, list);
            }
        }

        return list;
    }

    internal static void CollectMarked(DirectorySpace dir, List<SpaceBase> list)
    {
        dir.CollectMarked(list);
    }

    internal static List<SpaceBase> CollectMarked(SpaceBase space)
    {
        var marked = new List<SpaceBase>();

        if (space.IsDeleted)
        {
            marked.Add(space);
        }
        else if (space is DirectorySpace directory)
        {
            CollectMarked(directory, marked);
        }

        return marked;
    }

    internal static ScanMarkSnapshot CaptureMarks(SpaceBase root, SpaceBase space, bool marksPresent)
    {
        var scope = FindMarkScope(root, space);
        var marked = marksPresent || scope.IsDeleted ? CollectMarked(scope) : [];

        return new(root, scope, marked.Count, marked.Sum(static item => item.TotalSize));
    }

    internal static SpaceBase FindMarkScope(SpaceBase root, SpaceBase space)
    {
        var scope = space;

        for (var current = space; current is not null; current = current.Parent)
        {
            if (current.IsDeleted)
            {
                scope = current;
            }

            if (ReferenceEquals(current, root))
            {
                break;
            }
        }

        return scope;
    }

    internal static void RefreshNodeAfterDeletion(ScanNodeViewModel node, HashSet<SpaceBase> deletedSet)
    {
        if (node.Space is null)
        {
            return;
        }

        if (!node.IsLoaded)
        {
            node.SyncUnloadedChildren();
            node.NotifyPropertiesChanged();
            return;
        }

        var hasDeletedChild = node.RealizedChildren.Any(c => c.Space is not null && deletedSet.Contains(c.Space));

        if (hasDeletedChild)
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return;
        }

        foreach (var child in node.RealizedChildren)
        {
            RefreshNodeAfterDeletion(child, deletedSet);
        }

        node.NotifyPropertiesChanged();
    }

    internal static void RefreshNodeMarks(ScanNodeViewModel node, SpaceBase target)
    {
        if (node.Space is null)
        {
            return;
        }

        if (ReferenceEquals(node.Space, target))
        {
            node.RefreshMarks();
            return;
        }

        if (IsAncestorOf(node.Space, target))
        {
            node.RefreshOwnMarks();

            foreach (var child in node.RealizedChildren)
            {
                RefreshNodeMarks(child, target);
            }
        }
    }

    internal static List<ScanNodeViewModel> FilterTopLevelNodes(IEnumerable<ScanNodeViewModel> nodes)
    {
        var materialized = nodes.Where(static n => n.Space is not null).ToList();

        if (materialized.Count <= 1)
        {
            return materialized;
        }

        var selectedSpaces = materialized.Select(static n => n.Space!).ToHashSet<SpaceBase>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        return materialized
            .Where(node => !HasAncestorInSet(node.Space!, selectedSpaces) && seen.Add(node.Space!))
            .ToList();
    }

    internal static List<(SpaceBase Root, SpaceBase Scope)> FilterTopLevelScopes(IEnumerable<(SpaceBase Root, SpaceBase Scope)> candidates)
    {
        var list = candidates.ToList();

        if (list.Count <= 1)
        {
            return list;
        }

        var scopeSet = list.Select(static item => item.Scope).ToHashSet<SpaceBase>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        return list
            .Where(item => !HasAncestorInSet(item.Scope, scopeSet) && seen.Add(item.Scope))
            .ToList();
    }

    internal static bool IsSelfOrDescendantOfAny(SpaceBase space, IReadOnlySet<SpaceBase> targets)
    {
        return targets.Contains(space) || HasAncestorInSet(space, targets);
    }

    internal static bool RefreshNodeAfterAddition(ScanNodeViewModel node, DirectorySpace parent)
    {
        if (node.Space is null)
        {
            return false;
        }

        if (ReferenceEquals(node.Space, parent))
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return true;
        }

        if (IsAncestorOf(node.Space, parent))
        {
            if (node.IsLoaded)
            {
                _ = node.RealizedChildren.Any(child => RefreshNodeAfterAddition(child, parent));
            }
            else
            {
                node.SyncUnloadedChildren();
            }

            node.NotifyPropertiesChanged();
            return true;
        }

        return false;
    }

    internal static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static bool AddArchiveToTree(
        ObservableCollection<ScanNodeViewModel> roots,
        DirectorySpace source,
        ArchiveFileMetadata archive)
    {
        if (source.Parent is not DirectorySpace parent)
        {
            return false;
        }

        parent.AddFile(archive.Name, archive.Length, archive.CreationTime, archive.LastAccessTime);

        foreach (var root in roots)
        {
            RefreshNodeAfterAddition(root, parent);
        }

        return true;
    }

    internal static List<ScanNodeViewModel> RemoveRoot(ObservableCollection<ScanNodeViewModel> roots, string path)
    {
        var normalized = NormalizePath(path);
        var removed = new List<ScanNodeViewModel>();

        for (var i = roots.Count - 1; i >= 0; i--)
        {
            if (IsRootOf(roots[i], normalized))
            {
                removed.Add(roots[i]);
                roots.RemoveAt(i);
            }
        }

        return removed;
    }

    internal static bool SamePath(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = Path.GetPathRoot(right)?.Length ?? 0;

        while (start < right.Length)
        {
            var end = right.IndexOfAny(Separators, start);

            if (end < 0)
            {
                end = right.Length;
            }

            if (!left.AsSpan(start, end - start).SequenceEqual(right.AsSpan(start, end - start))
                && PathCase.IsCaseSensitive(right[..start]))
            {
                return false;
            }

            start = end + 1;
        }

        return true;
    }

    internal static List<ScanNodeViewModel> ReleaseUnmarkedRoot(
        ObservableCollection<ScanNodeViewModel> roots,
        string path,
        bool marksPresent,
        out bool keptMarked)
    {
        var normalized = NormalizePath(path);
        var released = new List<ScanNodeViewModel>();
        keptMarked = false;

        for (var i = roots.Count - 1; i >= 0; i--)
        {
            var root = roots[i];

            if (!IsRootOf(root, normalized))
            {
                continue;
            }

            if (KeepsMarks(root, marksPresent))
            {
                keptMarked = true;
                continue;
            }

            roots.RemoveAt(i);
            released.Add(root);
        }

        return released;
    }

    internal static bool HasReleasableRoot(IEnumerable<ScanNodeViewModel> roots, string path, bool marksPresent)
    {
        var normalized = NormalizePath(path);

        return roots.Any(root => IsRootOf(root, normalized) && !KeepsMarks(root, marksPresent));
    }

    private static bool HasAncestorInSet(SpaceBase space, IReadOnlySet<SpaceBase> set)
    {
        for (var parent = space.Parent; parent is not null; parent = parent.Parent)
        {
            if (set.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAncestorOf(SpaceBase candidate, SpaceBase target)
    {
        for (var current = target.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool KeepsMarks(ScanNodeViewModel root, bool marksPresent)
    {
        return marksPresent && root.Space is { } space && HasMarkedSelfOrChild(space);
    }

    private static bool IsRootOf(ScanNodeViewModel root, string normalizedPath)
    {
        return SamePath(NormalizePath(root.AbsolutePath), normalizedPath);
    }
}
