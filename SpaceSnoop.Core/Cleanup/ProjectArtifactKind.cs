namespace SpaceSnoop.Core.Cleanup;

public enum ProjectArtifactKind
{
    None = 0,
    Bin = 1,
    Obj = 2,
    TestResults = 3,
    Benchmarks = 4,
    Coverage = 5,
    CustomOutput = 6,
}
