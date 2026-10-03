using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.4, report audit of 3 October 2026 (FIX-05/FIX-11, decision D1). The daily reporting pack's Invoice Summary
/// and Invoice Lineage sheets carry source_gross_amount (R025 NETAMOUNT, GST-inclusive). They said "Net Value",
/// which reads as the ex-GST NETVALUE, while the workspace exports of the same figure say "Value incl. GST".
/// Wording only: the values and totals are unchanged.
/// </summary>
public sealed class DailyReportingPackLabelTests
{
    private static readonly DailyReportPackSection Section = new("Invoice", ReconciliationStatus.Passed, 236m, 0m, "Invoice summary passed.");

    [Fact]
    public void Invoice_summary_sheet_says_value_incl_GST_and_keeps_the_figures()
    {
        var table = DailyReportingPackService.InvoiceSummaryTable(Section,
        [
            new(new(2026, 8, 25), "WLMHW", "FIRST", "INV", 2m, 354m, 2, "First customer"),
            new(new(2026, 8, 25), "WLMHW", "SECOND", "SR", -1m, -118m, 1, "First customer")
        ]);
        var headers = table.Data.Columns.Select(column => column.Header).ToArray();
        Assert.Equal("Invoice Summary", table.Name);
        Assert.Contains("Value incl. GST", headers);
        Assert.DoesNotContain("Net Value", headers);
        var value = Array.IndexOf(headers, "Value incl. GST");
        Assert.Equal("#,##0.00", table.Data.Columns[value].NumberFormat);
        Assert.Equal([354m, -118m], table.Data.Rows.Select(row => row[value]));
        Assert.Equal(236m, table.Data.Totals![value]);
    }

    [Fact]
    public void Invoice_lineage_sheet_says_value_incl_GST_and_keeps_the_figures()
    {
        var table = DailyReportingPackService.InvoiceLineageTable(Section,
        [
            new(new(2026, 8, 25), "WLMHW", "FIRST", "1", "ITEM-1", "TITAN", "WATCHES", "INV", 1m, 118m, null, "R025.xlsx", "Sheet1", 2),
            new(new(2026, 8, 25), "WLMHW", "FIRST", "2", "ITEM-2", "TITAN", "WATCHES", "INV", 1m, 236m, null, "R025.xlsx", "Sheet1", 3)
        ]);
        var headers = table.Data.Columns.Select(column => column.Header).ToArray();
        Assert.Equal("Invoice Lineage", table.Name);
        Assert.Contains("Value incl. GST", headers);
        Assert.DoesNotContain("Net Value", headers);
        var value = Array.IndexOf(headers, "Value incl. GST");
        Assert.Equal([118m, 236m], table.Data.Rows.Select(row => (decimal?)row[value]));
        Assert.Equal(354m, table.Data.Totals![value]);
    }
}
