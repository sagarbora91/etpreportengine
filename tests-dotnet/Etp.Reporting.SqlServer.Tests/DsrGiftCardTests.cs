using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// Owner decision 13 / A2, A-Q2 confirmed 4 Oct 2026: gift-card lines (GIFT CARD / BRAND GC) leave DSR VALUE,
/// VOL and INVOICE and appear on their own GIFT CARD line. The SQL itself is pinned at the elevated gate
/// (EveningReportsSqlTests); these tests pin the SQL text and the SQL-free row builder.
/// </summary>
public sealed class DsrGiftCardTests
{
    private static readonly string[] Stores = ["WLMHW", "HEMW"];

    private static IReadOnlyList<DsrManagementRow> Rows(Dictionary<string, Facts> facts) =>
        OperationalReportRepository.BuildDsrPeriodRows("FTD", Stores,
            new IndianFinancialYearPeriodPolicy().Resolve(new DateOnly(2026, 9, 28), ReportingPeriodKind.Ftd),
            facts, new Dictionary<string, WalkIns>(StringComparer.OrdinalIgnoreCase), new ManagementMetricEngine());

    [Fact]
    public void Store_rows_carry_gift_card_value_outside_sales_and_combined_sums_when_every_store_has_it()
    {
        var rows = Rows(new(StringComparer.OrdinalIgnoreCase)
        {
            ["WLMHW"] = new(TySales: 10_000m, LySales: 8_000m, TyUnits: 2m, TyInvoices: 2, TyGiftCards: 500m, LyGiftCards: 0m),
            ["HEMW"] = new(TySales: 4_000m, LySales: 3_000m, TyUnits: 1m, TyInvoices: 1, TyGiftCards: 1_000m, LyGiftCards: 250m)
        });

        Assert.Equal(500m, rows.Single(x => x.Store == "WLMHW").TyGiftCards);
        var combined = rows.Single(x => x.Store == "COMBINED");
        Assert.Equal(14_000m, combined.TySales);
        Assert.Equal(1_500m, combined.TyGiftCards);
        Assert.Equal(250m, combined.LyGiftCards);
        // AVPT stays on merchandise only: 14,000 over 3 invoices.
        Assert.Equal(14_000m / 3m, combined.Atv);
    }

    [Fact]
    public void Combined_gift_cards_are_unavailable_when_a_store_has_no_figure()
    {
        var rows = Rows(new(StringComparer.OrdinalIgnoreCase)
        {
            ["WLMHW"] = new(TySales: 10_000m, TyUnits: 2m, TyInvoices: 2, TyGiftCards: 500m)
        });

        Assert.Null(rows.Single(x => x.Store == "HEMW").TyGiftCards);
        Assert.Null(rows.Single(x => x.Store == "COMBINED").TyGiftCards);
    }

    [Fact]
    public void Dsr_facts_sql_leaves_gift_cards_out_of_value_units_and_invoices_and_returns_them_separately()
    {
        var sql = OperationalReportRepository.DsrFactsSql;
        var predicate = GiftCardSql.SalesLine("l");

        Assert.Contains("N'GIFT CARD'", predicate);
        Assert.Contains("N'GC'", predicate);
        Assert.Contains("l.product_code", predicate);
        Assert.Contains("l.source_brand_code", predicate);
        Assert.Equal(4, Count(sql, $"CASE WHEN {predicate} THEN 0 ELSE l.source_"));
        Assert.Equal(2, Count(sql, $"UPPER(l.source_transaction_type)='INV' AND NOT {predicate}"));
        Assert.Equal(2, Count(sql, $"AND {predicate} THEN l.source_gross_amount END"));
    }

    [Theory]
    [InlineData("GIFT CARD")]
    [InlineData(" gift card ")]
    [InlineData("Other / unmapped")]
    [InlineData("INVOICE")]
    public void Gift_card_is_a_reserved_dsr_label(string label) => Assert.True(EveningMasterRepository.IsReservedLabel(label));

    [Fact]
    public void A_brand_label_is_not_reserved() => Assert.False(EveningMasterRepository.IsReservedLabel("G SHOCK"));

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
