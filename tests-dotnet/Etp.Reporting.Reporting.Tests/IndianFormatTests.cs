using System.Globalization;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>UI/UX audit FW rulebook R1-R9 (decision 45 #5): one formatter, Indian grouping, "Sept", plain status words.</summary>
public sealed class IndianFormatTests
{
    [Fact]
    public void Money_is_whole_rupees_with_lakh_grouping_a_real_minus_and_a_zero()
    {
        Assert.Equal("₹12,34,567", IndianFormat.Money(1_234_567m));
        Assert.Equal("−₹1,234", IndianFormat.Money(-1_234m));
        Assert.Equal("₹0", IndianFormat.Money(0m));
        Assert.Equal("—", IndianFormat.Money(null));
        Assert.Equal("₹12,34,567.50", IndianFormat.MoneyExact(1_234_567.5m));
    }

    [Fact]
    public void Money_cells_keep_lakh_grouping_for_negative_values_and_copy_into_Excel()
    {
        Assert.Equal("12,34,567.00", IndianFormat.MoneyCell(1_234_567m));
        Assert.Equal("-1,50,000.00", IndianFormat.MoneyCell(-150_000m));
        Assert.Equal("0.00", IndianFormat.MoneyCell(0m));
        Assert.Equal("Value incl. GST (₹)", IndianFormat.MoneyHeader("Value incl. GST"));
        Assert.Equal("Value incl. GST (₹)", IndianFormat.MoneyHeader("Value incl. GST (₹)"));
    }

    [Fact]
    public void Compact_money_uses_L_and_Cr_above_one_lakh()
    {
        Assert.Equal("₹17.96 L", IndianFormat.MoneyCompact(1_795_528m));
        Assert.Equal("₹1.03 Cr", IndianFormat.MoneyCompact(10_254_306m));
        Assert.Equal("₹99,999", IndianFormat.MoneyCompact(99_999m));
        Assert.Equal("−₹2.50 L", IndianFormat.MoneyCompact(-250_000m));
    }

    [Fact]
    public void Quantities_counts_and_percentages_follow_R4_and_R5()
    {
        Assert.Equal("1,234", IndianFormat.Quantity(1234m));
        Assert.Equal("12,345.50", IndianFormat.Quantity(12_345.5m, 2));
        Assert.Equal("12,34,567", IndianFormat.Count(1_234_567));
        Assert.Equal("1 invoice", IndianFormat.Plural(1, "invoice"));
        Assert.Equal("1,234 invoices", IndianFormat.Plural(1234, "invoice"));
        Assert.Equal("12.5%", IndianFormat.Percent(12.54m));
        Assert.Equal("+12.5%", IndianFormat.Percent(12.54m, signed: true));
        Assert.Equal("-3.2%", IndianFormat.Percent(-3.2m, signed: true));
        Assert.Equal("+0.0%", IndianFormat.Percent(0m, signed: true));
        Assert.Equal("—", IndianFormat.Percent(null));
    }

    [Fact]
    public void Dates_say_Sept_and_times_are_24_hour()
    {
        Assert.Equal("07 Sept 2026", IndianFormat.Date(new DateOnly(2026, 9, 7)));
        Assert.Equal("08 Oct 2026", IndianFormat.Date(new DateOnly(2026, 10, 8)));
        Assert.Equal("Sept 2026", IndianFormat.Month(new DateOnly(2026, 9, 1)));
        Assert.Equal("Mon 07 Sept 2026", IndianFormat.DateWithDay(new DateOnly(2026, 9, 7)));
        Assert.Equal("01 Sept 2026 – 07 Sept 2026", IndianFormat.DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7)));
        Assert.Equal("18:05", IndianFormat.Time(new DateTime(2026, 9, 7, 18, 5, 0)));
        Assert.Equal("07 Sept 2026 18:05", IndianFormat.DateAndTime(new DateTime(2026, 9, 7, 18, 5, 0)));
        Assert.Equal("—", IndianFormat.Date((DateOnly?)null));
    }

    [Fact]
    public void Sentences_do_not_depend_on_the_thread_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Total 12,34,567.89 on 07 Sept 2026", IndianFormat.Text($"Total {1_234_567.891m:N2} on {new DateOnly(2026, 9, 7):dd MMM yyyy}"));
            Assert.Equal("₹12,34,567", IndianFormat.Money(1_234_567m));
            Assert.Equal("07 Sept 2026", IndianFormat.Date(new DateOnly(2026, 9, 7)));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Metric_picks_the_format_by_kind()
    {
        Assert.Equal("1,23,456.00", IndianFormat.Metric(123_456m, "currency"));
        Assert.Equal("12.5%", IndianFormat.Metric(12.5m, "percent"));
        Assert.Equal("1.50", IndianFormat.Metric(1.5m, "ratio"));
        Assert.Equal("12", IndianFormat.Metric(12m, null));
        Assert.Equal("1.25", IndianFormat.Metric(1.25m, null));
        Assert.Equal("—", IndianFormat.Metric(null, "currency"));
    }

    [Theory]
    [InlineData("Passed", "OK")]
    [InlineData("PASS", "OK")]
    [InlineData("Warning", "Check")]
    [InlineData("Failed", "Problem")]
    [InlineData("Blocked", "Cannot run")]
    [InlineData("NotRun", "Not checked yet")]
    [InlineData("Known", "Known")]
    [InlineData("Skipped", "Skipped")]
    [InlineData("Accepted", "Accepted")]
    public void Status_words_follow_decision_45(string status, string label) => Assert.Equal(label, StatusWords.Label(status));

    [Fact]
    public void Status_enums_read_as_the_same_words() =>
        Assert.Equal(["OK", "Problem", "Cannot run", "Not checked yet"],
            new[] { ReconciliationStatus.Passed, ReconciliationStatus.Failed, ReconciliationStatus.Blocked, ReconciliationStatus.NotRun }.Select(x => StatusWords.Label(x)).ToArray());
}
