using System.Security.AccessControl;

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
}
