namespace SpaceSnoop.Core.Domain;

public enum SyncAction
{
    None,
    CopyToRight,
    CopyToLeft,
    DeleteLeft,
    DeleteRight,
    Skip,
}
