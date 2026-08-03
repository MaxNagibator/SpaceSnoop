namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceTraversal(long Directories, long FailedDirectories, int Parallelism);
