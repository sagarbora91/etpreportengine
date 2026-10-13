using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.12 lane N1 (decision 52): a store-day marked as a no-sale day reads "No sales (marked by ...)" on the DSR, its PDF matrix,
/// Excel and the pack, with real zeros for the day's sales instead of "not imported". Synthetic store codes only.
/// </summary>
public sealed class DailySalesReportNoSaleTests
{
    private static readonly DateOnly Day = new(2026, 8, 21);
    private const string Line = "No sales (marked by manager on 21 Aug 2026)";

    private static DailySalesReportDocument Document()
    {
        var sales = new List<DsrPeriodFact>
        {
            // ZZ001 has sales; ZZ002 has no sales file for the day but earlier days of the month.
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
                new("ZZ001", "Shop one", null, null, null, null, [new("VALUE", 1_000m, 900m, 11m, 9_000m, 20_000m, 18_000m, "currency"), new("VOL", 4m, 3m, 33m, 30m, 60m, 50m)]),
                new("ZZ002", "Shop two", null, null, null, null, [new("VALUE", null, 700m, null, 5_000m, 9_000m, 8_000m, "currency"), new("VOL", null, 2m, null, 12m, 20m, 18m), new("RETAIL WALKIN", null, null, null, 60m, 90m, null)]),
                new("COMBINED", "Combined", null, null, null, null, [new("VALUE", null, 1_600m, null, 14_000m, 29_000m, 26_000m, "currency")])
            ]
        };
    }

    [Fact]
    public void A_marked_store_without_sales_gets_the_line_and_real_zeros_for_the_day()
    {
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(Document(), [new("ZZ002", Line)]);

        var mark = Assert.Single(applied.NoSaleDays);
        Assert.Equal(("ZZ002", Line), (mark.StoreCode, mark.Line));
        Assert.Equal(Line, applied.NoSaleLine("zz002"));
        Assert.Null(applied.NoSaleLine("ZZ001"));

        var card = Assert.Single(applied.Stores, x => x.StoreCode == "ZZ002");
        Assert.Equal(0m, card.Periods[0].TyValue);
        Assert.Equal(0m, card.Periods[0].TyQuantity);
        Assert.Equal(5_000m, card.Periods[1].TyValue); // MTD stays as it is

        var sheet = Assert.Single(applied.EveningSheets, x => x.StoreCode == "ZZ002");
        Assert.Equal(Line, sheet.Rows[0].Metric);
        Assert.Null(sheet.Rows[0].Ftd);
        Assert.Equal(0m, sheet.Rows.Single(x => x.Metric == "VALUE").Ftd);
        Assert.Equal(0m, sheet.Rows.Single(x => x.Metric == "VOL").Ftd);
        Assert.Null(sheet.Rows.Single(x => x.Metric == "RETAIL WALKIN").Ftd); // walk-ins are an entry, not a sale
        Assert.Equal(5_000m, sheet.Rows.Single(x => x.Metric == "VALUE").Mtd);

        // Every store is now known for the day, so the combined tile shows the sum (ZZ001's sales plus a real 0).
        Assert.Equal(1_000m, applied.CombinedFtd);
        Assert.Equal(4m, applied.Units);
    }

    [Fact]
    public void The_line_reaches_the_pdf_matrix_and_the_excel_rows()
    {
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(Document(), [new("ZZ002", Line)]);
        var sheet = Assert.Single(applied.EveningSheets, x => x.StoreCode == "ZZ002");

        Assert.Equal(Line, EveningDsrPdf.Cells(sheet.Rows[0])[0]);
        var excel = EveningReportTables.Dsr(applied.EveningSheets);
        Assert.Contains(excel.Rows, row => Equals(row[0], "Shop two") && Equals(row[1], Line) && Equals(row[8], "No-sale day"));
    }

    [Fact]
    public void A_marked_store_that_holds_sales_and_unmarked_stores_are_left_alone()
    {
        var original = Document();
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(original, [new("ZZ001", Line)]);

        Assert.Same(original, applied);
        Assert.Empty(applied.NoSaleDays);
        Assert.Same(original, DailySalesReportBuilder.ApplyNoSaleDays(original, []));
        Assert.Same(original, DailySalesReportBuilder.ApplyNoSaleDays(original, [new("ZZ404", Line)]));
    }

    [Fact]
    public void The_combined_sheet_is_never_marked()
    {
        var applied = DailySalesReportBuilder.ApplyNoSaleDays(Document(), [new("COMBINED", Line), new("ZZ002", Line)]);

        Assert.DoesNotContain(applied.EveningSheets.Single(x => x.StoreCode == "COMBINED").Rows, row => row.Metric == Line);
        Assert.Equal(["ZZ002"], applied.NoSaleDays.Select(x => x.StoreCode));
    }
}
