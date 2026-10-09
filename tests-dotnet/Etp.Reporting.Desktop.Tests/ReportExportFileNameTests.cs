using Etp.Reporting.Desktop.Modules.Reports;

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
}
