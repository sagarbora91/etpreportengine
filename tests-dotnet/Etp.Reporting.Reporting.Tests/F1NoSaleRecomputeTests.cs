using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.12.1 lane F1 (money gap #16): when a no-sale mark makes every store's day known, the combined growth, invoice count and
/// the COMBINED matrix are recomputed with the tile, not left "—". Synthetic store codes and amounts.
/// </summary>
public sealed class F1NoSaleRecomputeTests
{
    [Fact]
    public void A_marked_store_completes_the_combined_growth_invoices_and_matrix()
    {
        var sales = new List<DsrPeriodFact>
        {
            // QQ1 sold 3,000 on 3 bills (LY 2,000); QQ2 has no file for the day (LY 1,000 on 1 bill). Combined LY 3,000.
            new("FTD", "QQ1", 3_000m, 2_000m, 5m, 4m, 3, 2, null, null, null, null),
            new("FTD", "QQ2", null, 1_000m, null, 1m, null, 1, null, null, null, null),
            new("FTD", "COMBINED", null, 3_000m, null, 5m, null, 3, null, null, null, null)
        };
        var built = DailySalesReportBuilder.Build(new(2026, 8, 21), sales, [], new Dictionary<string, decimal?>(),
            stores: [new("QQ1", "Shop one"), new("QQ2", "Shop two")]);
        var document = built with
        {
            EveningSheets =
            [
                new("QQ1", "Shop one", null, null, null, null, [new("VALUE", 3_000m, 2_000m, 50m, 9_000m, 9_000m, 8_000m, "currency"), new("INVOICE", 3m, 2m, 50m, 9m, 9m, 8m)]),
                new("QQ2", "Shop two", null, null, null, null, [new("VALUE", null, 1_000m, null, 4_000m, 4_000m, 3_000m, "currency"), new("INVOICE", null, 1m, null, 4m, 4m, 3m)]),
                new("COMBINED", "Combined", null, null, null, null, [new("VALUE", null, 3_000m, null, 13_000m, 13_000m, 11_000m, "currency"), new("INVOICE", null, 3m, null, 13m, 13m, 11m)])
            ]
        };
        Assert.Null(document.CombinedFtdGrowth.Value);

        var applied = DailySalesReportBuilder.ApplyNoSaleDays(document, [new("QQ2", "No sales (marked by manager on 21 Aug 2026)")]);

        Assert.Equal(3_000m, applied.CombinedFtd);                  // 3,000 + a real 0
        Assert.Equal(0m, applied.CombinedFtdGrowth.Value);           // (3,000 / 3,000 - 1) x 100 = 0 (was "—")
        Assert.Equal(3, applied.CombinedInvoices);                   // 3 + 0 (was "—")
        var combined = applied.EveningSheets.Single(x => x.StoreCode == "COMBINED");
        Assert.Equal((3_000m, 0m), (combined.Rows.Single(x => x.Metric == "VALUE").Ftd, combined.Rows.Single(x => x.Metric == "VALUE").Growth));
        Assert.Equal(3m, combined.Rows.Single(x => x.Metric == "INVOICE").Ftd);
        Assert.Equal(13_000m, combined.Rows.Single(x => x.Metric == "VALUE").Mtd); // MTD untouched
    }
}
