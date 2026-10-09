using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.9.7 report audit: the status line names a store whose source is not imported (RA-SALES-01, RA-STOCK-03 / RA-UI-08).</summary>
public sealed class ReportCoverageTextTests
{
    private static DsrManagementRecord Dsr(string period, string store, decimal? tySales, DateOnly? coversTo) =>
        new(period, store, new(2026, 10, 8), new(2026, 10, 8), tySales, null, null, "Missing", null, null, null, null, null, null, null, null, "policy", 0, null, null, coversTo);

    [Fact]
    public void Dsr_is_blocked_naming_each_store_whose_business_date_has_no_r025_and_its_last_imported_day()
    {
        var rows = new[]
        {
            Dsr("FTD", "WLMHW", null, new(2026, 9, 29)), Dsr("FTD", "HEMW", null, new(2026, 9, 28)), Dsr("FTD", "COMBINED", null, null),
            Dsr("MTD", "WLMHW", null, new(2026, 9, 29)), Dsr("YTD", "WLMHW", 8_242_572.10m, new(2026, 9, 29))
        };

        Assert.Equal("R025 not imported for 08 Oct 2026 (HEMW: last 28 Sep 2026; WLMHW: last 29 Sep 2026). Import the sales export (R025) for that date; FTD and MTD are blank, not zero.",
            ReportsWorkspaceView.DsrCoverageBlock(rows, new(2026, 10, 8)));
    }

    [Fact]
    public void Dsr_names_only_the_uncovered_store_and_says_none_imported_when_the_store_has_no_r025_at_all()
    {
        var rows = new[] { Dsr("FTD", "WLMHW", 46_095m, new(2026, 9, 29)), Dsr("FTD", "HEMW", null, null), Dsr("FTD", "COMBINED", null, null) };

        Assert.Equal("R025 not imported for 28 Sep 2026 (HEMW: none imported). Import the sales export (R025) for that date; FTD and MTD are blank, not zero.",
            ReportsWorkspaceView.DsrCoverageBlock(rows, new(2026, 9, 28)));
    }

    [Fact]
    public void Dsr_with_every_store_covered_is_not_blocked_even_when_a_store_sold_nothing()
    {
        var rows = new[] { Dsr("FTD", "WLMHW", 46_095m, new(2026, 9, 29)), Dsr("FTD", "HEMW", 0m, new(2026, 9, 28)), Dsr("FTD", "COMBINED", 46_095m, null) };

        Assert.Null(ReportsWorkspaceView.DsrCoverageBlock(rows, new(2026, 9, 28)));
    }

    [Fact]
    public void Stock_movement_names_the_scoped_stores_that_have_no_ledger_rows_in_the_period()
    {
        Assert.Equal(" No stock ledger rows for WLMHW between 01 Sep 2026 and 28 Sep 2026; import the stock ledger for those dates, or the store had no movement.",
            ReportsWorkspaceView.MissingMovementText(["WLMHW", "HEMW"], ["HEMW"], new(2026, 9, 1), new(2026, 9, 28)));
        Assert.Equal("", ReportsWorkspaceView.MissingMovementText(["WLMHW", "HEMW"], ["hemw", "WLMHW"], new(2026, 9, 1), new(2026, 9, 28)));
        Assert.Equal("", ReportsWorkspaceView.MissingMovementText([], [], new(2026, 9, 1), new(2026, 9, 28)));
    }
}
