using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Etp.Reporting.Desktop;

internal static class ProtectedOperationPath
{
    private static readonly HashSet<string> TrustedOwners = new(StringComparer.Ordinal)
    {
        "S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"
    };

    // Raw access-mask bits, so generic rights (which FileSystemRights does not name) are seen too.
    // CreateItemsRights is FILE_ADD_FILE and FILE_ADD_SUBDIRECTORY.
    private const long WriteRights = 0x116, CreateItemsRights = 0x6, DeleteRight = 0x10000, DeleteChildRight = 0x40,
        ChangePermissionsRight = 0x40000, TakeOwnershipRight = 0x80000;

    internal enum Role { Target, Ancestor, Root }

    public static void Validate(string path) => Validate(path, null);

    /// <param name="targetModifier">
    /// 1.11.0 (HealthExports): SIDs that may hold Modify (write, delete) on the target itself, but never change its
    /// permissions or take ownership. Null: nobody but SYSTEM, Administrators and TrustedInstaller.
    /// </param>
    public static void Validate(string path, Func<string, bool>? targetModifier)
    {
        var full = Path.GetFullPath(path);
        var volumeProblem = VolumeProblem(full, DriveTypeOf(full), DosDeviceOf(full));
        if (volumeProblem is not null) throw new InvalidOperationException(volumeProblem);
        Walk(full, targetModifier, current =>
        {
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked operation paths cannot be used.");
            return (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(current).GetAccessControl() : new FileInfo(current).GetAccessControl();
        });
    }

    // Every level from the full path up to its root. readSecurity is replaceable so tests can
    // describe a whole drive layout without touching real permissions.
    internal static void Walk(string fullPath, Func<string, FileSystemSecurity> readSecurity) => Walk(fullPath, null, readSecurity);

    internal static void Walk(string fullPath, Func<string, bool>? targetModifier, Func<string, FileSystemSecurity> readSecurity)
    {
        var role = Role.Target;
        var holdsTarget = false;
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var acl = readSecurity(current);
            if (role == Role.Ancestor && string.IsNullOrEmpty(Path.GetDirectoryName(current))) role = Role.Root;
            Check(current, acl, role, holdsTarget, role == Role.Target ? targetModifier : null);
            // The folder directly above a file target is where its DLLs would be planted.
            holdsTarget = role == Role.Target && acl is not DirectorySecurity;
            role = Role.Ancestor;
        }
    }

    // The same rule as Get-EtpProtectedPathFindings in scripts\etp-operations-common.ps1,
    // which explains it in full. In short: the target must not be writable, deletable or
    // re-permissionable by anyone but SYSTEM, Administrators or TrustedInstaller; a folder
    // above it must not let anyone else rename it, rename what is in it or change its
    // permissions; the folder that holds a file target must not let anyone else create files
    // or subfolders in it either (a program loads DLLs from its own folder, 1.9.3 review F1);
    // other folders above may (a new name beside the path cannot replace any part of it); the
    // volume root is the same less Delete, because a root cannot be renamed. Inherit-only and
    // Deny entries, and application package and capability SIDs (S-1-15-2-*, S-1-15-3-*, which
    // only ever narrow an AppContainer's access), do not count. The owner must be trusted at
    // every level.
    internal static void Check(string path, FileSystemSecurity acl, Role role, bool holdsTarget = false, Func<string, bool>? targetModifier = null)
    {
        var owner = acl.GetOwner(typeof(SecurityIdentifier))?.Value ?? "";
        if (!TrustedOwners.Contains(owner))
            throw new InvalidOperationException($"Operation files must be owned by Administrators or SYSTEM. '{path}' is owned by {owner}.");
        var danger = DeleteChildRight | ChangePermissionsRight | TakeOwnershipRight;
        if (role != Role.Root) danger |= DeleteRight;
        if (role == Role.Target) danger |= WriteRights;
        else if (holdsTarget) danger |= CreateItemsRights;
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
            // A permitted modifier of the target (HealthExports: the automation account and the Owners) may write and
            // delete there; changing permissions or taking ownership stays refused.
            var ruleDanger = role == Role.Target && targetModifier is not null && targetModifier(sid)
                ? danger & (ChangePermissionsRight | TakeOwnershipRight)
                : danger;
            if ((rights & ruleDanger) != 0)
                throw new InvalidOperationException($"Operation files must be protected from non-administrator changes. '{path}' gives {sid} access rights 0x{rights & ruleDanger:X}.");
        }
    }

    /// <summary>
    /// HealthExports modifiers: setup grants Modify to the automation account and the active Owners, resolved to
    /// individual account SIDs (S-1-5-21-..., never a well-known group). Groups such as Users, Authenticated Users or
    /// Everyone are still refused.
    /// </summary>
    internal static bool IsIndividualAccount(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.Ordinal);

    // The same rule as Get-EtpOperationVolumeProblem in scripts\etp-operations-common.ps1: the
    // Root role relaxes Delete only because a real volume root cannot be renamed. A share, a
    // mapped network drive or a SUBST drive has a "root" that is an ordinary folder, with
    // unchecked folders above it (1.9.3 review F6). dosDevice is what QueryDosDevice says the
    // drive letter stands for: \Device\HarddiskVolume3 for a volume, \??\C:\Data for SUBST.
    internal static string? VolumeProblem(string fullPath, DriveType? driveType, string? dosDevice)
    {
        var refusal = $"Operation files must be on a local drive of this computer. '{fullPath}' is on a network share, a mapped drive or a SUBST drive.";
        if (!HasDriveLetter(fullPath)) return refusal;
        if (driveType is not (DriveType.Fixed or DriveType.Removable)) return refusal;
        if (string.IsNullOrEmpty(dosDevice) || !dosDevice.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return refusal;
        return null;
    }

    private static bool HasDriveLetter(string fullPath) =>
        fullPath.Length >= 3 && char.IsAsciiLetter(fullPath[0]) && fullPath[1] == ':' && fullPath[2] == '\\';

    internal static DriveType? DriveTypeOf(string fullPath)
    {
        if (!HasDriveLetter(fullPath)) return null;
        try { return new DriveInfo(fullPath[..2]).DriveType; }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
    }

    internal static string? DosDeviceOf(string fullPath)
    {
        if (!HasDriveLetter(fullPath)) return null;
        var target = new StringBuilder(1024);
        // The first of what may be several NUL-separated names is the current one.
        return QueryDosDevice(fullPath[..2].ToUpperInvariant(), target, target.Capacity) == 0 ? null : target.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int length);
}
