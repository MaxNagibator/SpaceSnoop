using SpaceSnoop.Core.Git;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncGitText
{
    internal const string NoCommits = "нет коммитов";

    internal static string FormatBranch(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        return git.IsDetached ? "detached" : git.Branch;
    }

    internal static string FormatHead(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        if (!git.HasCommits)
        {
            return NoCommits;
        }

        return string.IsNullOrEmpty(git.Subject) ? git.ShortHash : $"{git.ShortHash} · {git.Subject}";
    }

    internal static string FormatDirty(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        return git.IsDirty ? $"{git.DirtyCount} изм." : "чисто";
    }

    internal static string FormatUpstream(GitRepoState? git)
    {
        if (git is null || !git.HasUpstream)
        {
            return string.Empty;
        }

        var parts = new List<string>(2);

        if (git.Ahead > 0)
        {
            parts.Add($"↑{git.Ahead}");
        }

        if (git.Behind > 0)
        {
            parts.Add($"↓{git.Behind}");
        }

        return string.Join(" ", parts);
    }
}
