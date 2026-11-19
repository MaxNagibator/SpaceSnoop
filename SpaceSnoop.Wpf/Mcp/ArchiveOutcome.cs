namespace SpaceSnoop.Wpf.Mcp;

public sealed record ArchiveOutcome(string? ArchivePath, bool OriginalDeleted, string StatusText);
