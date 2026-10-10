using System.IO;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

// 1.9.9 polish, lane stock: RA-STOCK-13, RA-UI-23, RA-EXPORT-16 (report audit 9 Oct 2026).
public sealed class StockPolishTests
{
    [Fact]
    public void Brand_stock_window_explains_an_empty_grid_and_what_to_do()
    {
        var text = BrandStockEntryWindow.DescribeLoaded(0, "WLMHW", new DateOnly(2026, 9, 28));

        Assert.Contains("No closing-stock snapshot", text, StringComparison.Ordinal);
        Assert.Contains("WLMHW on 28 Sep 2026", text, StringComparison.Ordinal);
        Assert.Contains("Import the Closing Stock export", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Brand_stock_window_status_is_never_blank_when_brands_load()
    {
        var text = BrandStockEntryWindow.DescribeLoaded(12, "HEMW", new DateOnly(2026, 9, 29));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("12 brand(s) for HEMW on 29 Sep 2026", text, StringComparison.Ordinal);
        Assert.Contains("Select a brand", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 12)]
    [InlineData(" 3.5 ", 3.5)]
    [InlineData("1,250", 1250)]
    public void Brand_stock_counts_parse_the_same_on_every_culture(string text, double expected)
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.True(BrandStockEntryWindow.TryParseCount(text, out var value));
            Assert.Equal((decimal)expected, value);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("-1")]
    [InlineData("ten")]
    public void Brand_stock_counts_refuse_blank_negative_or_text(string? text)
        => Assert.False(BrandStockEntryWindow.TryParseCount(text, out _));

    [Theory]
    [InlineData("evening")]
    [InlineData("Evening DSR")]
    [InlineData("evening report")]
    public void Find_a_screen_finds_the_DSR_for_evening(string query)
        => Assert.Contains(TaskNavigation.Search(query, ShellAccess.StoreManager), task => task.ReportCode == "dsr");

    [Fact]
    public void Dsr_keeps_its_existing_aliases()
    {
        var aliases = ReportTaskAliases.For("dsr");
        Assert.Contains("Daily Sales / DSR", aliases);
        Assert.Contains("LY / TY Comparison", aliases);
        Assert.Contains("Evening DSR", aliases);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020))] // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021))] // ERROR_LOCK_VIOLATION
    public void A_locked_file_says_it_is_open_in_another_program(int hresult)
    {
        var text = DesktopFriendlyError.Describe(new IOException("The process cannot access the file 'C:\\private\\Sales.xlsx'.", hresult));

        Assert.Equal(DesktopFriendlyError.FileInUseMessage, text);
        Assert.DoesNotContain("could not be read", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Another_file_failure_no_longer_claims_it_was_a_read()
    {
        var text = DesktopFriendlyError.Describe(new IOException("disk full"));

        Assert.DoesNotContain("could not be read", text, StringComparison.Ordinal);
        Assert.Contains("Close it in other applications", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_keeps_its_own_message()
        => Assert.Equal("The selected file is no longer available. Select it again.",
            DesktopFriendlyError.Describe(new FileNotFoundException("gone")));
}
