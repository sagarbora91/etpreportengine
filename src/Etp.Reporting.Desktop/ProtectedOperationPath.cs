using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Etp.Reporting.Desktop;

internal static class ProtectedOperationPath
{
    private static readonly HashSet<string> TrustedOwners = new(StringComparer.Ordinal)
    {
        "S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"
    };

    public static void Validate(string path)
    {
        var danger = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked operation paths cannot be used.");
            FileSystemSecurity acl = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current).GetAccessControl() : new FileInfo(current).GetAccessControl();
            if (!TrustedOwners.Contains(acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "")) throw new InvalidOperationException("Operation files must be owned by Administrators or SYSTEM.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 && rule.AccessControlType == AccessControlType.Allow &&
                    (rule.FileSystemRights & danger) != 0 && !TrustedOwners.Contains(rule.IdentityReference.Value))
                    throw new InvalidOperationException("Operation files must be protected from non-administrator changes.");
            danger = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        }
    }
}
