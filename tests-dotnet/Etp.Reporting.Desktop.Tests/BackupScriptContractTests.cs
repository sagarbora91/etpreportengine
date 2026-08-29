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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
