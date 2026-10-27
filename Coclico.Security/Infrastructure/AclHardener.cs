using System.Security.AccessControl;
using System.Security.Principal;

namespace Coclico.Security.Infrastructure;

public static class AclHardener
{
    public static bool TryHardenInstallDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            var dirInfo = new DirectoryInfo(directory);
            DirectorySecurity security = dirInfo.GetAccessControl();
            security.SetAccessRuleProtection(true, false);

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var currentUser = WindowsIdentity.GetCurrent().User;

            security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            if (currentUser != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            }

            dirInfo.SetAccessControl(security);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Principal.IdentityNotMappedException)
        {
            return false;
        }
    }
}
