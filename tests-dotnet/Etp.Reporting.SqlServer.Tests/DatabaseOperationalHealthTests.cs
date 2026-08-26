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
}
