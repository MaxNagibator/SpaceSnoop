namespace SpaceSnoop.Core.Domain;

public class DirectoryComparison(string name, string relativePath)
{
    public string Name { get; } = name;
    public string RelativePath { get; } = relativePath;
    public ComparisonStatus Status { get; set; }
    public List<FileComparison> Files { get; } = [];
    public List<DirectoryComparison> SubDirectories { get; } = [];
}
