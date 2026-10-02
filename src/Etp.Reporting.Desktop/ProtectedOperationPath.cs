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

    // Raw access-mask bits, so generic rights (which FileSystemRights does not name) are seen too.
    private const long WriteRights = 0x116, DeleteRight = 0x10000, DeleteChildRight = 0x40,
        ChangePermissionsRight = 0x40000, TakeOwnershipRight = 0x80000;

    internal enum Role { Target, Ancestor, Root }

    public static void Validate(string path)
    {
        var role = Role.Target;
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked operation paths cannot be used.");
            FileSystemSecurity acl = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(current).GetAccessControl() : new FileInfo(current).GetAccessControl();
            if (role == Role.Ancestor && string.IsNullOrEmpty(Path.GetDirectoryName(current))) role = Role.Root;
            Check(current, acl, role);
            role = Role.Ancestor;
        }
    }

    // The same rule as Get-EtpProtectedPathFindings in scripts\etp-operations-common.ps1,
    // which explains it in full. In short: the target must not be writable, deletable or
    // re-permissionable by anyone but SYSTEM, Administrators or TrustedInstaller; a folder
    // above it must not let anyone else rename it, rename what is in it or change its
    // permissions (creating new items beside the path is harmless); the volume root is the
    // same less Delete, because a root cannot be renamed. Inherit-only and Deny entries, and
    // application package and capability SIDs (S-1-15-2-*, S-1-15-3-*, which only ever narrow
    // an AppContainer's access), do not count. The owner must be trusted at every level.
    internal static void Check(string path, FileSystemSecurity acl, Role role)
    {
        var owner = acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "";
        if (!TrustedOwners.Contains(owner))
            throw new InvalidOperationException($"Operation files must be owned by Administrators or SYSTEM. '{path}' is owned by {owner}.");
        var danger = DeleteChildRight | ChangePermissionsRight | TakeOwnershipRight;
        if (role != Role.Root) danger |= DeleteRight;
        if (role == Role.Target) danger |= WriteRights;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = rule.IdentityReference.Value;
            if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0 ||
                TrustedOwners.Contains(sid) || sid.StartsWith("S-1-15-2-", StringComparison.Ordinal) || sid.StartsWith("S-1-15-3-", StringComparison.Ordinal))
                continue;
            long rights = unchecked((uint)(int)rule.FileSystemRights);
            // GENERIC_ALL and MAXIMUM_ALLOWED as everything, GENERIC_WRITE as FILE_GENERIC_WRITE.
            if ((rights & 0x12000000) != 0) rights |= 0x1F01FF;
            if ((rights & 0x40000000) != 0) rights |= WriteRights;
            if ((rights & danger) != 0)
                throw new InvalidOperationException($"Operation files must be protected from non-administrator changes. '{path}' gives {sid} access rights 0x{rights & danger:X}.");
        }
    }
}
