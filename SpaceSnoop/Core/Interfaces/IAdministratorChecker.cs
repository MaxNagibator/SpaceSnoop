namespace SpaceSnoop.Core.Interfaces;

public interface IAdministratorChecker
{
    bool IsCurrentUserAdmin();
    bool IsRestartRequired();
}
