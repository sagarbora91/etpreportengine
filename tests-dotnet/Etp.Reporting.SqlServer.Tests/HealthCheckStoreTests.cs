using System.Text.Json;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.11.0 self-diagnostics stores: the JSON documents sent to the <c>diag.*</c> procedures keep the shapes of
/// docs\design\SELF-DIAGNOSTICS-PROCEDURES.md (camelCase names, ISO UTC instants, yyyy-MM-dd dates), and
/// <c>detail_json</c> reads back into sub-results. No database is needed.
/// </summary>
public sealed class HealthCheckStoreTests
{
    [Fact]
    public void Ingest_json_is_an_array_with_the_documented_camelCase_properties()
    {
        var item = new ErrorOccurrence(
            new string('a', 64), 1,
            new ErrorFingerprintParts("REPORT_STORE_REQUIRED", "", "Reports.Workspace", "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "System.InvalidOperationException", 0),
            new DateTimeOffset(2026, 10, 9, 15, 45, 2, 123, TimeSpan.FromHours(5.5)),
            ErrorRegisterSeverity.Error, "1A2B3C4D5E6F", "1.11.0.0", "staff", "ALL",
            new DateOnly(2026, 10, 9), null, "RunReport",
            ["SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "ReportsWorkspaceView.RunAsync"]);

        using var document = JsonDocument.Parse(SqlServerErrorRegisterStore.BuildIngestJson([item]));

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var row = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ["fingerprint", "fingerprintVersion", "eventBase", "subCode", "source", "site", "exceptionType", "sqlNumber",
             "timestampUtc", "severity", "reference", "applicationVersion", "reportCode", "storeCode", "businessDate",
             "dateFrom", "operation", "stack"],
            row.EnumerateObject().Select(x => x.Name));
        Assert.Equal(new string('a', 64), row.GetProperty("fingerprint").GetString());
        Assert.Equal(1, row.GetProperty("fingerprintVersion").GetInt32());
        Assert.Equal("", row.GetProperty("subCode").GetString());
        Assert.Equal(0, row.GetProperty("sqlNumber").GetInt32());
        // 15:45:02.123 at +05:30 is 10:15:02.123 UTC.
        Assert.Equal("2026-10-09T10:15:02.123Z", row.GetProperty("timestampUtc").GetString());
        Assert.Equal("Error", row.GetProperty("severity").GetString());
        Assert.Equal("2026-10-09", row.GetProperty("businessDate").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("dateFrom").ValueKind);
        Assert.Equal(
            ["SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "ReportsWorkspaceView.RunAsync"],
            row.GetProperty("stack").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void Run_json_matches_the_record_health_check_run_document()
    {
        var started = new DateTimeOffset(2026, 10, 10, 21, 0, 4, TimeSpan.Zero);
        var run = new HealthCheckRun(
            HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 9), started, started.AddMilliseconds(41250),
            "1.11.0.0", "WORKPC", "Automation", HealthOutcome.Fail,
            new HealthCheckCounts(41, 5, 1, 2, 2), "health-20261010-210004-Nightly.json",
            [
                new HealthCheckResult(
                    "DB-04", HealthCheckArea.Database, HealthOutcome.Warning, "AUTO_CLOSE_ON",
                    "AUTO_CLOSE is on for EtpReporting.", null, TimeSpan.FromMilliseconds(12),
                    SubResults: [new HealthCheckSubResult("AX123", HealthOutcome.Warning, "EXPORTS_MISSING", 2m,
                        new Dictionary<string, string> { ["report"] = "R022" })]),
                new HealthCheckResult("DB-01", HealthCheckArea.Database, HealthOutcome.Pass, "OK", "Fine.", "FT-001", TimeSpan.Zero, 3.5m)
            ]);

        using var document = JsonDocument.Parse(SqlServerHealthCheckStore.BuildRunJson(run));
        var root = document.RootElement;

        Assert.Equal("Nightly", root.GetProperty("trigger").GetString());
        Assert.Equal("2026-10-09", root.GetProperty("businessDate").GetString());
        Assert.Equal("2026-10-10T21:00:04.000Z", root.GetProperty("startedUtc").GetString());
        Assert.Equal("2026-10-10T21:00:45.250Z", root.GetProperty("completedUtc").GetString());
        Assert.Equal(41250, root.GetProperty("durationMs").GetInt64());
        Assert.Equal("Fail", root.GetProperty("overall").GetString());
        Assert.Equal("Automation", root.GetProperty("runByRole").GetString());
        var counts = root.GetProperty("counts");
        Assert.Equal([41, 5, 1, 2, 2],
            new[] { "pass", "warning", "fail", "known", "skipped" }.Select(x => counts.GetProperty(x).GetInt32()));
        Assert.Equal("health-20261010-210004-Nightly.json", root.GetProperty("exportFileName").GetString());

        var results = root.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        var first = results[0];
        Assert.Equal("DB-04", first.GetProperty("checkId").GetString());
        Assert.Equal("Database", first.GetProperty("area").GetString());
        Assert.Equal("Warning", first.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("fixTaskId").ValueKind);
        Assert.Equal(12, first.GetProperty("durationMs").GetInt64());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("metricValue").ValueKind);
        var detail = Assert.Single(first.GetProperty("detail").EnumerateArray());
        Assert.Equal("AX123", detail.GetProperty("key").GetString());
        Assert.Equal("EXPORTS_MISSING", detail.GetProperty("findingCode").GetString());
        Assert.Equal(2m, detail.GetProperty("metricValue").GetDecimal());
        Assert.Equal("R022", detail.GetProperty("fields").GetProperty("report").GetString());

        var second = results[1];
        Assert.False(second.TryGetProperty("detail", out _));
        Assert.Equal("FT-001", second.GetProperty("fixTaskId").GetString());
        Assert.Equal(3.5m, second.GetProperty("metricValue").GetDecimal());
    }

    [Fact]
    public void Run_json_refuses_more_than_eighty_results()
    {
        var started = DateTimeOffset.UtcNow;
        var results = Enumerable.Range(0, 81)
            .Select(_ => new HealthCheckResult("DB-01", HealthCheckArea.Database, HealthOutcome.Pass, "OK", "Fine.", null, TimeSpan.Zero))
            .ToList();
        var run = new HealthCheckRun(HealthCheckTrigger.Manual, new DateOnly(2026, 10, 9), started, started, "1.11.0.0", "PC", "Owner",
            HealthOutcome.Pass, new HealthCheckCounts(81, 0, 0, 0, 0), null, results);

        Assert.Throws<ArgumentException>(() => SqlServerHealthCheckStore.BuildRunJson(run));
    }

    [Fact]
    public void Detail_json_reads_back_into_sub_results_and_tolerates_junk()
    {
        var subs = SqlServerHealthCheckStore.ParseDetail(
            """[{"key":"AX123","outcome":"Warning","findingCode":"EXPORTS_MISSING","metricValue":2,"fields":{"report":"R022","count":3}}]""");

        var sub = Assert.Single(subs!);
        Assert.Equal("AX123", sub.Key);
        Assert.Equal(HealthOutcome.Warning, sub.Outcome);
        Assert.Equal("EXPORTS_MISSING", sub.FindingCode);
        Assert.Equal(2m, sub.MetricValue);
        Assert.Equal("R022", sub.Fields!["report"]);
        Assert.Equal("3", sub.Fields["count"]);

        Assert.Null(SqlServerHealthCheckStore.ParseDetail(null));
        Assert.Null(SqlServerHealthCheckStore.ParseDetail(""));
        Assert.Null(SqlServerHealthCheckStore.ParseDetail("not json"));
        Assert.Null(SqlServerHealthCheckStore.ParseDetail("{}"));
    }
}
