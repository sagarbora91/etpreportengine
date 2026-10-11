using System.Diagnostics;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Import.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.11.0 code review (111-CODE-REVIEW.md) findings 1, 3 and 5, with the shapes the probes really produce:
/// every sub-result key and field must pass <c>diag.is_token</c> after the runner normalises it (finding 1: OPS-06
/// "Express Edition (64-bit)", DATA-01 "R022,R025", IMP-02 "Unknown layout", DATA-04 "Service files" made SQL refuse
/// every run with 51820); a Known on the line's own code covers sub-findings with other codes (finding 3); a probe
/// that ignores its token cannot hold the run past its allowance (finding 5). All data is synthetic.
/// </summary>
public sealed class HealthCheckReviewFixTests
{
    // diag.is_token (0054): 1..max characters of A-Z a-z 0-9 . _ : < > + -
    private static bool IsSqlToken(string value, int max) =>
        value.Length is > 0 && value.Length <= max && Regex.IsMatch(value, "^[A-Za-z0-9._:<>+-]+$");

    private static readonly DateOnly Day = new(2026, 10, 9);

    [Theory]
    [InlineData("Express Edition (64-bit)", "Express_Edition_64-bit")]
    [InlineData("R022,R025", "R022+R025")]
    [InlineData("R022, R025 ; R030", "R022+R025+R030")]
    [InlineData("Unknown layout", "Unknown_layout")]
    [InlineData("SqlException (SQL 229)", "SqlException_SQL_229")]
    [InlineData("Run report", "Run_report")]
    [InlineData("Health check report probe failed", "Health_check_report_probe_failed")]
    [InlineData("  ", "")]
    public void Detail_token_keeps_product_wording_readable(string value, string expected)
    {
        Assert.Equal(expected, HealthCheckTexts.DetailToken(value));
        if (expected.Length > 0) Assert.True(IsSqlToken(expected, 100));
    }

    [Fact]
    public void Real_probe_shapes_are_tokens_after_normalising_and_keep_their_detail()
    {
        var data01 = DataProbes.EvaluateCompleteness(Day, new DailyReadinessResult(Day, ["ZZ001", "ZZ002"],
        [
            new(DailyReadinessItemKind.MissingExport, "ZZ001", "R025", "message", DailyReadinessDestination.ImportIntake),
            new(DailyReadinessItemKind.MissingExport, "ZZ002", "R022", "message", DailyReadinessDestination.ImportIntake),
            new(DailyReadinessItemKind.MissingManualInput, "ZZ001", "WALK_INS", "message", DailyReadinessDestination.TodayWalkIns)
        ]), TimeSpan.Zero);
        var imp02 = ImportProbes.EvaluateFailures([new("R025", "Failed", 2), new("R025", "Unknown layout", 1)], TimeSpan.Zero);
        var imp01 = ImportProbes.EvaluateFamilies([new SyntheticFamilyCheck("R025", "R025", null, ["HEADER_MISSING", "SHEET_MISSING"])], true, TimeSpan.Zero);
        var data04 = DataProbes.EvaluateService(Day.AddDays(1), new ServiceFreshnessFacts(false, true, [], new Dictionary<string, string>(), Day.AddDays(-9)), TimeSpan.Zero);
        var ops06 = new HealthCheckResult(HealthCheckId.Ops06, HealthCheckArea.Operations, HealthOutcome.Warning, "SQL_RTM", "SQL Server 17.0.1000.7 (RTM) has no cumulative update.",
            null, TimeSpan.Zero, null,
            [new("sql-server", HealthOutcome.Pass, null, null, new Dictionary<string, string>
                { ["version"] = "17.0.1000.7", ["updateLevel"] = "RTM", ["edition"] = HealthCheckTexts.DetailToken("Express Edition (64-bit)") })]);
        var ops09 = new HealthCheckResult(HealthCheckId.Ops09, HealthCheckArea.Operations, HealthOutcome.Fail, "ETP_CRASH", "Crash.", null, TimeSpan.Zero, null,
            [new("etp-crash", HealthOutcome.Fail, "ETP_CRASH", 1, new Dictionary<string, string> { ["events"] = "1000,1026" })]);

        foreach (var raw in new[] { data01, imp02, imp01, data04, ops06, ops09 })
        {
            var line = HealthCheckRunner.Normalise(raw);
            Assert.NotNull(line.SubResults);
            Assert.Equal(raw.SubResults!.Count, line.SubResults!.Count);
            foreach (var sub in line.SubResults!)
            {
                Assert.True(IsSqlToken(sub.Key, 100), $"{raw.CheckId} key '{sub.Key}'");
                foreach (var field in sub.Fields ?? new Dictionary<string, string>())
                {
                    Assert.True(IsSqlToken(field.Key, 60), $"{raw.CheckId} field key '{field.Key}'");
                    Assert.True(field.Value.Length == 0 || IsSqlToken(field.Value, 100), $"{raw.CheckId} field '{field.Key}' = '{field.Value}'");
                    Assert.NotEqual(HealthCheckRunner.DroppedDetail, field.Value);
                }
            }
        }

        var exports = HealthCheckRunner.Normalise(data01).SubResults!.Single(sub => sub.Key == "EXPORTS_MISSING");
        Assert.Equal("R022+R025", exports.Fields!["codes"]);
        Assert.Equal("ZZ001+ZZ002", exports.Fields["stores"]);
        Assert.Equal("Express_Edition_64-bit", HealthCheckRunner.Normalise(ops06).SubResults![0].Fields!["edition"]);
        Assert.Equal("1000+1026", HealthCheckRunner.Normalise(ops09).SubResults![0].Fields!["events"]);
        Assert.Equal("1", HealthCheckRunner.Normalise(imp02).SubResults![0].Fields!["Unknown_layout"]);
        Assert.Equal("HEADER_MISSING+SHEET_MISSING", HealthCheckRunner.Normalise(imp01).SubResults![0].Fields!["blockers"]);
        Assert.Equal("service-files", HealthCheckRunner.Normalise(data04).SubResults![0].Key);
    }

    [Fact]
    public void Free_text_in_a_detail_is_dropped_never_laundered_into_a_token()
    {
        var sub = HealthCheckRunner.NormaliseSubResult(new HealthCheckSubResult("Ramesh Kumar", HealthOutcome.Warning, "x y", 1,
            new Dictionary<string, string> { ["customer"] = "Ramesh Kumar", ["path"] = @"C:\Users\x.xlsx", ["phone number"] = "98765 43210", ["codes"] = "R022, R025", ["empty"] = "" }));

        Assert.Equal(HealthCheckRunner.DroppedDetail, sub.Key);
        Assert.Equal("X_Y", sub.FindingCode);
        Assert.Equal(HealthCheckRunner.DroppedDetail, sub.Fields!["customer"]);
        Assert.Equal(HealthCheckRunner.DroppedDetail, sub.Fields["path"]);
        Assert.Equal(HealthCheckRunner.DroppedDetail, sub.Fields[HealthCheckRunner.DroppedDetail + "1"]); // "phone number"
        Assert.Equal("R022+R025", sub.Fields["codes"]);
        Assert.Equal("", sub.Fields["empty"]);
        Assert.DoesNotContain(sub.Fields, field => field.Key.Contains("Ramesh", StringComparison.Ordinal) || field.Value.Contains("Ramesh", StringComparison.Ordinal) || field.Value.Contains("43210", StringComparison.Ordinal));
    }

    // ---- finding 3: Known on the line's own code -------------------------------------------------------------------

    [Fact]
    public void Known_on_the_real_data01_line_code_makes_the_line_known()
    {
        var today = new DateOnly(2026, 10, 11);
        var line = DataProbes.EvaluateCompleteness(Day, new DailyReadinessResult(Day, ["ZZ001"],
        [
            new(DailyReadinessItemKind.MissingExport, "ZZ001", "R025", "message", DailyReadinessDestination.ImportIntake),
            new(DailyReadinessItemKind.MissingManualInput, "ZZ001", "WALK_INS", "message", DailyReadinessDestination.TodayWalkIns)
        ]), TimeSpan.Zero);
        Assert.Equal("DAY_INCOMPLETE", line.FindingCode);

        var known = HealthCheckRunner.ApplyKnown(line, [Known(HealthCheckId.Data01, "DAY_INCOMPLETE")], today);

        Assert.Equal(HealthOutcome.Known, known.Outcome);
        Assert.All(known.SubResults!, sub => Assert.Equal(HealthOutcome.Known, sub.Outcome));
        Assert.Contains("Known until 31 Oct 2026", known.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_on_the_line_code_covers_imp02_and_uncoded_err02_subs_but_never_a_fail_sub()
    {
        var today = new DateOnly(2026, 10, 11);
        var imp02 = ImportProbes.EvaluateFailures([new("R025", "Failed", 2), new("R022", "Unknown layout", 1)], TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Known, HealthCheckRunner.ApplyKnown(imp02, [Known(HealthCheckId.Imp02, "IMPORT_FAILURES_OPEN")], today).Outcome);

        var err02 = new HealthCheckResult(HealthCheckId.Err02, HealthCheckArea.Errors, HealthOutcome.Warning, "NEW_ERRORS", "x", null, TimeSpan.Zero, null,
            [new("REPORT_RUN_FAILED", HealthOutcome.Warning), new("HEALTH_PROBE_FAILED", HealthOutcome.Warning)]);
        Assert.Equal(HealthOutcome.Known, HealthCheckRunner.ApplyKnown(err02, [Known(HealthCheckId.Err02, "NEW_ERRORS")], today).Outcome);

        var withFail = err02 with { SubResults = [new("REPORT_RUN_FAILED", HealthOutcome.Warning), new("X", HealthOutcome.Fail, "CRITICAL")] };
        var result = HealthCheckRunner.ApplyKnown(withFail, [Known(HealthCheckId.Err02, "NEW_ERRORS")], today);
        Assert.Equal(HealthOutcome.Warning, result.Outcome);
        Assert.Equal(HealthOutcome.Fail, result.SubResults![1].Outcome);
    }

    // ---- finding 5: a probe that ignores its token -----------------------------------------------------------------

    [Fact]
    public async Task A_probe_that_ignores_its_token_is_skipped_at_its_allowance()
    {
        var stubborn = new IgnoringProbe([HealthCheckId.Ops03]);
        var options = new HealthCheckRunnerOptions
        {
            TimeProvider = TimeProvider.System,
            Budget = HealthCheckBudget.Default with { AreaCaps = HealthCheckArea.Operations.CapsWith(TimeSpan.FromMilliseconds(300)) },
            AppVersion = "1.11.0.0",
            MachineName = "TESTPC",
            RunByRole = "Owner"
        };
        var watch = Stopwatch.StartNew();

        var run = await new HealthCheckRunner([stubborn], options).RunAsync(HealthCheckTrigger.Manual);

        watch.Stop();
        var line = run.Results.Single(result => result.CheckId == HealthCheckId.Ops03);
        Assert.Equal((HealthOutcome.Skipped, HealthCheckTexts.TimeBudget), (line.Outcome, line.FindingCode));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"The run waited {watch.Elapsed} for a probe that ignores its token.");
        stubborn.Release.SetResult();
    }

    private sealed class IgnoringProbe(IReadOnlyList<string> ids) : IHealthProbe
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> CheckIds => ids;

        public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            await Release.Task; // like a hung COM call: the token is never looked at
            return [];
        }
    }

    private static HealthCheckKnownEntry Known(string checkId, string code) =>
        new(checkId, code, new DateOnly(2026, 10, 31), "Accepted", "owner", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
}
