using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.12.1 lane M2 (money test gaps #11 and #16). #11: whole-rupee display rounding at the midpoint and near zero.
/// #16: a store marked as a no-sale day must also give the combined growth, invoices and the COMBINED evening sheet
/// (today only CombinedFtd and Units are recomputed). Synthetic store codes only.
/// </summary>
public sealed class MoneyDisplayAndNoSaleTests
{
    // ---- gap #11 ----

    [Theory]
    [InlineData("22647.50", "₹22,648")]     // midpoint rounds away from zero: the sheet drops the paise, ETP shows 22,648
    [InlineData("5380.45", "₹5,380")]       // below the midpoint rounds down
    [InlineData("0.50", "₹1")]
    [InlineData("0.49", "₹0")]
    [InlineData("-0.40", "₹0")]             // a tiny negative reads zero, no minus sign
    [InlineData("-0.50", "−₹1")]            // the midpoint on the negative side rounds away from zero
    [InlineData("-1234.00", "−₹1,234")]
    [InlineData("99999.50", "₹1,00,000")]   // Indian grouping: 1,00,000
    public void Money_rounds_half_away_from_zero_and_tiny_negatives_read_zero(string amount, string expected) =>
        Assert.Equal(expected, IndianFormat.Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("99999.50", "₹1,00,000")]   // below the lakh threshold on the raw value: stays in the whole-rupee form
    [InlineData("99999.00", "₹99,999")]
    [InlineData("100000", "₹1.00 L")]
    [InlineData("1795530.60", "₹17.96 L")]  // 17.9553 L
    [InlineData("-150000", "−₹1.50 L")]
    [InlineData("10000000", "₹1.00 Cr")]
    public void Compact_money_switches_at_the_lakh_and_the_crore_on_the_raw_value(string amount, string expected) =>
        Assert.Equal(expected, IndianFormat.MoneyCompact(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("208.555", true, "+208.6%")]
    [InlineData("12.25", false, "12.3%")]
    [InlineData("-12.25", true, "-12.3%")]
    [InlineData("-0.04", true, "+0.0%")]    // rounds to 0.0, no minus sign
    [InlineData("0", true, "+0.0%")]
    public void Percent_has_one_decimal_rounded_away_from_zero(string value, bool signed, string expected) =>
        Assert.Equal(expected, IndianFormat.Percent(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), signed));

    // ---- gap #16 ----

    private static readonly DateOnly Day = new(2026, 8, 21);
    private const string Line = "No sales (marked by manager on 21 Aug 2026)";

    private static DailySalesReportDocument Document()
    {
        var sales = new List<DsrPeriodFact>
        {
            // ZZ001 sold 1,000 on 2 invoices (LY 900); ZZ002 has no sales file for the day (LY 700 on 1 invoice). Combined LY 1,600 on 3.
            new("FTD", "ZZ001", 1_000m, 900m, 4m, 3m, 2, 2, 2m, 500m, 10m, 20m),
            new("MTD", "ZZ001", 9_000m, 8_000m, 30m, 25m, 20, 18, 1.5m, 450m, 100m, 20m),
            new("FTD", "ZZ002", null, 700m, null, 2m, null, 1, null, null, null, null),
            new("MTD", "ZZ002", 5_000m, 4_000m, 12m, 10m, 9, 8, 1.3m, 555m, 60m, 15m),
            new("FTD", "COMBINED", null, 1_600m, null, 5m, null, 3, null, null, null, null)
        };
        var document = DailySalesReportBuilder.Build(Day, sales, [], new Dictionary<string, decimal?>(),
            stores: [new("ZZ001", "Shop one"), new("ZZ002", "Shop two")]);
        return document with
        {
            EveningSheets =
            [
                new("ZZ001", "Shop one", null, null, null, null, [new("VALUE", 1_000m, 900m, 11m, 9_000m, 20_000m, 18_000m, "currency"), new("INVOICE", 2m, 2m, 0m, 20m, 40m, 36m)]),
                new("ZZ002", "Shop two", null, null, null, null, [new("VALUE", null, 700m, null, 5_000m, 9_000m, 8_000m, "currency"), new("INVOICE", null, 1m, null, 9m, 15m, 14m)]),
                new("COMBINED", "Combined", null, null, null, null, [new("VALUE", null, 1_600m, null, 14_000m, 29_000m, 26_000m, "currency"), new("INVOICE", null, 3m, null, 29m, 55m, 50m)])
            ]
        };
    }

    [Fact]
    public void A_marked_store_also_gives_combined_growth_invoices_and_the_combined_sheet()
    {
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(Document(), [new("ZZ002", Line)]);

        // Already done today: ZZ001 1,000 + ZZ002 a real 0.
        Assert.Equal(1_000m, applied.CombinedFtd);
        // 1,000 against the combined LY 1,600: (1,000 / 1,600 - 1) * 100 = -37.5.
        Assert.Equal(-37.5m, applied.CombinedFtdGrowth.Value);
        Assert.Equal(MetricAvailability.Available, applied.CombinedFtdGrowth.Availability);
        // Invoices: ZZ001 2 + ZZ002 a real 0.
        Assert.Equal(2, applied.CombinedInvoices);
        // The top tile and the COMBINED matrix agree.
        var combined = Assert.Single(applied.EveningSheets, x => x.StoreCode == "COMBINED");
        Assert.Equal(1_000m, combined.Rows.Single(x => x.Metric == "VALUE").Ftd);
        Assert.Equal(2m, combined.Rows.Single(x => x.Metric == "INVOICE").Ftd);
        // LY, MTD and YTD of the COMBINED sheet are not touched.
        Assert.Equal(1_600m, combined.Rows.Single(x => x.Metric == "VALUE").Ly);
        Assert.Equal(14_000m, combined.Rows.Single(x => x.Metric == "VALUE").Mtd);
    }

    [Fact]
    public void A_marked_stores_own_sheet_reads_zero_and_the_tile_is_known()
    {
        // Pins what holds both before and after F1: the tile is recomputed and the marked store's own sheet reads zero.
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(Document(), [new("ZZ002", Line)]);

        Assert.Equal(1_000m, applied.CombinedFtd);
        Assert.Equal(0m, applied.EveningSheets.Single(x => x.StoreCode == "ZZ002").Rows.Single(x => x.Metric == "VALUE").Ftd);
        Assert.Equal(0m, applied.EveningSheets.Single(x => x.StoreCode == "ZZ002").Rows.Single(x => x.Metric == "INVOICE").Ftd);
    }
}
