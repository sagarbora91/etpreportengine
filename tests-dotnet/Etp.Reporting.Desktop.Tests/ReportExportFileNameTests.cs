using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>RA-EXPORT-03: the proposed Excel and PDF file names never hold a character Windows rejects.</summary>
public sealed class ReportExportFileNameTests
{
    [Theory]
    [InlineData("Slow / Exception Stock", "Slow___Exception_Stock")]
    [InlineData("Daily Sales Report", "Daily_Sales_Report")]
    [InlineData("Closing Stock", "Closing_Stock")]
    public void Report_name_becomes_a_safe_file_name(string reportName, string expected)
    {
        var safe = ReportsWorkspaceView.SafeFileName(reportName);
        Assert.Equal(expected, safe);
        Assert.Equal(-1, safe.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.DoesNotContain(' ', safe);
    }

    [Fact]
    public void Every_invalid_file_name_character_is_replaced()
    {
        var name = "A" + new string(Path.GetInvalidFileNameChars()) + "B";
        var safe = ReportsWorkspaceView.SafeFileName(name);
        Assert.Equal(-1, safe.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.Equal(name.Length, safe.Length);
        Assert.StartsWith("A", safe);
        Assert.EndsWith("B", safe);
    }

    // 1.9.8 (RA-EXPORT-01/03): the name the Save dialog proposes - safe report name, From and To as yyyyMMdd, the
    // format's extension - built once for Excel and PDF.
    [Theory]
    [InlineData("Slow / Exception Stock", false, "Slow___Exception_Stock_20260901_20260930.xlsx")]
    [InlineData("Slow / Exception Stock", true, "Slow___Exception_Stock_20260901_20260930.pdf")]
    [InlineData("Daily Sales Report", true, "Daily_Sales_Report_20260901_20260930.pdf")]
    [InlineData("Brand Sales", false, "Brand_Sales_20260901_20260930.xlsx")]
    public void Proposed_file_name_carries_the_safe_name_the_window_and_the_extension(string reportName, bool pdf, string expected)
    {
        var metadata = new ExcelReportMetadata(reportName, new(2026, 9, 1), new(2026, 9, 30), "Passed", "v1", "x", DateTimeOffset.UtcNow);
        var name = ReportsWorkspaceView.ProposedFileName(metadata, pdf);
        Assert.Equal(expected, name);
        Assert.Equal(-1, name.IndexOfAny(Path.GetInvalidFileNameChars()));
    }

    [Fact]
    public void Proposed_file_name_for_a_single_day_repeats_the_day()
    {
        var metadata = new ExcelReportMetadata("Daily Exceptions", new(2026, 8, 25), new(2026, 8, 25), "Passed", "v1", "x", DateTimeOffset.UtcNow);
        Assert.Equal("Daily_Exceptions_20260825_20260825.xlsx", ReportsWorkspaceView.ProposedFileName(metadata, pdf: false));
    }
}
