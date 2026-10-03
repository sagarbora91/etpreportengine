using System.Text.Json;
using Etp.Reporting.Import.Audit;

namespace Etp.Reporting.Import.Tests;

/// <summary>The expectation file (design 7.3, 12.2 ExpectationTests).</summary>
public sealed class AuditExpectationTests
{
    private static AuditExpectations Synthetic() =>
        AuditExpectations.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "import-audit", "expect-held-titan-synthetic.json"));

    private static AuditFileReport File(string store, string report, AuditPlannerOneReport? planner = null,
        IReadOnlyList<AuditSnapshotDateReport>? snapshots = null) => new()
    {
        File = $"{store}/{report}.xlsx", Store = store, ReportCode = report == "R030" ? "STOCK_LEDGER" : report, FamilyCode = report,
        Outcome = "Ready", Planner1 = planner, SnapshotDates = snapshots ?? []
    };

    [Fact]
    public void The_synthetic_file_parses()
    {
        var expectations = Synthetic();

        Assert.Equal("held-titan-synthetic", expectations.Name);
        Assert.Equal(4, expectations.Files.Count);
        Assert.Equal(0, expectations.UnexpectedBlockers);
        Assert.Equal(["IMPORT_LEGACY_RESTATEMENT_REQUIRED"], expectations.LegacyHoldsAllowed);
    }

    [Fact]
    public void Matching_predictions_pass()
    {
        var files = new[]
        {
            File("WLMHW", "R030", new("Imported") { Rows = new(1, 7, 0) }),
            File("WLMHW", "R022", new("Imported") { Rows = new(10, 0, 0), PromotesOver = [10078] }),
            File("WLMHW", "R010", new("Imported") { SnapshotRows = new Dictionary<string, int> { ["2026-07-02"] = 3, ["2026-08-07"] = 3, ["2026-09-29"] = 2 } })
        };

        var (result, missing) = Synthetic().Apply(files);

        Assert.Empty(missing);
        Assert.All(result, file => Assert.True(file.Expectation!.Pass, string.Join("; ", file.Expectation.Mismatches)));
    }

    [Fact]
    public void Every_field_mismatch_is_reported_by_name()
    {
        var (result, _) = Synthetic().Apply([File("WLMHW", "R030", new("Failed") { Rows = new(0, 6, 2) })]);

        var mismatches = Assert.Single(result).Expectation!.Mismatches;
        Assert.Contains(mismatches, text => text.StartsWith("planner1.result:", StringComparison.Ordinal));
        Assert.Contains(mismatches, text => text.StartsWith("planner1.new:", StringComparison.Ordinal));
        Assert.Contains(mismatches, text => text.StartsWith("planner1.present:", StringComparison.Ordinal));
        Assert.Contains(mismatches, text => text.StartsWith("planner1.conflict:", StringComparison.Ordinal));
    }

    [Fact]
    public void An_omitted_field_is_not_checked()
    {
        // The R022 entry names no new or present count.
        var (result, _) = Synthetic().Apply([File("WLMHW", "R022", new("Imported") { Rows = new(999, 999, 0), PromotesOver = [10078] })]);

        Assert.True(Assert.Single(result).Expectation!.Pass);
    }

    [Fact]
    public void A_forbidden_file_is_a_finding()
    {
        var (result, _) = Synthetic().Apply([File("HEMW", "R010", new("Imported"))]);

        var file = Assert.Single(result);
        Assert.False(file.Expectation!.Pass);
        Assert.Equal(AuditVerdict.Findings, AuditReportBuilder.Verdict(file, blocked: false, strict: false));
        Assert.Equal(AuditExitCodes.Findings, AuditReportBuilder.Summary([file with { Verdict = AuditVerdict.Findings }], 0).ExitCode);
    }

    [Fact]
    public void Snapshot_counts_are_compared_per_date()
    {
        var (result, _) = Synthetic().Apply([File("WLMHW", "R010", new("Imported") { SnapshotRows = new Dictionary<string, int> { ["2026-07-02"] = 3, ["2026-08-07"] = 4, ["2026-09-29"] = 2 } })]);

        Assert.Equal(["snapshots.2026-08-07: expected 3, predicted 4"], Assert.Single(result).Expectation!.Mismatches);
    }

    [Fact]
    public void An_entry_that_matches_no_file_is_a_run_finding()
    {
        var (_, missing) = Synthetic().Apply([File("WLMHW", "R030", new("Imported") { Rows = new(1, 7, 0) })]);

        Assert.Equal(2, missing.Count);
    }

    [Fact]
    public void Unexpected_blockers_are_counted_unless_allowed()
    {
        var blocked = File("WLMHW", "R025", new("Failed") { Code = "IMPORT_PERIOD_ALREADY_PRESENT" }) with { Verdict = AuditVerdict.Blocked };
        var allowed = File("WLMHW", "R024", new("Failed") { Code = "IMPORT_LEGACY_RESTATEMENT_REQUIRED" }) with { Verdict = AuditVerdict.Blocked };
        var expectations = Synthetic();

        var summary = AuditReportBuilder.Summary([blocked, allowed], 0, expectedBlocker: expectations.AllowsBlocker);

        Assert.Equal(1, summary.UnexpectedBlockers);
        Assert.Equal(AuditExitCodes.Findings, summary.ExitCode);
    }

    [Theory]
    [InlineData("""{ "schema": "etp-import-audit-expect/2", "files": [] }""")]
    [InlineData("""{ "files": [] }""")]
    [InlineData("""[]""")]
    public void A_wrong_schema_is_an_input_error(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<AuditInputException>(() => AuditExpectations.Parse(document.RootElement));
    }

    [Fact]
    public void An_unreadable_file_is_an_input_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-expect-{Guid.NewGuid():N}.json");
        System.IO.File.WriteAllText(path, "{ not json");
        try { Assert.Throws<AuditInputException>(() => AuditExpectations.Load(path)); }
        finally { System.IO.File.Delete(path); }
    }
}
