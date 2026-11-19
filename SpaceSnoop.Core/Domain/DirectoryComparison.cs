namespace SpaceSnoop.Core.Domain;

public class DirectoryComparison(string name, string relativePath)
{
    public string Name { get; } = name;
    public string RelativePath { get; } = relativePath;
    public ComparisonStatus Status { get; set; }
    public SyncAction Action { get; set; }
    public DateTime? LeftModified { get; set; }
    public DateTime? RightModified { get; set; }
    public bool LeftIncomplete { get; set; }
    public bool RightIncomplete { get; set; }
    public bool IsIncomplete => LeftIncomplete || RightIncomplete;
    public bool DeleteLeftBlocked { get; set; }
    public bool DeleteRightBlocked { get; set; }
    public List<string> SkippedLinks { get; } = [];
    public List<FileComparison> Files { get; } = [];
    public List<DirectoryComparison> SubDirectories { get; } = [];
}
