using System.Globalization;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class DailySalesReportTests
{
    [Fact]
    public void Formulas_use_safe_business_definitions()
    {
        var growth = new ManagementMetricEngine().Growth(145_970m, 69_444m);
        var conversion = new ManagementMetricEngine().Conversion(11, 14m);
        var achievement = DailySalesReportBuilder.Achievement(1_795_528m, 2_900_000m);

        Assert.Equal(110.2m, decimal.Round(growth.Value!.Value, 1));
        Assert.Equal(78.6m, decimal.Round(conversion.Value!.Value, 1));
        Assert.Equal(61.9m, decimal.Round(achievement.Value!.Value, 1));
    }

    [Fact]
    public void Missing_and_zero_denominators_display_na()
    {
        var missing = DailySalesReportBuilder.Achievement(10m, null);
        var zero = DailySalesReportBuilder.Achievement(10m, 0m);

        Assert.Equal(MetricAvailability.MissingSource, missing.Availability);
        Assert.Equal(MetricAvailability.NotApplicable, zero.Availability);
        Assert.Equal("N/A", DsrDisplay.Percent(missing));
        Assert.Equal("N/A", DsrDisplay.Percent(zero));
    }

    [Fact]
    public void Progress_fill_is_capped_but_display_retains_true_percentage()
    {
        var target = new DsrTargetProgress("WLMHW", "Titan World", "#2269E8", 120m, 100m,
            DailySalesReportBuilder.Achievement(120m, 100m));

        Assert.Equal(100m, target.FillPercent);
        Assert.Equal("+120.0%", DsrDisplay.Percent(target.Achievement));
    }

    [Fact]
    public void Weekday_is_derived_from_the_business_date()
    {
        Assert.Equal("Monday", EmptyDocument(new DateOnly(2026, 8, 24)).Weekday());
        Assert.Equal("Tuesday", EmptyDocument(new DateOnly(2026, 8, 25)).Weekday());
    }

    [Fact]
    public void Indian_currency_and_compact_formatting_are_used()
    {
        Assert.Equal("₹1,45,970", DsrDisplay.Currency(145_970m));
        Assert.Equal("₹17.96 L", DsrDisplay.CompactCurrency(1_795_528m));
        Assert.Equal("₹1.03 Cr", DsrDisplay.CompactCurrency(10_254_306m));
        Assert.Equal("— / —", DsrDisplay.ValueQuantity(null, null));
    }

    [Fact]
    public void Missing_ly_mtd_is_explicit_and_never_fabricated()
    {
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), [],
            new Dictionary<string, decimal?> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m });

        Assert.All(document.Stores, store =>
        {
            var mtd = Assert.Single(store.Periods, period => period.Period == "MTD");
            Assert.Null(mtd.LyValue);
            Assert.Null(mtd.LyQuantity);
            Assert.Equal("LY MTD source required", mtd.MissingSourceNote);
            Assert.Equal("— / —", DsrDisplay.ValueQuantity(mtd.LyValue, mtd.LyQuantity));
        });
    }

    [Fact]
    public void Approved_fixture_contains_all_required_report_sections()
    {
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), [],
            new Dictionary<string, decimal?> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m });

        Assert.Equal(["Titan World", "Helios"], document.Stores.Select(x => x.DisplayName));
        Assert.All(document.Stores, x => Assert.Equal(["FTD", "MTD", "YTD"], x.Periods.Select(p => p.Period)));
        Assert.Equal(["Titan World", "Helios", "Combined"], document.Targets.Select(x => x.DisplayName));
        Assert.Equal(11, document.CombinedInvoices);
        Assert.Equal(14m, document.WalkIns);
    }

    private static DailySalesReportDocument EmptyDocument(DateOnly date) => new(date, "Daily Sales Report (DSR)", "",
        null, new(null, MetricAvailability.MissingSource, ""), null, null, null,
        new(null, MetricAvailability.MissingSource, ""), null, new(null, MetricAvailability.MissingSource, ""), null,
        new(null, MetricAvailability.MissingSource, ""), [], new(null, null, null, null, null,
            new Dictionary<string, decimal?>()), [], "test");

    private static DsrPeriodFact[] SalesFacts() =>
    [
        new("FTD", "WLMHW", 69_880m, 22_647m, 8m, 6m, 8, 6, 1m, 8_735m, 10m, null),
        new("MTD", "WLMHW", 973_860m, null, 199m, null, 184, null, 1.08m, 5_293m, null, null),
        new("YTD", "WLMHW", 6_703_290m, 4_890_060m, 1_325m, 1_116m, 1_250, 1_053, 1.06m, 5_363m, null, null),
        new("FTD", "HEMW", 76_090m, 46_797m, 3m, 3m, 3, 3, 1m, 25_363m, 4m, null),
        new("MTD", "HEMW", 821_668m, null, 40m, null, 37, null, 1.08m, 22_207m, null, null),
        new("YTD", "HEMW", 3_551_016m, 2_216_848m, 192m, 133m, 181, 125, 1.06m, 19_619m, null, null),
        new("FTD", "COMBINED", 145_970m, 69_444m, 11m, 9m, 11, 9, 1m, 13_270m, 14m, null),
        new("MTD", "COMBINED", 1_795_528m, null, 239m, null, 221, null, 1.08m, 8_125m, null, null),
        new("YTD", "COMBINED", 10_254_306m, 7_106_908m, 1_517m, 1_249m, 1_431, 1_178, 1.06m, 7_166m, null, null)
    ];
}
