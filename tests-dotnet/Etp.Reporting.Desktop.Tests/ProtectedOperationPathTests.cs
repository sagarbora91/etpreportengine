using System.IO;
using System.Security.AccessControl;
using Etp.Reporting.Desktop.Modules.HealthCheck;

namespace Etp.Reporting.Desktop.Tests;

// The application's copy of the protected-install rule, fed synthetic security descriptors so
// no real drive root or permission is touched. The E: descriptors are Workpc's on 2 Oct 2026
// (icacls /save of E:\ and E:\Program Files before they were changed by hand). The script copy
// is covered by the ProtectedInstallLayouts scenario of test-etp-operations-boundaries.ps1.
public sealed class ProtectedOperationPathTests
{
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string InstallingUser = "S-1-5-21-1000000001-2000000002-3000000003-1001";
    private const string FormattedRoot = "O:SYG:SYD:(A;;FA;;;BA)(A;OICIIO;GA;;;BA)(A;;FA;;;SY)(A;OICIIO;GA;;;SY)(A;;0x1301bf;;;AU)(A;OICIIO;SDGXGWGR;;;AU)(A;;0x1200a9;;;BU)(A;OICIIO;GXGR;;;BU)";
    private const string ProgramFilesDacl = "D:PAI(A;OICI;FA;;;S-1-15-2-1)(A;OICI;0x1200a9;;;S-1-15-2-2)(A;OICIIO;FA;;;CO)(A;OICIIO;FA;;;SY)(A;;0x1301bf;;;SY)(A;OICIIO;FA;;;BA)(A;;0x1301bf;;;BA)(A;OICI;0x1200a9;;;BU)(A;CI;FA;;;" + TrustedInstaller + ")";
    private const string UnderFormattedRoot = "O:BAG:SYD:AI(A;ID;FA;;;BA)(A;OICIIOID;GA;;;BA)(A;ID;FA;;;SY)(A;OICIIOID;GA;;;SY)(A;ID;0x1301bf;;;AU)(A;OICIIOID;SDGXGWGR;;;AU)(A;ID;0x1200a9;;;BU)(A;OICIIOID;GXGR;;;BU)";
    private const string Plain = "O:BAG:SYD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)";

    private static DirectorySecurity Folder(string sddl)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        return security;
    }

    private static Exception? Problem(string sddl, ProtectedOperationPath.Role role) =>
        Record.Exception(() => ProtectedOperationPath.Check(@"E:\Example", Folder(sddl), role));

    [Fact]
    public void A_windows_formatted_data_drive_root_is_accepted_above_an_installation()
        // Authenticated Users' Modify on a root includes Delete, which cannot rename a root.
        => Assert.Null(Problem(FormattedRoot, ProtectedOperationPath.Role.Root));

    [Fact]
    public void Application_package_full_control_on_program_files_is_accepted()
        => Assert.Null(Problem("O:BAG:SY" + ProgramFilesDacl, ProtectedOperationPath.Role.Ancestor));

    [Fact]
    public void A_program_files_owned_by_the_installing_user_is_refused()
    {
        var problem = Problem($"O:{InstallingUser}G:SY" + ProgramFilesDacl, ProtectedOperationPath.Role.Ancestor);
        Assert.IsType<InvalidOperationException>(problem);
        Assert.Contains("owned by Administrators or SYSTEM", problem.Message, StringComparison.Ordinal);
        Assert.Contains(InstallingUser, problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ancestor")]
    [InlineData("Target")]
    public void A_folder_inheriting_authenticated_users_modify_is_refused(string role)
    {
        var problem = Problem(UnderFormattedRoot, Enum.Parse<ProtectedOperationPath.Role>(role));
        Assert.IsType<InvalidOperationException>(problem);
        Assert.Contains("S-1-5-11", problem.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Root", "(A;;FA;;;AU)", true)]
    [InlineData("Root", "(A;;WD;;;AU)", true)]
    [InlineData("Root", "(A;;0x40;;;AU)", true)]
    [InlineData("Root", "(A;;SD;;;AU)", false)]
    [InlineData("Root", "(A;;0x116;;;AU)", false)]
    [InlineData("Ancestor", "(A;;0x4;;;BU)", false)]
    [InlineData("Ancestor", "(A;;0x2;;;BU)", false)]
    [InlineData("Target", "(A;;0x4;;;BU)", true)]
    [InlineData("Ancestor", "(A;;SD;;;BU)", true)]
    [InlineData("Ancestor", "(A;;0x40;;;BU)", true)]
    [InlineData("Ancestor", "(A;;WO;;;BU)", true)]
    [InlineData("Ancestor", "(A;;GA;;;BU)", true)]
    [InlineData("Target", "(A;;GW;;;BU)", true)]
    [InlineData("Target", "(A;OICIIO;FA;;;BU)", false)]
    [InlineData("Ancestor", "(D;;FA;;;BU)", false)]
    [InlineData("Target", "(A;;FA;;;S-1-15-2-1)", false)]
    [InlineData("Target", "(A;;FA;;;S-1-15-3-1)", false)]
    [InlineData("Ancestor", "(A;;FA;;;WD)", true)]
    [InlineData("Target", "(A;;FA;;;CO)", true)]
    public void Each_entry_is_judged_by_where_it_sits(string role, string entry, bool refused)
    {
        var problem = Problem(Plain + entry, Enum.Parse<ProtectedOperationPath.Role>(role));
        Assert.Equal(refused, problem is not null);
        if (refused) Assert.Contains("protected from non-administrator changes", problem!.Message, StringComparison.Ordinal);
    }

    // 1.9.3 review F1. A folder under C:\ProgramData inherits BUILTIN\Users (CI)(WD,AD,WEA,WA):
    // they may create files in it, but not delete, rename or re-permission anything. That
    // folder holding sqlcmd.exe lets a user plant a DLL the elevated setup or the automation
    // account then loads.
    private const string UnderProgramData = "O:BAG:SYD:AI(A;OICIID;FA;;;SY)(A;OICIID;FA;;;BA)(A;OICIID;0x1200a9;;;BU)(A;CIID;0x116;;;BU)";

    [Theory]
    [InlineData("(A;;0x2;;;BU)")]
    [InlineData("(A;;0x4;;;BU)")]
    [InlineData("(A;CIID;0x116;;;BU)")]
    [InlineData("(A;;GW;;;BU)")]
    public void The_folder_holding_a_file_target_must_not_let_others_create_items(string entry)
    {
        Assert.Null(Record.Exception(() => ProtectedOperationPath.Check(@"E:\Example", Folder(Plain + entry), ProtectedOperationPath.Role.Ancestor)));
        var problem = Record.Exception(() => ProtectedOperationPath.Check(@"E:\Example", Folder(Plain + entry), ProtectedOperationPath.Role.Ancestor, holdsTarget: true));
        Assert.IsType<InvalidOperationException>(problem);
        Assert.Contains("S-1-5-32-545", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_windows_formatted_data_drive_root_cannot_hold_a_file_target()
        => Assert.NotNull(Record.Exception(() => ProtectedOperationPath.Check(@"E:\", Folder(FormattedRoot), ProtectedOperationPath.Role.Root, holdsTarget: true)));

    private static FileSecurity FileAcl(string sddl)
    {
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm(sddl);
        return security;
    }

    private static Exception? Walk(string path, IReadOnlyDictionary<string, FileSystemSecurity> layout) =>
        Record.Exception(() => ProtectedOperationPath.Walk(path, item => layout.TryGetValue(item, out var security)
            ? security : throw new KeyNotFoundException($"The layout has no entry for {item}.")));

    [Fact]
    public void A_sqlcmd_in_a_folder_users_may_create_files_in_is_refused()
    {
        const string fileUnderProgramData = "O:BAG:SYD:AI(A;ID;FA;;;SY)(A;ID;FA;;;BA)(A;ID;0x1200a9;;;BU)";
        var layout = new Dictionary<string, FileSystemSecurity>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\"] = Folder("O:SYG:SYD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1200a9;;;BU)"),
            [@"C:\ProgramData"] = Folder("O:SYG:SYD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)(A;CI;0x116;;;BU)"),
            [@"C:\ProgramData\SqlTools"] = Folder(UnderProgramData),
            [@"C:\ProgramData\SqlTools\Binn"] = Folder(UnderProgramData),
            [@"C:\ProgramData\SqlTools\Binn\SQLCMD.EXE"] = FileAcl(fileUnderProgramData)
        };
        var problem = Walk(@"C:\ProgramData\SqlTools\Binn\SQLCMD.EXE", layout);
        Assert.IsType<InvalidOperationException>(problem);
        Assert.Contains(@"'C:\ProgramData\SqlTools\Binn' gives S-1-5-32-545", problem.Message, StringComparison.Ordinal);
        // With the tools folder closed the same layout passes: users may still create items in
        // the folders further up, which cannot replace any part of the path.
        layout[@"C:\ProgramData\SqlTools\Binn"] = Folder(Plain);
        Assert.Null(Walk(@"C:\ProgramData\SqlTools\Binn\SQLCMD.EXE", layout));
        // A folder target is still judged by its own rights, not as the holder of a file.
        Assert.Null(Walk(@"C:\ProgramData\SqlTools\Binn", layout));
    }

    [Theory]
    [InlineData(@"C:\Tools\sqlcmd.exe", "Fixed", @"\Device\HarddiskVolume3", false)]
    [InlineData(@"F:\Tools\sqlcmd.exe", "Removable", @"\Device\HarddiskVolume7", false)]
    [InlineData(@"S:\Tools\sqlcmd.exe", "Fixed", @"\??\C:\Data\ETP", true)]
    [InlineData(@"N:\Tools\sqlcmd.exe", "Network", @"\Device\LanmanRedirector\;N:0000000000012345\pc\share", true)]
    [InlineData(@"C:\Tools\sqlcmd.exe", "Fixed", null, true)]
    [InlineData(@"C:\Tools\sqlcmd.exe", null, @"\Device\HarddiskVolume3", true)]
    [InlineData(@"\\pc\share\Tools\sqlcmd.exe", null, null, true)]
    [InlineData(@"\\?\C:\Tools\sqlcmd.exe", "Fixed", @"\Device\HarddiskVolume3", true)]
    public void Only_a_real_local_volume_has_a_root_that_cannot_be_renamed(string path, string? driveType, string? dosDevice, bool refused)
    {
        var problem = ProtectedOperationPath.VolumeProblem(path, driveType is null ? null : Enum.Parse<DriveType>(driveType), dosDevice);
        Assert.Equal(refused, problem is not null);
    }

    // 1.11.4: OPS-05 failed ACL_RIGHTS on the VM night-one run (12 Oct 2026) for the Backups folder alone. Setup
    // (initialize-etp-operation-folders.ps1, Set-PrivateDirectory) gives Modify there to SQL Server's service account, which
    // writes the .bak, and to the automation account; the probe judged Backups with no permitted writer at all.
    private const string SqlService = "S-1-5-80-3880006512-4290199581-1648723128-3569869737-3631323133"; // sc showsid MSSQL$SQLEXPRESS
    private const string Automation = "S-1-5-21-1000000001-2000000002-3000000003-1009";
    private const string SetupBackups = "O:BAG:SYD:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;" + SqlService + ")(A;OICI;0x1301bf;;;" + Automation + ")";

    [Fact]
    public void The_service_sid_is_the_one_windows_gives_the_sql_express_service()
    {
        Assert.Equal(SqlService, ProtectedOperationPath.ServiceSid("MSSQL$SQLEXPRESS"));
        Assert.Equal(SqlService, ProtectedOperationPath.ServiceSid("mssql$sqlexpress"));
    }

    [Fact]
    public void The_backups_folder_as_setup_leaves_it_passes_with_its_two_writers_and_failed_before()
    {
        var writers = ProtectedOperationPath.BackupWriters(["MSSQL$SQLEXPRESS"], Automation);
        Assert.Null(Record.Exception(() => ProtectedOperationPath.Check(@"C:\ProgramData\EtpReporting\Backups", Folder(SetupBackups), ProtectedOperationPath.Role.Target, targetModifier: writers)));
        // 1.11.3's rule (no writer permitted) is what the VM reported.
        Assert.Contains("protected from non-administrator changes", Problem(SetupBackups, ProtectedOperationPath.Role.Target)!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(A;OICI;0x1301bf;;;BU)")]                                   // Users may modify
    [InlineData("(A;OICI;0x1301bf;;;S-1-5-21-1000000001-2000000002-3000000003-1010)")] // another account
    [InlineData("(A;OICI;0x1701bf;;;" + SqlService + ")")]                   // the SQL service may change permissions
    [InlineData("(A;OICI;FA;;;" + Automation + ")")]                         // the automation account has Full Control
    public void A_backups_writer_outside_setup_or_with_more_than_modify_is_still_a_finding(string extra)
    {
        var writers = ProtectedOperationPath.BackupWriters(["MSSQL$SQLEXPRESS"], Automation);
        Assert.NotNull(Record.Exception(() => ProtectedOperationPath.Check(@"C:\ProgramData\EtpReporting\Backups", Folder(SetupBackups + extra), ProtectedOperationPath.Role.Target, targetModifier: writers)));
    }

    [Fact]
    public void Without_a_resolved_automation_account_only_the_sql_service_may_write_the_backups()
    {
        var writers = ProtectedOperationPath.BackupWriters(["MSSQL$SQLEXPRESS"], null);
        Assert.True(writers(SqlService));
        Assert.False(writers(Automation));
        Assert.Null(ProtectedOperationPath.AccountSid(null));
        Assert.Null(ProtectedOperationPath.AccountSid(@"NOSUCHPC\NoSuchAccount1114"));
    }

    [Fact]
    public void The_checker_gives_each_folder_its_own_writers()
    {
        var writers = ProtectedOperationPath.BackupWriters(["MSSQL$SQLEXPRESS"], Automation);
        var checker = new ProtectedOperationPathChecker(@"C:\ProgramData\EtpReporting\HealthExports", @"C:\ProgramData\EtpReporting\Backups", writers);
        Assert.Same(writers, checker.TargetModifier(@"C:\ProgramData\EtpReporting\Backups\"));
        Assert.NotNull(checker.TargetModifier(@"C:\ProgramData\EtpReporting\HealthExports"));
        Assert.Null(checker.TargetModifier(@"C:\ProgramData\EtpReporting\Operations\operations.json"));
        Assert.Null(new ProtectedOperationPathChecker().TargetModifier(@"C:\ProgramData\EtpReporting\Backups"));
    }

    [Fact]
    public void The_system_drive_is_a_real_local_volume()
    {
        var path = Path.GetFullPath(Environment.SystemDirectory);
        Assert.Null(ProtectedOperationPath.VolumeProblem(path, ProtectedOperationPath.DriveTypeOf(path), ProtectedOperationPath.DosDeviceOf(path)));
    }
}
