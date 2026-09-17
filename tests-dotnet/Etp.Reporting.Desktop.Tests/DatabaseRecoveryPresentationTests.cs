using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// R4. Missing evidence must read Missing and old evidence must read Stale (n days).
/// An owner shown "Healthy" beside a backup that was never taken has been told the
/// opposite of the truth, which is the failure this block exists to prevent.
/// </summary>
public sealed class DatabaseRecoveryPresentationTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
    private static DatabaseOperationalHealthThresholds Thresholds => DatabaseOperationalHealthThresholds.Default;

    private static DatabaseOperationalHealth Health(
        DateTime? backup = null, DateTime? drill = null, DateTime? import = null,
        string? backupHash = null, string? drillHash = null, decimal? freeSpace = 50m, int failed = 0) =>
        new(OperationalHealthSeverity.Healthy, 512m, 10240m, backup, failed, freeSpace, [])
        {
            LastSuccessfulBackupSha256 = backupHash,
            LastSuccessfulRecoveryDrillUtc = drill,
            LastSuccessfulRecoveryDrillSha256 = drillHash,
            LastSuccessfulImportUtc = import,
            LastSuccessfulImportFiles = import is null ? 0 : 5
        };

    private static string StatusOf(DatabaseOperationalHealth health, string item) =>
        DatabaseRecoveryPresentation.Lines(health, Thresholds, Now).Single(x => x.Item == item).Status;

    [Fact]
    public void Absent_backup_and_drill_read_Missing_and_never_Healthy()
    {
        var health = Health();
        Assert.Equal("Missing", StatusOf(health, "Last verified backup"));
        Assert.Equal("Missing", StatusOf(health, "Last verified recovery drill"));
        Assert.Equal("Missing", StatusOf(health, "Last successful import"));
        Assert.DoesNotContain(DatabaseRecoveryPresentation.Lines(health, Thresholds, Now),
            line => line.Status.Contains("Healthy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Old_evidence_reads_Stale_with_the_age_in_days()
    {
        // Backups are expected within 36 hours; this one is four days old.
        var health = Health(backup: Now.AddDays(-4), backupHash: new string('a', 64));
        Assert.Equal("Stale (4 days)", StatusOf(health, "Last verified backup"));

        // Drills are expected within 45 days; this one is a hundred.
        var older = Health(drill: Now.AddDays(-100), drillHash: new string('b', 64));
        Assert.Equal("Stale (100 days)", StatusOf(older, "Last verified recovery drill"));
    }

    [Fact]
    public void One_day_stale_is_singular()
        => Assert.Equal("Stale (1 day)", DatabaseRecoveryPresentation.Describe(Now.AddHours(-40), Thresholds.MaximumBackupAge, Now));

    [Fact]
    public void Current_evidence_shows_its_time_and_fingerprint()
    {
        var health = Health(backup: Now.AddHours(-2), backupHash: new string('c', 64));
        var line = DatabaseRecoveryPresentation.Lines(health, Thresholds, Now).Single(x => x.Item == "Last verified backup");
        Assert.NotEqual("Missing", line.Status);
        Assert.DoesNotContain("Stale", line.Status);
        Assert.Contains("Fingerprint cccccccccccccccc", line.Detail);
    }

    [Fact]
    public void Warnings_are_listed_individually()
    {
        var health = new DatabaseOperationalHealth(OperationalHealthSeverity.Critical, 9000m, 10240m, null, 3, 2m,
        [
            new("BACKUP_MISSING", OperationalHealthSeverity.Critical, "No verified backup."),
            new("DISK_LOW", OperationalHealthSeverity.Warning, "Backup disk is nearly full.")
        ]);
        var warnings = DatabaseRecoveryPresentation.Lines(health, Thresholds, Now).Where(x => x.Item == "Warning").ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, x => x.Detail.Contains("No verified backup"));
        Assert.Contains(warnings, x => x.Detail.Contains("nearly full"));
    }

    [Fact]
    public void Failed_imports_are_surfaced_with_where_to_look()
    {
        Assert.Equal("3", StatusOf(Health(failed: 3), "Failed imports, last 24 hours"));
        Assert.Contains("Problems", DatabaseRecoveryPresentation.Lines(Health(failed: 3), Thresholds, Now)
            .Single(x => x.Item == "Failed imports, last 24 hours").Detail);
    }

    [Fact]
    public void Unreadable_health_says_so_rather_than_showing_an_empty_block()
    {
        var line = Assert.Single(DatabaseRecoveryPresentation.Lines(null, Thresholds, Now));
        Assert.Equal("Missing", line.Status);
    }

    [Fact]
    public void Free_space_reads_Missing_when_the_backup_folder_cannot_be_read()
        => Assert.Equal("Missing", StatusOf(Health(freeSpace: null), "Backup folder free space"));
}
