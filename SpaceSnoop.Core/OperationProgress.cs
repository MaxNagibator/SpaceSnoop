namespace SpaceSnoop.Core;

public readonly record struct OperationProgress(int Completed, string Current, long Bytes = 0);
