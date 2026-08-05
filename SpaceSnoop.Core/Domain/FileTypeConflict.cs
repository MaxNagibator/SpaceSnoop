namespace SpaceSnoop.Core.Domain;

public enum FileTypeConflict
{
    None = 0,
    LeftFileRightDirectory = 1,
    RightFileLeftDirectory = 2,
}
