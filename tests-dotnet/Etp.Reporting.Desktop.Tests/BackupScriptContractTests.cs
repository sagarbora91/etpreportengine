namespace Etp.Reporting.Desktop.Tests;

public sealed class BackupScriptContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Backup_filename_is_collision_resistant_and_existing_targets_fail_closed()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "backup-etp-database.ps1"));

        Assert.Contains("yyyyMMdd-HHmmss-fff", script, StringComparison.Ordinal);
        Assert.Contains("[Guid]::NewGuid().ToString(\"N\")", script, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $backupPath", script, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite an existing database backup.", script, StringComparison.Ordinal);

        var guard = script.IndexOf("Test-Path -LiteralPath $backupPath", StringComparison.Ordinal);
        var backup = script.IndexOf("BACKUP DATABASE", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < backup, "The existing-file guard must run before SQL Server can write the backup.");
    }

    [Fact]
    public void Backup_enforces_requested_disk_floor_and_writes_receipt_only_after_verification()
    {
        var script = ReadRepositoryFile("scripts", "backup-etp-database.ps1");

        Assert.Contains("[double]$MinimumFreeSpaceGb", script, StringComparison.Ordinal);
        Assert.Contains("RESTORE VERIFYONLY", script, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 1", script, StringComparison.Ordinal);
        Assert.Contains("verified = $true", script, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $backupPath -Algorithm SHA256", script, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite an existing backup verification receipt.", script, StringComparison.Ordinal);

        var diskFloor = script.IndexOf("$MinimumFreeSpaceGb -gt 0", StringComparison.Ordinal);
        var backup = script.IndexOf("BACKUP DATABASE", StringComparison.Ordinal);
        var verify = script.IndexOf("RESTORE VERIFYONLY", StringComparison.Ordinal);
        var receipt = script.IndexOf("$receipt = [ordered]@{", StringComparison.Ordinal);
        var publish = script.IndexOf("Move-Item -LiteralPath $temporaryResultPath", StringComparison.Ordinal);
        Assert.True(diskFloor >= 0 && diskFloor < backup, "The requested disk-space floor must fail before backup starts.");
        Assert.True(verify > backup && receipt > verify && publish > receipt, "The receipt must be published only after SQL Server verification and hashing.");
    }

    [Fact]
    public void Bootstrap_backs_up_pending_existing_database_before_migration_and_health_checks_afterward()
    {
        var script = ReadRepositoryFile("scripts", "bootstrap-etp-prerequisites.ps1");

        Assert.Contains("SQL Server 2022 or newer is required", script, StringComparison.Ordinal);
        Assert.Contains("compatibility level", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MinimumBackupFreeSpaceGb", script, StringComparison.Ordinal);
        Assert.Contains("Assert-VerifiedBackupReceipt", script, StringComparison.Ordinal);
        Assert.Contains("No automatic restore or reverse migration was attempted", script, StringComparison.Ordinal);
        Assert.Contains("DBCC CHECKDB", script, StringComparison.Ordinal);
        Assert.DoesNotContain("RESTORE DATABASE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", script, StringComparison.OrdinalIgnoreCase);

        var existingDatabaseCheck = script.IndexOf("$databaseState = Invoke-SqlScalar", StringComparison.Ordinal);
        var pendingMigrationCheck = script.IndexOf("$appliedMigrationCount -lt $migrationFiles.Count", StringComparison.Ordinal);
        var verifiedBackup = script.IndexOf("& $backupScript", StringComparison.Ordinal);
        var migration = script.IndexOf("Start-Process -FilePath $application", StringComparison.Ordinal);
        var postMigrationState = script.IndexOf("$postState = Invoke-SqlScalar", StringComparison.Ordinal);
        var integrityCheck = script.IndexOf("DBCC CHECKDB", StringComparison.Ordinal);
        Assert.True(
            existingDatabaseCheck >= 0 && existingDatabaseCheck < pendingMigrationCheck &&
            pendingMigrationCheck < verifiedBackup && verifiedBackup < migration &&
            migration < postMigrationState && postMigrationState < integrityCheck,
            "Upgrade safety must preserve preflight -> verified backup -> migration -> post-migration health ordering.");
    }

    [Fact]
    public void Installer_aborts_through_setup_engine_and_lifecycle_test_can_guard_external_data()
    {
        var installer = ReadRepositoryFile("installer", "EtpReportingEngine.iss");
        var lifecycle = ReadRepositoryFile("scripts", "test-installer-lifecycle.ps1");

        Assert.Contains("Name: \"sqlprerequisites\"", installer, StringComparison.Ordinal);
        Assert.Contains("Flags: checkedonce", installer, StringComparison.Ordinal);
        Assert.Contains("if (CurStep = ssPostInstall) then", installer, StringComparison.Ordinal);
        Assert.Contains("if not WizardIsTaskSelected('sqlprerequisites') then", installer, StringComparison.Ordinal);
        Assert.Contains("Parameters := Parameters + ' -SkipSqlInstallation'", installer, StringComparison.Ordinal);
        Assert.Contains("RaiseException('Mandatory database migration and health validation failed", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("and WizardIsTaskSelected('sqlprerequisites')", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("ExitProcess@kernel32", installer, StringComparison.Ordinal);
        Assert.Contains("No automatic restore or database deletion was attempted", installer, StringComparison.Ordinal);
        Assert.Contains("after application files were installed", installer, StringComparison.Ordinal);
        Assert.Contains("Do not launch ETP until setup completes successfully", installer, StringComparison.Ordinal);
        Assert.Contains("[string]$PreservedFilePath", lifecycle, StringComparison.Ordinal);
        Assert.Contains("[switch]$AllowPrerequisiteInstallation", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Assert-PreservedFileUnchanged 'install'", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Assert-PreservedFileUnchanged 'upgrade'", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Assert-PreservedFileUnchanged 'uninstall'", lifecycle, StringComparison.Ordinal);
        Assert.Contains("'/MERGETASKS=!sqlprerequisites'", lifecycle, StringComparison.Ordinal);
        Assert.Contains("'/MERGETASKS=sqlprerequisites'", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("!sqlbootstrap", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("/CURRENTUSER", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Get-NormalizedReleaseProductVersion $installedVersionInfo.ProductVersion", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Installed executable ProductVersion", lifecycle, StringComparison.Ordinal);
        Assert.Contains("$expectedFileVersion = \"$normalizedExpectedVersion.0\"", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Expected embedded FileVersion", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("$actual -and", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith($ExpectedVersion", lifecycle, StringComparison.Ordinal);
        Assert.Contains("[string]::Equals($actualProductVersion, $normalizedExpectedVersion", lifecycle, StringComparison.Ordinal);

        var mandatoryDatabasePath = installer.IndexOf("if (CurStep = ssPostInstall) then", StringComparison.Ordinal);
        var optionalPrerequisiteChoice = installer.IndexOf("if not WizardIsTaskSelected('sqlprerequisites') then", StringComparison.Ordinal);
        var bootstrapExecution = installer.IndexOf("if (not Exec(", StringComparison.Ordinal);
        Assert.True(
            mandatoryDatabasePath >= 0 && mandatoryDatabasePath < optionalPrerequisiteChoice && optionalPrerequisiteChoice < bootstrapExecution,
            "Every install/upgrade must execute database migration and health validation; only prerequisite package installation may be skipped.");
    }

    private static string ReadRepositoryFile(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
