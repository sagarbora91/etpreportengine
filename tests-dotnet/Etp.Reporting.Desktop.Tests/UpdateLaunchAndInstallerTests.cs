using System.Diagnostics;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1): how setup is started for an update, the pre-update backup's arguments, and the installer's update-only
/// behaviour (wait for ETP to close, write setup-result.json, restart ETP unelevated only on success).
/// </summary>
public sealed class UpdateLaunchAndInstallerTests
{
    private const string Attempt = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Setup_runs_silent_with_a_log_never_the_sql_media_and_elevates_itself()
    {
        var start = UpdateInstallerLauncher.BuildStartInfo(@"C:\ProgramData\EtpReporting\Updates\1.12.0\EtpReportingEngine-Setup-1.12.0-x64.exe",
            @"C:\ProgramData\EtpReporting\Updates\1.12.0\setup-" + Attempt + ".log", Attempt);
        Assert.Equal(@"C:\ProgramData\EtpReporting\Updates\1.12.0\EtpReportingEngine-Setup-1.12.0-x64.exe", start.FileName);
        Assert.Equal("/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"C:\\ProgramData\\EtpReporting\\Updates\\1.12.0\\setup-" + Attempt + ".log\" /MERGETASKS=\"!sqlprerequisites\" /ETPUPDATE=1 /ETPATTEMPT=" + Attempt,
            start.Arguments);
        // No "runas": setup asks for elevation itself and keeps its unelevated original for the restart (runasoriginaluser).
        Assert.True(string.IsNullOrEmpty(start.Verb));
        Assert.True(start.UseShellExecute);

        Assert.Throws<ArgumentException>(() => UpdateInstallerLauncher.BuildStartInfo(@"C:\x\setup.exe", @"C:\x\log.txt", "not-an-id"));
        Assert.Throws<ArgumentException>(() => UpdateInstallerLauncher.BuildStartInfo(@"setup.exe", @"C:\x\log.txt", Attempt));
        Assert.Throws<ArgumentException>(() => UpdateInstallerLauncher.BuildStartInfo(@"C:\x\setup.exe", "C:\\x\\a\" /DIR=\"C:\\evil", Attempt));
    }

    [Fact]
    public void The_pre_update_backup_runs_the_installed_backup_script_with_purpose_PreUpdate_and_a_result_file()
    {
        var start = PreUpdateBackupRunner.BuildStartInfo(@"C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\backup-etp-database.ps1",
            @"Data Source=localhost\SQLEXPRESS;Initial Catalog=EtpReporting;Integrated Security=true", @"C:\ProgramData\EtpReporting\Updates\1.12.0\pre-update-backup.json");
        var arguments = start.ArgumentList.ToList();
        Assert.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.False(start.Environment.ContainsKey("PSModulePath"));
        Assert.Equal(new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File" }, arguments.Take(4));
        Assert.Equal("EtpReporting", PreUpdateBackupRunner.DatabaseArgument(start));
        Assert.Equal("PreUpdate", arguments[arguments.IndexOf("-Purpose") + 1]);
        Assert.Equal(@"C:\ProgramData\EtpReporting\Updates\1.12.0\pre-update-backup.json", arguments[arguments.IndexOf("-ResultPath") + 1]);
    }

    /// <summary>
    /// INTEGRATOR: needs the two-line change to scripts\backup-etp-database.ps1 in the B1 NOTE (not a B1 file): 'PreUpdate'
    /// in the -Purpose ValidateSet, written to the receipt as PRE_UPDATE. Until then this test fails, and an in-app update
    /// stops at its backup (safe: nothing is installed).
    /// </summary>
    [Fact]
    public void The_backup_script_accepts_purpose_PreUpdate_and_records_it_as_PRE_UPDATE_which_rotation_keeps()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "backup-etp-database.ps1"));
        Assert.Contains("[ValidateSet('Scheduled','PreMigration','PreRollback','PreUpdate')][string]$Purpose", script, StringComparison.Ordinal);
        Assert.Contains("'PreUpdate' { 'PRE_UPDATE' }", script, StringComparison.Ordinal);
        // Rotation deletes only SCHEDULED backups; anything else is kept (etp-operations-common.ps1).
        var common = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "etp-operations-common.ps1"));
        Assert.Contains("return ([string]$Receipt.purpose) -ceq 'SCHEDULED'", common, StringComparison.Ordinal);
    }

    private static string Installer() => File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "EtpReportingEngine.iss"));

    [Fact]
    public void The_installer_restarts_etp_after_an_update_only_on_success_and_never_elevated()
    {
        var iss = Installer();
        var restart = iss.Split('\n').Single(line => line.Contains("Check: ShouldRestartAfterUpdate", StringComparison.Ordinal));
        Assert.Contains("Filename: \"{app}\\{#AppExeName}\"", restart, StringComparison.Ordinal);
        Assert.Contains("runasoriginaluser", restart, StringComparison.Ordinal);
        Assert.Contains("postinstall", restart, StringComparison.Ordinal);
        Assert.DoesNotContain("skipifsilent", restart, StringComparison.Ordinal);
        Assert.Contains("Result := IsEtpUpdate() and ShouldOfferLaunch();", iss, StringComparison.Ordinal);
        Assert.Contains("Result := ExpandConstant('{param:ETPUPDATE|0}') = '1';", iss, StringComparison.Ordinal);
        // The ordinary launch entry is unchanged: never in a silent install.
        Assert.Contains("Flags: nowait postinstall skipifsilent; Check: ShouldOfferLaunch", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void The_installer_waits_for_etp_to_close_only_for_an_update_then_keeps_its_old_refusal()
    {
        var iss = Installer();
        var initialize = iss[iss.IndexOf("function InitializeSetup(): Boolean;", StringComparison.Ordinal)..];
        initialize = initialize[..initialize.IndexOf("\nend;", StringComparison.Ordinal)];
        var wait = initialize.IndexOf("while CheckForMutexes('{#AppMutexName}') and (Waited < 240) do", StringComparison.Ordinal);
        var refuse = initialize.IndexOf("if CheckForMutexes('{#AppMutexName}') then", StringComparison.Ordinal);
        Assert.True(initialize.IndexOf("if IsEtpUpdate() then", StringComparison.Ordinal) is >= 0 and var guard && guard < wait, "The wait is only for an update.");
        Assert.True(wait >= 0 && refuse > wait, "After the wait, a running ETP is still refused.");
        Assert.Contains("EtpStillRunning := True;", initialize, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminateProcess", iss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_installer_writes_the_update_result_with_fixed_codes_and_stamps_its_own_product_version()
    {
        var iss = Installer();
        Assert.Contains("VersionInfoProductTextVersion={#AppVersion}", iss, StringComparison.Ordinal);
        Assert.Contains("VersionInfoVersion={#AppVersion}", iss, StringComparison.Ordinal);
        Assert.Contains("Folder := ExpandConstant('{commonappdata}\\EtpReporting\\Updates');", iss, StringComparison.Ordinal);
        Assert.Contains("SaveStringToFile(Folder + '\\setup-result.json',", iss, StringComparison.Ordinal);
        foreach (var code in new[] { "'ETP_STILL_RUNNING'", "'SETUP_STEP_FAILED'", "'DATABASE_RESTORE_PENDING'", "'SUCCEEDED'", "'SETUP_CANCELLED'" })
            Assert.Contains(code, iss, StringComparison.Ordinal);
        var deinitialize = iss[iss.IndexOf("procedure DeinitializeSetup();", StringComparison.Ordinal)..];
        Assert.True(deinitialize.IndexOf("RecordEtpUpdateResult();", StringComparison.Ordinal) < deinitialize.IndexOf("ExitProcess(SetupIncompleteExitCode);", StringComparison.Ordinal),
            "The result is written before setup exits with 1603.");
        Assert.Contains("if CurStep = ssDone then", iss, StringComparison.Ordinal);
    }
}
