using System.Security.AccessControl;
using System.Security.Principal;

namespace SpaceSnoop.Tests;

public static class TestAcl
{
    private const int UnreadableAttempts = 20;
    private const int UnreadableWaitMs = 50;

    public static void DenyEnumeration(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        security.AddAccessRule(new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            AccessControlType.Deny));

        new DirectoryInfo(path).SetAccessControl(security);
        RequireUnreadable(path);
    }

    public static void AllowEnumeration(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        security.RemoveAccessRuleAll(new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            AccessControlType.Deny));

        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void RequireUnreadable(string path)
    {
        for (var attempt = 0; attempt < UnreadableAttempts; attempt++)
        {
            try
            {
                _ = Directory.EnumerateFileSystemEntries(path).Any();
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            Thread.Sleep(UnreadableWaitMs);
        }

        Assert.Ignore($"Каталог {path} остаётся читаемым: запрещающее правило доступа в этой среде не действует.");
    }
}
