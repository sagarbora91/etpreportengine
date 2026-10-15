using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>1.12.1 lane F1 (decision 68): the money rules that need no database. Synthetic figures, worked out beside each line.</summary>
public sealed class F1MoneyRulesTests
{
    [Theory]
    // 25 Aug: 31 - 25 = 6 days left; 6,26,138 / 6 = 1,04,356.33... (7 before 1.12.1: 89,448.29).
    [InlineData("2026-08-25", "626138", "104356.33")]
    // 10 Feb 2028 (leap): 29 - 10 = 19 days left; 1,90,000 / 19 = 10,000.
    [InlineData("2028-02-10", "190000", "10000.00")]
    // 28 Feb 2028: one day left; an over-achieved month reads negative: -10,000 / 1 = -10,000.
    [InlineData("2028-02-28", "-10000", "-10000.00")]
    public void Required_daily_sales_divides_by_the_days_left_after_the_business_date(string day, string balance, string expected)
    {
        var value = OperationalReportRepository.RequiredDailySales(decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Math.Round(value!.Value, 2));
    }

    [Fact]
    public void Required_daily_sales_is_blank_on_the_last_day_and_without_a_target()
    {
        Assert.Null(OperationalReportRepository.RequiredDailySales(-10_000m, new DateOnly(2028, 2, 29))); // no day left (divided by 1 before)
        Assert.Null(OperationalReportRepository.RequiredDailySales(5_000m, new DateOnly(2026, 8, 31)));
        Assert.Null(OperationalReportRepository.RequiredDailySales(null, new DateOnly(2026, 8, 25)));
    }

    [Fact]
    public void Tender_documents_pass_within_one_rupee_and_keep_their_variance()
    {
        // Invoice 1,129.40 paid 1,129.00 cash: variance 0.40, Passed (Failed before 1.12.1). 1,130.01 vs 1,129.00: 1.01, Failed.
        var result = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "D1", 1129.40m), new("S1", "D2", 1130.01m)],
            [new("S1", "D1", "CASH", 1129.00m, true), new("S1", "D2", "CASH", 1129.00m, true)],
            RetailReportingPolicy.Tender);
        Assert.Equal((0.40m, ReconciliationStatus.Passed), (result.Documents[0].Variance, result.Documents[0].Status));
        Assert.Equal((1.01m, ReconciliationStatus.Failed), (result.Documents[1].Variance, result.Documents[1].Status));
        Assert.Equal(ReconciliationStatus.Failed, result.Status);
    }

    [Fact]
    public void The_wdc_tile_sums_only_the_service_money_shop()
    {
        // Titan (Service-money shop) WDC 250, Helios WDC 100: tile 250 (350 before 1.12.1). No Service-money shop: blank.
        (string, decimal)[] entries = [("WLMHW", 250m), ("HEMW", 100m)];
        Assert.Equal(250m, OperationalReportRepository.ServiceMoneyWdc(entries, new HashSet<string>(["wlmhw"], StringComparer.OrdinalIgnoreCase)));
        Assert.Null(OperationalReportRepository.ServiceMoneyWdc(entries, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    }

    // Decision 68 #4. MTD of 25 Aug 2026; LY = 1-25 Aug 2025. TY 20,000, LY 5,000 (growth 300% if both were complete).
    private static readonly BusinessReportingPeriod Mtd = new IndianFinancialYearPeriodPolicy().Resolve(new(2026, 8, 25), ReportingPeriodKind.Mtd);

    private static DsrManagementRow MtdRow(IReadOnlyList<(string, DateOnly, DateOnly)> ranges, IReadOnlyList<(string, DateOnly)>? noSale = null)
    {
        var facts = new Dictionary<string, Facts>(StringComparer.OrdinalIgnoreCase) { ["ZZ1"] = new(TySales: 20_000m, LySales: 5_000m, TyUnits: 4m, LyUnits: 2m, TyInvoices: 4, LyInvoices: 2) };
        var applied = OperationalReportRepository.ApplySourceCoverage(facts, ranges, ["ZZ1"], Mtd, noSale);
        return OperationalReportRepository.BuildDsrPeriodRows("MTD", ["ZZ1"], Mtd, applied, new Dictionary<string, WalkIns>(StringComparer.OrdinalIgnoreCase), new ManagementMetricEngine())[0];
    }

    [Fact]
    public void A_partly_covered_ly_period_keeps_its_figure_marked_partial_with_growth_blank()
    {
        // The history starts 15 Aug 2025, so 1-14 Aug 2025 have no file: LY MTD 5,000 is a partial sum.
        var row = MtdRow([("ZZ1", new(2025, 8, 15), new(2026, 8, 25))]);
        Assert.Equal((20_000m, 5_000m), (row.TySales, row.LySales));
        Assert.Null(row.GrowthPercent);                       // 300 before 1.12.1
        Assert.Equal("Partial", row.GrowthStatus);
        Assert.Equal("Partial: the sales export covers only part of LY MTD", row.PartialNote);
    }

    [Fact]
    public void A_missing_day_makes_this_year_partial_but_a_marked_no_sale_day_does_not()
    {
        (string, DateOnly, DateOnly)[] gap = [("ZZ1", new(2024, 9, 16), new(2026, 8, 10)), ("ZZ1", new(2026, 8, 12), new(2026, 8, 25))];
        var missing = MtdRow(gap);
        Assert.Equal("Partial: the sales export covers only part of MTD", missing.PartialNote);
        Assert.Null(missing.GrowthPercent);

        // 11 Aug marked as a no-sale day (decision 52): every day is known, growth (20,000 / 5,000 - 1) x 100 = 300.
        var marked = MtdRow(gap, [("zz1", new DateOnly(2026, 8, 11))]);
        Assert.Null(marked.PartialNote);
        Assert.Equal(300m, marked.GrowthPercent);
    }

    [Fact]
    public void The_dsr_document_shows_a_partial_period_with_its_note_and_no_growth()
    {
        var row = MtdRow([("ZZ1", new(2025, 8, 15), new(2026, 8, 25))]);
        var document = DailySalesReportBuilder.Build(new(2026, 8, 25),
            [new("MTD", "ZZ1", row.TySales, row.LySales, row.TyUnits, row.LyUnits, row.TyInvoices, row.LyInvoices, row.Upt, row.Atv, null, null, PartialNote: row.PartialNote)],
            [], new Dictionary<string, decimal?>());
        var mtd = document.Stores.Single().Periods.Single(x => x.Period == "MTD");
        Assert.Equal((20_000m, 5_000m), (mtd.TyValue, mtd.LyValue));
        Assert.Null(mtd.ValueGrowth.Value);
        Assert.Null(mtd.QuantityGrowth.Value);
        Assert.Equal(row.PartialNote, mtd.MissingSourceNote);
    }
}
