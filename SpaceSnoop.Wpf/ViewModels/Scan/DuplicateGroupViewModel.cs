using SpaceSnoop.Core.Duplicates;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed class DuplicateGroupViewModel
{
    internal DuplicateGroupViewModel(DuplicateGroup group, Func<IReadOnlyList<SpaceBase>, bool, int> mark)
    {
        Members = [.. group.Members.Select(x => new DuplicateMemberViewModel(x, mark))];
        SizeText = SizeFormatter.Format(group.Size);
        ReclaimText = SizeFormatter.Format(group.ReclaimableBytes);
        CountText = Plural.Format(group.Members.Count, "файл", "файла", "файлов");
        OmittedText = group.OmittedMembers > 0 ? $"ещё {group.OmittedMembers:N0} не показано" : string.Empty;
    }

    public IReadOnlyList<DuplicateMemberViewModel> Members { get; }

    public string SizeText { get; }

    public string ReclaimText { get; }

    public string CountText { get; }

    public string OmittedText { get; }

    public bool HasOmitted => OmittedText.Length > 0;
}
