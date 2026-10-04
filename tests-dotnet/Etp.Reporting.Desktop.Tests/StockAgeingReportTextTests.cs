using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>Owner answer Q8: the Slow / Exception list keeps NEW items and says how many there are.</summary>
public sealed class StockAgeingReportTextTests
{
    private static StockInventoryRecord Row(string status) =>
        new(new(2026, 8, 25), "HEMW", "ITEM", "BRAND", "CLUSTER", 1m, null, null, null, null, status);

    [Fact]
    public void Status_line_counts_new_items()
    {
        Assert.Equal(" 2 NEW (received in the last 60 days).", ReportsWorkspaceView.NewStockText([Row("NEW"), Row("NEW"), Row("NEVER SOLD")]));
        Assert.Equal("", ReportsWorkspaceView.NewStockText([Row("NEVER SOLD")]));
    }

    [Fact]
    public void Export_note_explains_new()
    {
        Assert.Contains("received in the last 60 days and not sold since is NEW", ReportsWorkspaceView.ReceiptNote, StringComparison.Ordinal);
        Assert.DoesNotContain("cost", ReportsWorkspaceView.ReceiptNote, StringComparison.OrdinalIgnoreCase);
    }
}
