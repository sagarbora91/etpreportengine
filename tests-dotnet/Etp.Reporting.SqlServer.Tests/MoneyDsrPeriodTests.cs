using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// 1.12.1 lane M2 (money test gaps #5, #9, #20; decision 68): partly covered periods, productivity of combined/LY figures,
/// and the 25 Aug 2026 tie-out store totals through the pure row builder and the DSR builder. Every expected value is
/// worked out by hand in the comment beside it. Synthetic store codes and the store totals of the tie-out only.
/// </summary>
public sealed class MoneyDsrPeriodTests
{
    private static readonly string[] OneStore = ["WLMHW"];
    private static readonly string[] TwoStores = ["WLMHW", "HEMW"];
    private static readonly DateOnly TieOutDay = new(2026, 8, 25);
    private static readonly IndianFinancialYearPeriodPolicy Policy = new();

    private static Dictionary<string, Facts> NewFacts() => new(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<DsrManagementRow> Rows(string kind, IReadOnlyList<string> stores, Dictionary<string, Facts> facts, DateOnly day) =>
        OperationalReportRepository.BuildDsrPeriodRows(kind, stores, Policy.Resolve(day, Enum.Parse<ReportingPeriodKind>(kind, ignoreCase: true)),
            facts, new Dictionary<string, WalkIns>(StringComparer.OrdinalIgnoreCase), new ManagementMetricEngine());

    // ---- gap #5, decision 68: a partly covered period shows the partial figure, marked partial, growth blank ----

    [Fact]
    public void Partly_covered_ly_period_keeps_the_partial_figure_and_the_ty_figure()
    {
        // MTD of 25 Aug 2026: LY period is 1-25 Aug 2025. The history file starts 15 Aug 2025, so 1-14 Aug 2025 have no source.
        var period = Policy.Resolve(TieOutDay, ReportingPeriodKind.Mtd);
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 20_000m, LySales: 5_000m, TyUnits: 4m, LyUnits: 2m, TyInvoices: 4, LyInvoices: 2);

        var applied = OperationalReportRepository.ApplySourceCoverage(facts, [("WLMHW", new(2025, 8, 15), new(2026, 8, 25))], OneStore, period);

        // Decision 68: the partial figure is shown (not nulled, not turned into a zero).
        Assert.Equal(5_000m, applied["WLMHW"].LySales);
        Assert.Equal(20_000m, applied["WLMHW"].TySales);
        Assert.Equal(new DateOnly(2026, 8, 25), applied["WLMHW"].SourceCoversTo);
    }

    [Fact(Skip = "Fails until F1 (gap #5, decision 68)")]
    public void Partly_covered_ly_period_has_no_growth_percent_and_is_marked_partial()
    {
        var period = Policy.Resolve(TieOutDay, ReportingPeriodKind.Mtd);
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 20_000m, LySales: 5_000m, TyUnits: 4m, LyUnits: 2m, TyInvoices: 4, LyInvoices: 2);
        var applied = OperationalReportRepository.ApplySourceCoverage(facts, [("WLMHW", new(2025, 8, 15), new(2026, 8, 25))], OneStore, period);

        var row = OperationalReportRepository.BuildDsrPeriodRows("MTD", OneStore, period, applied,
            new Dictionary<string, WalkIns>(StringComparer.OrdinalIgnoreCase), new ManagementMetricEngine()).Single(x => x.Store == "WLMHW");

        // 20,000 / 5,000 - 1 = 3 = 300% today. Decision 68: growth is blank for a partly covered comparison.
        Assert.Null(row.GrowthPercent);
        Assert.Equal(5_000m, row.LySales);
        // The 'partial' marker: F1 names the member. Found by name so this file compiles whatever F1 calls it.
        var marker = typeof(DsrManagementRow).GetProperties().SingleOrDefault(p => p.Name.Contains("Partial", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(marker);
        var value = marker!.GetValue(row);
        Assert.True(value is true || value is string { Length: > 0 } || (value is not null and not bool and not string), $"{marker.Name} must mark the LY MTD as partial");
    }

    [Fact]
    public void Fully_covered_ly_period_keeps_its_growth_percent()
    {
        // The control: coverage from 16 Sep 2024 spans every day of 1-25 Aug 2025 and 1-25 Aug 2026.
        var period = Policy.Resolve(TieOutDay, ReportingPeriodKind.Mtd);
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 20_000m, LySales: 5_000m, TyUnits: 4m, LyUnits: 2m, TyInvoices: 4, LyInvoices: 2);
        var applied = OperationalReportRepository.ApplySourceCoverage(facts, [("WLMHW", new(2024, 9, 16), new(2026, 8, 25))], OneStore, period);

        var row = Rows("MTD", OneStore, applied, TieOutDay).Single(x => x.Store == "WLMHW");

        Assert.Equal(300m, row.GrowthPercent); // (20,000 / 5,000 - 1) * 100
    }

    [Fact(Skip = "Fails until F1 (gap #5, decision 68)")]
    public void A_ty_period_with_one_missing_day_in_the_middle_is_partial_with_no_growth()
    {
        // 1-10 Aug and 12-25 Aug 2026 are covered; 11 Aug has no source. LY is fully covered.
        var period = Policy.Resolve(TieOutDay, ReportingPeriodKind.Mtd);
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 20_000m, LySales: 16_000m, TyUnits: 4m, LyUnits: 3m, TyInvoices: 4, LyInvoices: 3);
        var applied = OperationalReportRepository.ApplySourceCoverage(facts,
            [("WLMHW", new(2024, 9, 16), new(2026, 8, 10)), ("WLMHW", new(2026, 8, 12), new(2026, 8, 25))], OneStore, period);

        var row = Rows("MTD", OneStore, applied, TieOutDay).Single(x => x.Store == "WLMHW");

        Assert.Equal(20_000m, row.TySales);   // the partial figure is still shown
        Assert.Null(row.GrowthPercent);       // today (20,000 / 16,000 - 1) * 100 = 25
    }

    // ---- gap #9: combined and LY productivity use summed numerators and denominators ----

    [Fact]
    public void Combined_ftd_productivity_divides_the_summed_value_by_the_summed_invoices()
    {
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 69_880m, TyUnits: 8m, TyInvoices: 8);
        facts["HEMW"] = new(TySales: 76_090m, TyUnits: 3m, TyInvoices: 3);

        var rows = Rows("FTD", TwoStores, facts, TieOutDay);
        var combined = rows.Single(x => x.Store == "COMBINED");
        var helios = rows.Single(x => x.Store == "HEMW");

        // 69,880 + 76,090 = 1,45,970 ; 8 + 3 = 11 units and 11 invoices.
        Assert.Equal(145_970m, combined.TySales);
        Assert.Equal(1.00m, Math.Round(combined.Upt!.Value, 2));          // 11 / 11
        Assert.Equal(13_270.00m, Math.Round(combined.Atv!.Value, 2));     // 1,45,970 / 11 = 13,270 exactly
        // Not the average of the two store figures: (8,735 + 25,363.33) / 2 = 17,049.17.
        Assert.NotEqual(17_049.17m, Math.Round(combined.Atv.Value, 2));
        Assert.Equal(25_363.33m, Math.Round(helios.Atv!.Value, 2));       // 76,090 / 3 = 25,363.333...
    }

    [Fact]
    public void Mtd_productivity_rounds_to_two_decimals_from_the_summed_figures()
    {
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 973_862m, TyUnits: 199m, TyInvoices: 181);

        var titan = Rows("MTD", OneStore, facts, TieOutDay).Single(x => x.Store == "WLMHW");

        Assert.Equal(1.10m, Math.Round(titan.Upt!.Value, 2));         // 199 / 181 = 1.09945
        Assert.Equal(5_380.45m, Math.Round(titan.Atv!.Value, 2));     // 9,73,862 / 181 = 5,380.4530 (181 x 5,380 = 9,73,780, rest 82 / 181 = 0.453)
    }

    [Fact]
    public void Ly_productivity_on_the_card_is_ly_units_and_value_over_ly_invoices()
    {
        var facts = NewFacts();
        facts["WLMHW"] = new(TySales: 69_880m, LySales: 22_647.50m, TyUnits: 8m, LyUnits: 6m, TyInvoices: 8, LyInvoices: 5);
        var report = Build(Rows("FTD", OneStore, facts, TieOutDay), OneStore);

        var card = report.Stores.Single(x => x.StoreCode == "WLMHW");
        var aupt = card.OperationalMetrics.Single(x => x.Name == "AUPT");
        var avpt = card.OperationalMetrics.Single(x => x.Name == "AVPT");

        Assert.Equal(1.2m, aupt.LastYear);        // 6 / 5
        Assert.Equal(4_529.5m, avpt.LastYear);    // 22,647.50 / 5
        Assert.Equal(8_735m, avpt.Ftd);           // 69,880 / 8
        Assert.Equal(1m, aupt.Ftd);               // 8 / 8
    }

    // ---- gap #20: the 25 Aug 2026 tie-out store totals ----

    [Fact]
    public void The_25_aug_tie_out_totals_give_the_figures_the_owner_checked_on_screen()
    {
        var ftd = NewFacts();
        ftd["WLMHW"] = new(TySales: 69_880m, LySales: 22_647.50m, TyUnits: 8m, LyUnits: 6m, TyInvoices: 8, LyInvoices: 5);
        ftd["HEMW"] = new(TySales: 76_090m, TyUnits: 3m, TyInvoices: 3);
        var mtd = NewFacts();
        mtd["WLMHW"] = new(TySales: 973_862m, TyUnits: 199m, TyInvoices: 181);
        mtd["HEMW"] = new(TySales: 821_668.60m, TyUnits: 40m, TyInvoices: 38);
        var ytd = NewFacts();
        ytd["WLMHW"] = new(TySales: 6_703_293.10m, LySales: 4_890_065.45m, TyUnits: 1_325m, LyUnits: 1_116m, TyInvoices: 1_211, LyInvoices: 986);
        ytd["HEMW"] = new(LySales: 2_136_215.10m, LyUnits: 126m, LyInvoices: 126, LyGiftCards: 50_000m);

        var rows = Rows("FTD", TwoStores, ftd, TieOutDay).Concat(Rows("MTD", TwoStores, mtd, TieOutDay)).Concat(Rows("YTD", TwoStores, ytd, TieOutDay)).ToArray();
        var report = Build(rows, TwoStores);

        // 69,880 + 76,090 = 1,45,970
        Assert.Equal(145_970m, report.CombinedFtd);
        Assert.Equal("₹1,45,970", DsrKpiCards.For(report).Single(x => x.Label == "COMBINED FTD").Value);
        // 9,73,862 + 8,21,668.60 = 17,95,530.60 ; 17,95,530.60 / 1,00,000 = 17.955 -> "17.96"
        Assert.Equal(1_795_530.60m, report.MtdSales);
        Assert.Equal("₹17.96 L", DsrKpiCards.For(report).Single(x => x.Label == "MTD SALES").Value);
        // 69,880 / 22,647.50 = 3.08555 -> growth 208.555% -> "+208.6%"
        var titan = report.Stores.Single(x => x.StoreCode == "WLMHW");
        Assert.Equal(208.56m, Math.Round(titan.Periods[0].ValueGrowth.Value!.Value, 2));
        Assert.Equal("+208.6%", DsrDisplay.Percent(titan.Periods[0].ValueGrowth));
        // YTD: 67,03,293.10 / 48,90,065.45 = 1.370798 -> "+37.1%"
        Assert.Equal("+37.1%", DsrDisplay.Percent(titan.Periods[2].ValueGrowth));
        // Helios AVPT: 76,090 / 3 = 25,363.33
        var helios = rows.Single(x => x.Period == "FTD" && x.Store == "HEMW");
        Assert.Equal(25_363.33m, Math.Round(helios.Atv!.Value, 2));
        // Helios LY YTD gift cards come through on their own line, outside VALUE.
        Assert.Equal(50_000m, rows.Single(x => x.Period == "YTD" && x.Store == "HEMW").LyGiftCards);
        // Combined YTD is unknown while Helios has no current YTD figure: it is not a partial sum.
        Assert.Null(report.YtdSales);
    }

    private static DailySalesReportDocument Build(IReadOnlyList<DsrManagementRow> rows, IReadOnlyList<string> stores) =>
        DailySalesReportBuilder.Build(TieOutDay,
            rows.Select(x => new DsrPeriodFact(x.Period, x.Store, x.TySales, x.LySales, x.TyUnits, x.LyUnits, x.TyInvoices, x.LyInvoices,
                x.Upt, x.Atv, x.WalkIns, x.ConversionPercent, x.TyGiftCards, x.LyGiftCards)).ToArray(),
            [], new Dictionary<string, decimal?>(), stores: stores.Select(x => new DsrStoreDefinition(x, x)).ToArray());
}
