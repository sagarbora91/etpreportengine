using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.3, Phase 4 A4.4 and A4.4a. The recovery drill compares four row counts from the backup
/// receipt with the restored copy. System status and the dashboard must show a failed drill
/// as failed, naming the table and both numbers, and a drill that had no counts to compare as
/// passed with its reason - never as an ordinary pass.
/// </summary>
public sealed class RecoveryDrillRowCountTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('A', 64);

    private static RecoveryDrillResult Matched() => new(Now.AddHours(-1), true, Hash, "Matched", null,
        [new("sales_invoices", 1200, 1200), new("sales_lines", 5400, 5400), new("import_files", 30, 30), new("daily_reporting_days", 61, 61)]);

    private static RecoveryDrillResult Mismatch() => new(Now.AddHours(-1), false, Hash, "Mismatch", null,
        [new("sales_invoices", 1200, 1200), new("sales_lines", 5401, 5400), new("import_files", 30, 30), new("daily_reporting_days", 61, 61)]);

    [Fact]
    public void A_failed_drill_is_a_critical_warning_naming_the_table_and_both_numbers()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-2), 0, Now,
            lastRecoveryDrillUtc: Now.AddDays(-3), lastRecoveryDrillSha256: Hash, monitorRecoveryDrill: true,
            latestRecoveryDrillResult: Mismatch());

        Assert.Equal(OperationalHealthSeverity.Critical, result.Severity);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("RECOVERY_DRILL_FAILED", warning.Code);
        Assert.Contains("sales_lines receipt 5,401, restored copy 5,400", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sales_invoices", warning.Message, StringComparison.Ordinal);
        Assert.NotNull(result.LatestRecoveryDrillResult);
    }

    [Fact]
    public void A_passed_drill_adds_no_warning_and_is_carried_on_the_health_record()
    {
        var drill = Matched();
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-2), 0, Now,
            lastRecoveryDrillUtc: Now.AddHours(-1), lastRecoveryDrillSha256: Hash, monitorRecoveryDrill: true,
            latestRecoveryDrillResult: drill);
        Assert.Equal(OperationalHealthSeverity.Healthy, result.Severity);
        Assert.Empty(result.Warnings);
        Assert.Same(drill, result.LatestRecoveryDrillResult);
    }

    [Fact]
    public void A_failed_drill_is_not_judged_when_drills_are_not_monitored()
    {
        var result = DatabaseOperationalHealthEvaluator.Evaluate(100, 1000, Now.AddHours(-2), 0, Now, latestRecoveryDrillResult: Mismatch());
        Assert.DoesNotContain(result.Warnings, x => x.Code == "RECOVERY_DRILL_FAILED");
    }

    [Fact]
    public void Summary_of_a_passed_drill_lists_all_four_counts()
    {
        var text = RecoveryDrillResultText.Summary(Matched());
        Assert.Equal("Passed. Row counts match the backup receipt: sales_invoices 1,200; sales_lines 5,400; import_files 30; daily_reporting_days 61.", text);
        Assert.Equal("Passed", RecoveryDrillResultText.Status(Matched()));
    }

    [Fact]
    public void Summary_of_a_mismatch_names_only_the_differing_table_with_both_numbers()
    {
        Assert.Equal("Failed. Row counts in the restored copy differ from the backup receipt: sales_lines receipt 5,401, restored copy 5,400.",
            RecoveryDrillResultText.Summary(Mismatch()));
        Assert.Equal("Failed", RecoveryDrillResultText.Status(Mismatch()));
    }

    [Theory]
    [InlineData(true, "CHANGED_DURING_BACKUP", "Passed, but row counts were not recorded by this backup: the counted tables changed while the backup was being taken.")]
    [InlineData(true, "OPERATIONS_MODULE_OUTDATED", "Passed, but row counts were not recorded by this backup: the backup was taken through an operations module older than 1.9.3.")]
    [InlineData(true, "COUNT_FAILED", "Passed, but row counts were not recorded by this backup: the tables could not be counted when the backup was taken.")]
    [InlineData(true, "RECEIPT_WITHOUT_COUNTS", "Passed, but row counts were not recorded by this backup: the backup receipt was written before 1.9.3.")]
    [InlineData(false, "OPERATIONS_MODULE_OUTDATED", "Failed: the operations module did not count the restored copy; run ETP setup again to reinstall it.")]
    [InlineData(false, "RESTORED_COPY_NOT_COUNTED", "Failed: the restored copy could not be counted.")]
    [InlineData(false, "RECEIPT_COUNTS_UNREADABLE", "Failed: the row counts in the backup receipt are unreadable.")]
    public void A_drill_without_counts_says_why_and_never_reads_as_a_plain_pass(bool succeeded, string reason, string expected)
    {
        var drill = new RecoveryDrillResult(Now, succeeded, Hash, "NotRecorded", reason, []);
        Assert.Equal(expected, RecoveryDrillResultText.Summary(drill));
    }

    [Fact]
    public void No_drill_result_reads_Missing()
    {
        Assert.Equal("Missing", RecoveryDrillResultText.Status(null));
        Assert.Equal("No recovery drill result has been recorded.", RecoveryDrillResultText.Summary(null));
    }

    [Fact]
    public async Task The_dashboard_carries_the_latest_drill_result_in_words()
    {
        var health = new DatabaseOperationalHealth(OperationalHealthSeverity.Critical, 100m, null, Now, 0, 50m, [])
        { LatestRecoveryDrillResult = Mismatch() };
        var query = new SqlServerDashboardQuery(
            _ => Task.FromResult(new OperationalSummary(0, 0, 0, null, [])),
            _ => Task.FromResult(health),
            (_, _) => Task.FromResult<IReadOnlyList<OperationalAuditEvent>>([]));
        var result = await query.LoadAsync();
        Assert.Equal(RecoveryDrillResultText.Summary(Mismatch()), result.Health.LatestRecoveryDrillSummary);

        var none = new SqlServerDashboardQuery(
            _ => Task.FromResult(new OperationalSummary(0, 0, 0, null, [])),
            _ => Task.FromResult(health with { LatestRecoveryDrillResult = null }),
            (_, _) => Task.FromResult<IReadOnlyList<OperationalAuditEvent>>([]));
        Assert.Null((await none.LoadAsync()).Health.LatestRecoveryDrillSummary);
    }

    [Fact]
    public void Migration_0043_keeps_drill_results_append_only_and_readable_only_through_procedures()
    {
        var sql = File.ReadAllText(MigrationPath());
        Assert.Contains("CREATE TABLE dbo.recovery_drill_results", sql, StringComparison.Ordinal);
        Assert.Contains("INSTEAD OF UPDATE,DELETE", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT,INSERT,UPDATE,DELETE ON dbo.recovery_drill_results TO etp_store_manager,etp_viewer,etp_automation;", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON dbo.record_recovery_drill_result TO etp_owner,etp_automation;", sql, StringComparison.Ordinal);
        Assert.Contains("GRANT EXECUTE ON dbo.load_latest_recovery_drill_result TO etp_owner,etp_store_manager,etp_viewer,etp_automation;", sql, StringComparison.Ordinal);
        // The recording procedure keeps the record_verified_operation rule: operations account,
        // Owner or SQL administrator only.
        Assert.Contains("IF COALESCE(IS_ROLEMEMBER(''etp_automation''),0)<>1", sql, StringComparison.Ordinal);
        // Every reason the scripts can write is one the table accepts.
        foreach (var reason in new[] { "CHANGED_DURING_BACKUP", "OPERATIONS_MODULE_OUTDATED", "COUNT_FAILED", "RECEIPT_WITHOUT_COUNTS", "RESTORED_COPY_NOT_COUNTED", "RECEIPT_COUNTS_UNREADABLE" })
            Assert.Contains("'" + reason + "'", sql, StringComparison.Ordinal);
        // The committed health procedure is not redefined (the runner is checksum fail-closed
        // for applied migrations; this one adds its own procedure instead).
        Assert.DoesNotContain("load_database_operational_health", sql, StringComparison.Ordinal);
    }

    private static string MigrationPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "database", "migrations", "0043_recovery_drill_row_counts.sql");
    }
}
