using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.0 self-diagnostics: one round trip through the stores and the <c>diag</c> procedures of migrations
/// 0054/0055. The fixture connects as the test sysadmin, which is not an etp_owner member, so the Owner-only
/// procedures (entry detail, status, merge, known set/clear) are covered by the role tests, not here.
/// </summary>
public sealed class HealthCheckStoreSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [SqlFact]
    public async Task Error_occurrence_and_health_run_round_trip_through_the_stores()
    {
        var errors = new SqlServerErrorRegisterStore(database.ConnectionString);
        var fingerprint = new string('c', 64);
        var item = new ErrorOccurrence(
            fingerprint, 1,
            new ErrorFingerprintParts("STORE_ROUND_TRIP", "", "Tests.Stores", "HealthCheckStoreSqlTests.Run", "System.InvalidOperationException", 0),
            new DateTimeOffset(2026, 10, 9, 10, 15, 2, 123, TimeSpan.Zero),
            ErrorRegisterSeverity.Error, "AABBCCDDEEFF", "1.11.0.0", "staff", "ALL",
            new DateOnly(2026, 10, 9), null, "RunReport",
            ["HealthCheckStoreSqlTests.Run", "TestHost.Invoke"]);

        var first = await errors.IngestAsync([item], "1.11.0.0");
        Assert.Equal(new ErrorIngestResult(1, 0, 0, 1, 0), first);

        var again = await errors.IngestAsync([item], "1.11.0.0");
        Assert.Equal(1, again.Duplicates);
        Assert.Equal(0, again.NewEntries);

        var entry = Assert.Single(await errors.LoadRegisterAsync());
        Assert.Equal(fingerprint, entry.Fingerprint);
        Assert.Equal("STORE_ROUND_TRIP", entry.EventCode);
        Assert.Equal(ErrorStatus.New, entry.Status);
        Assert.Equal(ErrorRegisterSeverity.Error, entry.MaxSeverity);
        Assert.Equal(1, entry.OccurrenceCount);
        Assert.Equal("1.11.0.0", entry.LastVersion);
        Assert.Equal(["HealthCheckStoreSqlTests.Run", "TestHost.Invoke"], entry.LatestStack);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 10, 15, 2, 123, TimeSpan.Zero), entry.LastSeenUtc);
        Assert.Single(await errors.LoadRegisterAsync(ErrorStatus.New));
        Assert.Empty(await errors.LoadRegisterAsync(ErrorStatus.Fixed));
        Assert.Single(await errors.LoadRegisterAsync(reference: "AABBCCDDEEFF"));

        var health = new SqlServerHealthCheckStore(database.ConnectionString);
        var started = new DateTimeOffset(2026, 10, 10, 21, 0, 4, TimeSpan.Zero);
        var runId = await health.RecordRunAsync(new HealthCheckRun(
            HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 9), started, started.AddSeconds(41),
            "1.11.0.0", "TESTPC", "Automation", HealthOutcome.Warning,
            new HealthCheckCounts(1, 1, 0, 0, 0), "health-test.json",
            [
                new HealthCheckResult("DB-01", HealthCheckArea.Database, HealthOutcome.Pass, "OK", "Fine.", null, TimeSpan.FromMilliseconds(5), 1m),
                new HealthCheckResult("DB-04", HealthCheckArea.Database, HealthOutcome.Warning, "AUTO_CLOSE_ON", "AUTO_CLOSE is on.", null, TimeSpan.FromMilliseconds(12),
                    SubResults: [new HealthCheckSubResult("AX123", HealthOutcome.Warning, "EXPORTS_MISSING", 2m, new Dictionary<string, string> { ["report"] = "R022" })])
            ]));
        Assert.True(runId > 0);

        var run = Assert.Single(await health.LoadRunsAsync(5, HealthCheckTrigger.Nightly));
        Assert.Equal(runId, run.HealthCheckRunId);
        Assert.Equal(HealthCheckTrigger.Nightly, run.Trigger);
        Assert.Equal(new DateOnly(2026, 10, 9), run.BusinessDate);
        Assert.Equal(HealthOutcome.Warning, run.Overall);
        Assert.Equal(new HealthCheckCounts(1, 1, 0, 0, 0), run.Counts);
        Assert.Equal("health-test.json", run.ExportFileName);
        Assert.Empty(await health.LoadRunsAsync(5, HealthCheckTrigger.Manual));

        var results = await health.LoadResultsAsync(runId);
        Assert.Equal(["DB-01", "DB-04"], results.Select(x => x.CheckId));
        var warning = results[1];
        Assert.Equal(HealthCheckArea.Database, warning.Area);
        Assert.Equal("AUTO_CLOSE_ON", warning.FindingCode);
        var sub = Assert.Single(warning.SubResults!);
        Assert.Equal("EXPORTS_MISSING", sub.FindingCode);
        Assert.Equal("R022", sub.Fields!["report"]);

        var summary = await health.LoadLatestNightlySummaryAsync();
        Assert.NotNull(summary);
        Assert.Equal(new DateOnly(2026, 10, 9), summary!.BusinessDate);
        Assert.Equal(HealthOutcome.Warning, summary.Overall);

        Assert.Empty(await health.LoadKnownAsync());
        var snapshot = await health.LoadRolePermissionsAsync();
        Assert.NotNull(snapshot.Permissions);
    }
}
