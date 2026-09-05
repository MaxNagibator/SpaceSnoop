namespace SpaceSnoop.Core.Domain;

public enum FileTypeConflict
{
    None = 0,
    LeftFileRightDirectory = 1,
    RightFileLeftDirectory = 2,
    LeftLinkRightObject = 3,
    RightLinkLeftObject = 4,
    CaseCollision = 5,
}
