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
        Assert.Equal("—", DsrDisplay.Percent(missing));
        Assert.Equal("—", DsrDisplay.Percent(zero));
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
            new Dictionary<string, decimal?> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m }, stores: [new("WLMHW","Titan World"),new("HEMW","Helios")]);

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
            new Dictionary<string, decimal?> { ["WLMHW"] = 1_600_000m, ["HEMW"] = 1_300_000m }, stores: [new("WLMHW","Titan World"),new("HEMW","Helios")]);

        Assert.Equal(["Titan World", "Helios"], document.Stores.Select(x => x.DisplayName));
        Assert.All(document.Stores, x => Assert.Equal(["FTD", "MTD", "YTD"], x.Periods.Select(p => p.Period)));
        Assert.Equal(["Titan World", "Helios", "Combined"], document.Targets.Select(x => x.DisplayName));
        Assert.Equal(11, document.CombinedInvoices);
        Assert.Equal(14m, document.WalkIns);
        Assert.Equal(78.6m, decimal.Round(document.Conversion.Value!.Value, 1));
        var cards = DsrKpiCards.For(document);
        Assert.Equal(["COMBINED FTD", "UNITS", "WALK-INS", "CONVERSION", "MTD SALES", "YTD SALES"], cards.Select(x => x.Label));
        Assert.Equal(new DsrKpiCard("WALK-INS", "14", "Titan World 10 · Helios 4"), cards[2]);
        Assert.Equal(new DsrKpiCard("CONVERSION", "78.6%", "11 invoices / 14 walk-ins"), cards[3]);
    }

    // HEMW FIX-03 / WLMHW FIX-06 (report audit 3 Oct 2026): walk-ins not entered are "Data not available", never 0.
    [Fact]
    public void Walk_ins_not_entered_show_data_not_available_on_the_kpi_cards()
    {
        var facts = SalesFacts().Select(x => x with { WalkIns = null, ConversionPercent = null }).ToArray();
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), facts, [],
            new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")]);

        Assert.Null(document.WalkIns);
        Assert.All(document.Stores, x => Assert.Null(x.FtdWalkIns));
        Assert.Null(document.Conversion.Value);
        Assert.Equal(MetricAvailability.MissingInput, document.Conversion.Availability);
        var cards = DsrKpiCards.For(document);
        Assert.Equal(new DsrKpiCard("WALK-INS", "—", "Data not available"), cards[2]);
        Assert.Equal(new DsrKpiCard("CONVERSION", "—", "Data not available"), cards[3]);
    }

    [Fact]
    public void One_store_without_walk_ins_makes_combined_walk_ins_and_conversion_unavailable()
    {
        // COMBINED carries the 1.9.3 plain sum (Titan 10 + Helios nothing = 10); the document must not show it.
        var facts = SalesFacts().Select(x => x.Period == "FTD" && x.StoreCode == "HEMW" ? x with { WalkIns = null }
            : x.Period == "FTD" && x.StoreCode == "COMBINED" ? x with { WalkIns = 10m } : x).ToArray();
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), facts, [],
            new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")]);

        Assert.Null(document.WalkIns);
        Assert.Equal(10m, document.Stores.Single(x => x.StoreCode == "WLMHW").FtdWalkIns);
        Assert.Null(document.Conversion.Value);
        var cards = DsrKpiCards.For(document);
        Assert.Equal(new DsrKpiCard("WALK-INS", "—", "Data not available"), cards[2]);
        Assert.Equal(new DsrKpiCard("CONVERSION", "—", "Data not available"), cards[3]);
    }

    // Owner decision 13 / A2: gift cards are shown on their own, never inside the combined FTD value.
    [Fact]
    public void Combined_gift_cards_are_carried_beside_the_ftd_value_not_inside_it()
    {
        var facts = SalesFacts().Select(x => x.Period == "FTD" && x.StoreCode == "COMBINED" ? x with { TyGiftCards = 2_000m, LyGiftCards = 0m } : x).ToArray();
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), facts, [],
            new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")]);

        Assert.Equal(145_970m, document.CombinedFtd);
        Assert.Equal(2_000m, document.CombinedFtdGiftCards);
    }

    // RA-OPS-08 / decision 16: only the Titan World shop enters Service money, so the Service card sums that shop alone;
    // Helios having no Service entry is not applicable, not missing.
    [Fact]
    public void Service_total_counts_only_the_service_money_shop_when_titan_has_entered()
    {
        DsrServiceFact[] service =
        [
            new("FTD", "WLMHW", 700m, 1_807m, 577m, 3_084m, 0m),
            new("FTD", "HEMW", null, null, null, null, null),
            new("MTD", "WLMHW", null, null, null, 77_128m, 51_385m),
            new("MTD", "HEMW", null, null, null, null, null)
        ];
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), SalesFacts(), service,
            new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")], serviceMoneyStores: ["WLMHW"]);

        Assert.Equal(3_084m, document.Service.Total);
        Assert.Equal(700m, document.Service.Cash);
        Assert.Equal(1_807m, document.Service.Card);
        Assert.Equal(577m, document.Service.Upi);
        Assert.Equal(77_128m, document.Service.PeriodTotals["MTD"]);
        Assert.Equal(51_385m, document.Service.PeriodTotals["LY MTD"]);
        Assert.Equal("₹3,084", DsrDisplay.Currency(document.Service.Total));
    }

    [Fact]
    public void Service_total_stays_missing_when_the_service_money_shop_has_no_entry()
    {
        DsrServiceFact[] service =
        [
            new("FTD", "WLMHW", null, null, null, null, null),
            new("FTD", "HEMW", 100m, 200m, 300m, 600m, null)
        ];
        // The view lists the Service-money shop only once it holds an entry, so a Helios-only day gives an empty set.
        foreach (var shops in new IReadOnlyCollection<string>[] { ["WLMHW"], [] })
        {
            var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), SalesFacts(), service,
                new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")], serviceMoneyStores: shops);

            Assert.Null(document.Service.Total);
            Assert.Null(document.Service.Cash);
            Assert.Null(document.Service.PeriodTotals["FTD"]);
            Assert.Equal("—", DsrDisplay.Currency(document.Service.Total));
        }
    }

    [Fact]
    public void Service_total_without_a_service_money_shop_list_still_needs_every_store()
    {
        DsrServiceFact[] service =
        [
            new("FTD", "WLMHW", 700m, 1_807m, 577m, 3_084m, 0m),
            new("FTD", "HEMW", null, null, null, null, null)
        ];
        var document = DailySalesReportBuilder.Build(new DateOnly(2026, 9, 28), SalesFacts(), service,
            new Dictionary<string, decimal?>(), stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")]);

        Assert.Null(document.Service.Total);
    }

    [Fact]
    public void Evening_dsr_excel_rows_keep_the_gift_card_line_with_its_note()
    {
        var sheet = new EveningStoreSheet("HEMW", "Helios", null, null, null, null,
        [
            new("VALUE", 4_000m, 3_000m, 33.3m, 40_000m, 400_000m, 300_000m, "currency"),
            new("Other / unmapped", 4_000m, 3_000m, 33.3m, 40_000m, 400_000m, 300_000m, "currency"),
            new("GIFT CARD", 1_000m, 0m, null, 1_000m, 5_000m, 0m, "currency", "Gift-card sales (GIFT CARD / BRAND GC); not in VALUE, VOL or INVOICE")
        ]);
        var data = EveningReportTables.Dsr([sheet]);

        var gift = data.Rows.Single(x => Equals(x[1], "GIFT CARD"));
        Assert.Equal(1_000m, gift[2]);
        Assert.Equal(5_000m, gift[6]);
        Assert.Contains("not in VALUE", (string)gift[8]!);
        Assert.Equal(4_000m, data.Rows.Single(x => Equals(x[1], "VALUE"))[2]);
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
