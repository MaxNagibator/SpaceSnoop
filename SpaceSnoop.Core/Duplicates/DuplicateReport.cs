namespace SpaceSnoop.Core.Duplicates;

public readonly record struct DuplicateReport(
    IReadOnlyList<DuplicateGroup> Groups,
    long ReclaimableBytes,
    int Examined,
    int OmittedGroups,
    int UnreadableDirectories,
    IReadOnlyList<string> Errors);
