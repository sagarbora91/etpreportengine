using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class DatabaseOperationalHealthTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_IsHealthy_WhenAllControlsAreWithinLimits()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-2), 0, Now);
        Assert.Equal(OperationalHealthSeverity.Healthy, result.Severity);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Evaluate_ReportsMissingBackupAsCritical()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, null, null, 0, Now);
        Assert.Equal(OperationalHealthSeverity.Critical, result.Severity);
        Assert.Contains(result.Warnings, x => x.Code == "BACKUP_MISSING");
    }

    [Fact]
    public void Evaluate_ReportsStaleBackupGrowthAndFailedImports()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(850, 1000, Now.AddHours(-40), 2, Now);
        Assert.Equal(OperationalHealthSeverity.Warning, result.Severity);
        Assert.Equal(["BACKUP_STALE", "DATABASE_GROWTH", "FAILED_IMPORTS"], result.Warnings.Select(x => x.Code));
    }

    [Theory]
    [InlineData(19, "BACKUP_SPACE_LOW", OperationalHealthSeverity.Warning)]
    [InlineData(4, "BACKUP_SPACE_CRITICAL", OperationalHealthSeverity.Critical)]
    public void Evaluate_ReportsLowBackupStorage(decimal freeGb, string code, OperationalHealthSeverity severity)
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-2), 0, Now, backupFreeSpaceGb: freeGb);
        Assert.Equal(severity, result.Severity);
        Assert.Contains(result.Warnings, x => x.Code == code);
    }

    [Fact]
    public void Evaluate_Exposes_separate_backup_and_drill_evidence_with_fresh_recovery_status()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-1), 0, Now,
            lastBackupSha256: new string('B', 64), lastRecoveryDrillUtc: Now.AddDays(-30),
            lastRecoveryDrillSha256: new string('A', 64), monitorRecoveryDrill: true);
        Assert.Equal(OperationalHealthSeverity.Healthy, result.Severity);
        Assert.Equal(new string('B', 64), result.LastSuccessfulBackupSha256);
        Assert.Equal(Now.AddDays(-30), result.LastSuccessfulRecoveryDrillUtc);
        Assert.Equal(new string('A', 64), result.LastSuccessfulRecoveryDrillSha256);
    }

    [Theory]
    [InlineData(null, "RECOVERY_DRILL_MISSING")]
    [InlineData(46, "RECOVERY_DRILL_STALE")]
    public void Evaluate_Warns_when_monthly_recovery_drill_is_missing_or_overdue(int? daysAgo, string code)
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-1), 0, Now,
            lastRecoveryDrillUtc: daysAgo is { } days ? Now.AddDays(-days) : null, monitorRecoveryDrill: true);
        Assert.Equal(OperationalHealthSeverity.Warning, result.Severity);
        Assert.Equal(code, Assert.Single(result.Warnings).Code);
    }
}
