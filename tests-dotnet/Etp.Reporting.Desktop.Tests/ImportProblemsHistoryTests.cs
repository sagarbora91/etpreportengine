using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;

namespace Etp.Reporting.Desktop.Tests;

// R-WLMHW-09 (report audit, 3 Oct 2026): in All stores, another store's clean import of a
// file with the same name hid this store's failure (6 WLMHW problems in store scope, 3 in All stores).
public sealed class ImportProblemsHistoryTests
{
    private static readonly DateTime Failed = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Another_stores_clean_import_of_the_same_file_name_does_not_hide_a_failure()
    {
        var problems = ImportProblems.FromHistory(
        [
            (Failed, Result("R022_Revenue_Report.xlsx", "R022", "WLMHW", "Failed")),
            (Failed.AddHours(1), Result("R022_Revenue_Report.xlsx", "R022", "HEMW", "Imported"))
        ]);

        var problem = Assert.Single(problems);
        Assert.Equal("WLMHW", problem.Store);
        Assert.Equal("Failed", problem.Status);
    }

    [Fact]
    public void A_clean_import_of_another_report_with_the_same_file_name_does_not_hide_a_failure()
    {
        var problems = ImportProblems.FromHistory(
        [
            (Failed, Result("export.xlsx", "R022", "WLMHW", "Failed")),
            (Failed.AddHours(1), Result("export.xlsx", "R025", "WLMHW", "Imported"))
        ]);

        Assert.Equal("WLMHW", Assert.Single(problems).Store);
    }

    [Fact]
    public void The_same_stores_later_clean_import_still_clears_its_failure()
    {
        Assert.Empty(ImportProblems.FromHistory(
        [
            (Failed, Result("R022_Revenue_Report.xlsx", "R022", "WLMHW", "Failed")),
            (Failed.AddHours(1), Result("r022_revenue_report.xlsx", "R022", "wlmhw", "Imported"))
        ]));
    }

    [Fact]
    public void A_failure_that_never_learned_its_store_is_cleared_by_the_retry_of_the_same_bytes()
    {
        Assert.Empty(ImportProblems.FromHistory(
        [
            (Failed, Result("R022_Revenue_Report.xlsx", null, null, "Failed") with { SourceSha256 = "ABC123" }),
            (Failed.AddHours(1), Result("R022_Revenue_Report.xlsx", "R022", "WLMHW", "Imported") with { SourceSha256 = "abc123" })
        ]));
    }

    // Review of FIX-09: a failure with no store shows in every store scope, but another store's clean
    // row shows only in that store's scope and All stores, so it must not clear the failure.
    [Fact]
    public void Another_stores_clean_file_does_not_clear_a_failure_that_never_learned_its_store()
    {
        var history = new (DateTime, FolderImportFileResult)[]
        {
            (Failed, Result("R022_Revenue_Report.xlsx", null, null, "Failed") with { SourceSha256 = "wlmhw-bytes" }),
            (Failed.AddHours(1), Result("R022_Revenue_Report.xlsx", "R022", "HEMW", "Imported") with { SourceSha256 = "hemw-bytes" })
        };
        var allStores = ImportProblems.FromHistory(history);
        // The store-scoped history (WLMHW selected) keeps the null-store failure and drops HEMW's row.
        var wlmhwScope = ImportProblems.FromHistory(history.Where(entry => entry.Item2.StoreCode is null or "WLMHW"));

        Assert.Single(allStores);
        Assert.Equal(wlmhwScope.Count, allStores.Count);
    }

    [Fact]
    public void A_failure_that_never_learned_its_store_and_has_no_hash_is_not_cleared_by_a_stores_clean_file()
    {
        Assert.Single(ImportProblems.FromHistory(
        [
            (Failed, Result("R022_Revenue_Report.xlsx", null, null, "Failed")),
            (Failed.AddHours(1), Result("R022_Revenue_Report.xlsx", "R022", "HEMW", "Imported"))
        ]));
    }

    [Fact]
    public void A_clean_import_before_the_failure_does_not_clear_it()
    {
        var problems = ImportProblems.FromHistory(
        [
            (Failed.AddHours(-1), Result("R022_Revenue_Report.xlsx", "R022", "WLMHW", "Imported")),
            (Failed, Result("R022_Revenue_Report.xlsx", "R022", "WLMHW", "Failed"))
        ]);

        Assert.Single(problems);
    }

    private static FolderImportFileResult Result(string file, string? report, string? store, string status) =>
        new(file, report, store, new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), status);
}
