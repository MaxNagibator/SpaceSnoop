namespace SpaceSnoop.Core.Domain;

public class FileComparison(string name, string relativePath)
{
    public string Name { get; } = name;
    public string RelativePath { get; } = relativePath;
    public ComparisonStatus Status { get; set; }
    public SyncAction Action { get; set; }
    public FileTypeConflict TypeConflict { get; set; }
    public bool DeleteLeftBlocked { get; set; }
    public bool DeleteRightBlocked { get; set; }

    public long? LeftSize { get; set; }
    public long? RightSize { get; set; }
    public DateTime? LeftModified { get; set; }
    public DateTime? RightModified { get; set; }
    public string? LeftHash { get; set; }
    public string? RightHash { get; set; }
}
