using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0 code review (111-CODE-REVIEW.md) Blockers 1 and 2, across the layers that each passed their own tests:
/// (2) an entry the desktop log writes with an operation text that has spaces is ingested by
/// <c>diag.ingest_error_occurrences</c>, not rejected and lost; (1) a run with the sub-results the probes really
/// produce (edition text, comma-separated codes, an event key with five digits) is recorded by
/// <c>diag.record_health_check_run</c> without 51820 and keeps its detail. Own database (class fixture). Synthetic data.
/// </summary>
public sealed class HealthCheckReviewFixSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [SqlFact]
    public async Task Blocker_2_a_logged_operation_with_spaces_is_ingested_and_stored_as_a_token()
    {
        var log = Path.Combine(Path.GetTempPath(), "etp-review-ingest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reference = DesktopDiagnostics.Record(new InvalidOperationException("synthetic"), "Reports.Workspace", "REPORT_RUN_FAILED",
                logDirectory: log, timestampUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
                context: new DesktopDiagnosticContext("staff", "ALL", new DateOnly(2026, 10, 9), null), operation: "Run report");
            DesktopDiagnostics.Record(new InvalidOperationException("synthetic"), "HealthCheck.Probe", "HEALTH_REPORT_PROBE_FAILED",
                logDirectory: log, timestampUtc: DateTimeOffset.UtcNow.AddMinutes(-1), operation: "Health check report probe failed");

            var run = await new ErrorRegisterIngest(new SqlServerErrorRegisterStore(database.ConnectionString), "1.11.0.0", log).RunAsync();

            Assert.False(run.Failed);
            Assert.Equal(0, run.SkippedInvalid);
            Assert.Equal(2, run.Sent);
            Assert.Equal(0, run.Rejected);
            Assert.Equal(2, run.Accepted);
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT operation FROM diag.error_occurrences WHERE reference=@r", connection);
            command.Parameters.AddWithValue("@r", reference);
            Assert.Equal("Run_report", await command.ExecuteScalarAsync());
        }
        finally
        {
            try { Directory.Delete(log, recursive: true); } catch (IOException) { }
        }
    }

    [SqlFact]
    public async Task Blocker_1_a_run_with_real_probe_details_is_recorded_whole_and_keeps_its_detail()
    {
        var day = new DateOnly(2026, 10, 9);
        var data01 = DataProbes.EvaluateCompleteness(day, new DailyReadinessResult(day, ["ZZ001", "ZZ002"],
        [
            new(DailyReadinessItemKind.MissingExport, "ZZ001", "R025", "message", DailyReadinessDestination.ImportIntake),
            new(DailyReadinessItemKind.MissingExport, "ZZ002", "R022", "message", DailyReadinessDestination.ImportIntake)
        ]), TimeSpan.Zero);
        var imp02 = ImportProbes.EvaluateFailures([new("R025", "Unknown layout", 1)], TimeSpan.Zero);
        var ops09 = EventLogProbe.Evaluate(new EventLogFindings(1, 0, new Dictionary<int, int> { [17204] = 2 }, new Dictionary<int, int>(), 0, 0));
        var ops06 = new HealthCheckResult(HealthCheckId.Ops06, HealthCheckArea.Operations, HealthOutcome.Warning, "SQL_RTM",
            "SQL Server 17.0.1000.7 (RTM) has no cumulative update installed.", null, TimeSpan.Zero, null,
            [new("sql-server", HealthOutcome.Pass, null, null, new Dictionary<string, string>
                { ["version"] = "17.0.1000.7", ["updateLevel"] = "RTM", ["edition"] = HealthCheckTexts.DetailToken("Express Edition (64-bit)") })]);
        // A detail value that is free text (not a probe's real output): dropped by the runner, never a refused run.
        var odd = new HealthCheckResult(HealthCheckId.Rpt02, HealthCheckArea.Reports, HealthOutcome.Pass, "OK", "Store Sales Summary ran.", null, TimeSpan.Zero, null,
            [new("ZZ001", HealthOutcome.Pass, "OK", 1m, new Dictionary<string, string> { ["skipped"] = "RPT-16 has no result (see Tender)" })]);

        var runner = new HealthCheckRunner([new Fixed([data01, imp02, ops09, ops06, odd])], new HealthCheckRunnerOptions
        {
            AppVersion = "1.11.0.0",
            MachineName = "TESTPC",
            RunByRole = "Owner"
        });
        var run = await runner.RunAsync(HealthCheckTrigger.Manual);
        var store = new SqlServerHealthCheckStore(database.ConnectionString);

        var id = await store.RecordRunAsync(run); // threw 51820 before the fix

        var stored = await store.LoadResultsAsync(id);
        var exports = stored.Single(line => line.CheckId == HealthCheckId.Data01).SubResults!.Single(sub => sub.Key == "EXPORTS_MISSING");
        Assert.Equal("R022+R025", exports.Fields!["codes"]);
        Assert.Equal("ZZ001+ZZ002", exports.Fields["stores"]);
        Assert.Equal("Express_Edition_64-bit", stored.Single(line => line.CheckId == HealthCheckId.Ops06).SubResults![0].Fields!["edition"]);
        Assert.Contains(stored.Single(line => line.CheckId == HealthCheckId.Ops09).SubResults!, sub => sub.Key == "event:17204");
        Assert.Equal("1", stored.Single(line => line.CheckId == HealthCheckId.Imp02).SubResults![0].Fields!["Unknown_layout"]);
        Assert.Equal(HealthCheckRunner.DroppedDetail, stored.Single(line => line.CheckId == HealthCheckId.Rpt02).SubResults![0].Fields!["skipped"]);
    }

    private sealed class Fixed(IReadOnlyList<HealthCheckResult> lines) : IHealthProbe
    {
        // One probe for several areas runs in the area of its first id; the runner takes each id's line from the answer.
        public IReadOnlyList<string> CheckIds { get; } = lines.Select(line => line.CheckId).ToArray();

        public Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken) =>
            Task.FromResult(lines);
    }
}
