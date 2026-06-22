namespace SpaceSnoop.Core.Domain;

public enum ComparisonStatus
{
    Identical = 0,
    LeftOnly = 1,
    RightOnly = 2,
    Modified = 3,
    Conflict = 4,
}
