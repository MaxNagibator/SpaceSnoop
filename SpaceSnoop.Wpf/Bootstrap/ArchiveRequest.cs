using System.IO.Compression;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed record ArchiveRequest(
    string SourcePath,
    string TargetPath,
    int EstimatedFiles,
    long TotalBytes,
    bool DeleteOriginal,
    CompressionLevel Level,
    bool Interactive);
