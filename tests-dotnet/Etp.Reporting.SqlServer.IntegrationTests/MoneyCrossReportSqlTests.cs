using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane M1 (MONEY-TEST-GAPS #1, #2, #3, #4, #13; decision 68, 15 Oct 2026). Where two reports should agree on the
/// same store-day but are computed by different SQL: the DSR, Management Trend, Staff / CRO, Sales summary (Store and
/// Brand-wise) and Customer-wise. Every expected value is hand-computed in the comment beside it and follows decision 68:
/// gift cards are out of the store total in every report; a bill cancelled the same day is not an invoice and not a CRO
/// transaction (decision 41); a bill partly cancelled the same day is still one invoice.
/// <para>
/// Cross-layer: synthetic rows go into the real tables of a fresh test database, and the C# repositories read them through
/// the real SQL. Synthetic stores only (GCSTAFF, NEWTYPE, CRO41, PART41, TRND41, GCSUM), no customer data, no impersonation.
/// Tests that fail on the 1.12.0 code because of a decision-68 rule carry <c>Skip = "Fails until F1 ..."</c>; the integrator
/// removes the Skip once lane F1 (production fix) has merged (1121-M1-NOTE.md).
/// </para>
/// </summary>
public sealed class MoneyCrossReportSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // Gap #1. The CANCEL41 seed of ReportLogicAndSpeedSqlTests plus a gift-card-only bill on d1.
    //   d1: I1 = 1,000 + 500 (cancelled the same day by B1 = -1,000 - 500, R025 INVREFNO I1); I2 = 700; G1 = GIFT CARD 2,000.
    //   d2: I3 = 300.  d3: B2 = -300 cancels I3 two days later (not the same day, so I3 stays an invoice on d2).
    [SqlFact(Skip = "Fails until F1 (gap #1, decision 68)")]
    public async Task Trend_invoices_follow_decision_41_and_gift_cards_like_the_dsr()
    {
        const string store = "TRND41";
        var d1 = new DateOnly(2032, 10, 5); var d2 = d1.AddDays(1); var d3 = d1.AddDays(2);
        await SeedAsync(store,
            new("I1", d1, "INV", "ITEMC1", "WB", 1, 1000),
            new("I1", d1, "INV", "ITEMC2", "WB", 1, 500),
            new("I2", d1, "INV", "ITEMC3", "WB", 1, 700),
            new("B1", d1, "BC", "ITEMC1", "WB", -1, -1000, Ref: "I1", RefDate: d1),
            new("B1", d1, "BC", "ITEMC2", "WB", -1, -500, Ref: "I1", RefDate: d1),
            new("G1", d1, "INV", "GIFT CARD", "GC", 1, 2000),
            new("I3", d2, "INV", "ITEMC4", "WB", 1, 300),
            new("B2", d3, "BC", "ITEMC4", "WB", -1, -300, Ref: "I3", RefDate: d2));

        var trend = (await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(d1, d3))
            .Where(x => x.StoreCode == store).ToDictionary(x => x.BusinessDate);

        // d1: value 1,000 + 500 + 700 - 1,000 - 500 = 700 (gift card adds 0); units 1 + 1 + 1 - 1 - 1 = 1;
        // invoices: I2 only (I1 cancelled the same day, G1 is gift cards only) = 1; returns: B1 = 1. Today Trend gives 3 invoices.
        Assert.Equal((700m, 1m, 1, 1), (trend[d1].NetSales, trend[d1].Units, trend[d1].Invoices, trend[d1].Returns));
        // d2: I3 300, 1 unit, 1 invoice, no return.
        Assert.Equal((300m, 1m, 1, 0), (trend[d2].NetSales, trend[d2].Units, trend[d2].Invoices, trend[d2].Returns));
        // d3: B2 -300, -1 unit, no invoice, 1 return.
        Assert.Equal((-300m, -1m, 0, 1), (trend[d3].NetSales, trend[d3].Units, trend[d3].Invoices, trend[d3].Returns));

        // The DSR FTD of each day agrees with the Trend row of that day.
        var expected = new[] { (d1, 700m, 1), (d2, 300m, 1), (d3, -300m, 0) };
        foreach (var (day, value, invoices) in expected)
        {
            var ftd = await FtdAsync(store, day);
            Assert.Equal(value, ftd.TySales);
            Assert.Equal(invoices, ftd.TyInvoices);
            Assert.Equal(trend[day].NetSales, ftd.TySales);
            Assert.Equal(trend[day].Invoices, ftd.TyInvoices);
        }
        Assert.Equal(2000m, (await FtdAsync(store, d1)).TyGiftCards); // the DSR GIFT CARD line, outside VALUE
    }

    // Gap #2. Bill A1: watch 4,000 + gift card (brand GC) 1,000, both sold by CRO C1 (paired R013 rows).
    [SqlFact(Skip = "Fails until F1 (gap #2, decision 68)")]
    public async Task Staff_total_on_a_gift_card_day_equals_the_dsr_value()
    {
        const string store = "GCSTAFF";
        var day = new DateOnly(2032, 9, 14);
        await SeedAsync(store,
            new("A1", day, "INV", "WATCH2", "WB", 1, 4000, Cro: "C1"),
            new("A1", day, "INV", "GCX", "GC", 1, 1000, Cro: "C1"));

        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(day, day, [store]));

        // Gift cards out of the store total (decision 68 #1): canonical 4,000 and C1 4,000. Today 5,000 / 5,000.
        Assert.Equal((4000m, 4000m, 0m), (staff.CanonicalSales, staff.AttributedSales, staff.Variance));
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        var c1 = Assert.Single(staff.Rows);
        // C1: 4,000, 1 unit (the gift card adds 0 like DSR VOL), 1 transaction; UPT 1/1 = 1; ATV 4,000/1; 100% of the store.
        Assert.Equal(("C1", 4000m, 1m, 1), (c1.CroNumber, c1.NetSales, c1.NetQuantity, c1.Transactions));
        Assert.Equal(1m, c1.Upt);
        Assert.Equal(4000m, c1.Atv);
        Assert.Equal(100m, c1.ContributionPercent);

        var ftd = await FtdAsync(store, day);
        Assert.Equal(4000m, ftd.TySales);
        Assert.Equal(1000m, ftd.TyGiftCards);
        Assert.Equal(1, ftd.TyInvoices);
        Assert.Equal(ftd.TySales, staff.CanonicalSales);
    }

    // Gap #2, second half. A line whose type is not INV / SR / BC is not in the DSR VALUE, so the Staff store total, which
    // decision 68 makes equal to the DSR store total, leaves it out too. Today the canonical total adds it and the staff
    // check reads Failed for ever. NOTE: decision 68 names gift cards; this follows "store total = DSR VALUE" (gap #2's
    // first option). If F1 keeps unknown types in the canonical total, keep this skipped and ask Sagar (1121-M1-NOTE.md).
    [SqlFact(Skip = "Fails until F1 (gap #2, decision 68)")]
    public async Task Staff_store_total_leaves_out_a_line_type_the_dsr_does_not_count()
    {
        const string store = "NEWTYPE";
        var day = new DateOnly(2032, 9, 15);
        await SeedAsync(store,
            new("A1", day, "INV", "WATCH3", "WB", 1, 2000, Cro: "C1"),
            new("X1", day, "NEW", "ITEMX", "WB", 1, 500));

        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(day, day, [store]));
        var ftd = await FtdAsync(store, day);

        Assert.Equal(2000m, ftd.TySales); // the NEW line is not INV/SR/BC
        // Today: canonical 2,500, attributed 2,000, variance 500, Failed.
        Assert.Equal((2000m, 2000m, 0m), (staff.CanonicalSales, staff.AttributedSales, staff.Variance));
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        Assert.Equal(ftd.TySales, staff.CanonicalSales);
    }

    // Gap #3. C1 sells I1 1,500 (cancelled the same day by B1 -1,500, with an R025 BC row naming I1) and I2 700.
    [SqlFact(Skip = "Fails until F1 (gap #3, decision 68)")]
    public async Task A_bill_cancelled_the_same_day_is_not_a_cro_transaction()
    {
        const string store = "CRO41";
        var day = new DateOnly(2032, 9, 16);
        await SeedAsync(store,
            new("I1", day, "INV", "ITEM1", "WB", 1, 1500, Cro: "C1"),
            new("I2", day, "INV", "ITEM2", "WB", 1, 700, Cro: "C1"),
            new("B1", day, "BC", "ITEM1", "WB", -1, -1500, Ref: "I1", RefDate: day, Cro: "C1"));

        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(day, day, [store]));

        // Value 1,500 + 700 - 1,500 = 700; quantity 1 + 1 - 1 = 1; transactions: I2 only = 1 (today 2: I1 and I2,
        // giving ATV 350 and UPT 0.5). ATV 700 / 1 = 700, UPT 1 / 1 = 1.
        Assert.Equal((700m, 700m, ReconciliationStatus.Passed), (staff.CanonicalSales, staff.AttributedSales, staff.Status));
        var c1 = Assert.Single(staff.Rows);
        Assert.Equal((700m, 1m, 1), (c1.NetSales, c1.NetQuantity, c1.Transactions));
        Assert.Equal(700m, c1.Atv);
        Assert.Equal(1m, c1.Upt);

        // The DSR of the same day: VALUE 700, INVOICE 1, AVPT 700, AUPT 1. The CRO denominator is the DSR INVOICE.
        var ftd = await FtdAsync(store, day);
        Assert.Equal(700m, ftd.TySales);
        Assert.Equal(1m, ftd.TyUnits);
        Assert.Equal(1, ftd.TyInvoices);
        Assert.Equal(700m, ftd.Atv);
        Assert.Equal(1m, ftd.Upt);
        Assert.Equal(ftd.TyInvoices, staff.Rows.Sum(x => x.Transactions));
    }

    // Gap #4. I1 lines 1,000 (ITEMA) + 500 (ITEMB); B1 BC -500 (qty -1, ITEMB) names I1 on the same day: one line cancelled.
    [SqlFact(Skip = "Fails until F1 (gap #4, decision 68)")]
    public async Task A_bill_partly_cancelled_the_same_day_is_still_one_invoice_in_every_report()
    {
        const string store = "PART41";
        var day = new DateOnly(2032, 9, 17);
        await SeedAsync(store,
            new("I1", day, "INV", "ITEMA", "WB", 1, 1000, Cro: "C1"),
            new("I1", day, "INV", "ITEMB", "WB", 1, 500, Cro: "C1"),
            new("B1", day, "BC", "ITEMB", "WB", -1, -500, Ref: "I1", RefDate: day, Cro: "C1"));

        // DSR: VALUE 1,000 + 500 - 500 = 1,000; VOL 1 + 1 - 1 = 1; INVOICE 1 (decision 68 #3; today 0, so AVPT/AUPT read N/A);
        // AVPT 1,000 / 1 = 1,000; AUPT 1 / 1 = 1.
        var ftd = await FtdAsync(store, day);
        Assert.Equal(1000m, ftd.TySales);
        Assert.Equal(1m, ftd.TyUnits);
        Assert.Equal(1, ftd.TyInvoices);
        Assert.Equal(1000m, ftd.Atv);
        Assert.Equal(1m, ftd.Upt);

        // Management Trend: the same value, units and invoice; B1 is one return document.
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(day, day), x => x.StoreCode == store);
        Assert.Equal((1000m, 1m, 1, 1), (trend.NetSales, trend.Units, trend.Invoices, trend.Returns));

        // Staff / CRO: C1 has 1,000 and one transaction (I1), so ATV 1,000 and UPT 1.
        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(day, day, [store]));
        var c1 = Assert.Single(staff.Rows);
        Assert.Equal((1000m, 1m, 1), (c1.NetSales, c1.NetQuantity, c1.Transactions));
        Assert.Equal(1000m, c1.Atv);
        Assert.Equal(1m, c1.Upt);
        Assert.Equal((1000m, ReconciliationStatus.Passed), (staff.CanonicalSales, staff.Status));
    }

    // Gap #13. The GCDSR seed of EveningReportsSqlTests: G1 = watch 4,000 (brand WB) + gift card 1,000 (brand GC);
    // G2 = a gift-card-only bill (product GIFT CARD) 2,000.
    [SqlFact(Skip = "Fails until F1 (gap #13, decision 68)")]
    public async Task Sales_summary_brand_wise_customer_wise_and_trend_leave_gift_cards_out_like_the_dsr()
    {
        const string store = "GCSUM";
        var day = new DateOnly(2032, 10, 8);
        await SeedAsync(store,
            new("G1", day, "INV", "WATCH1", "WB", 1, 4000),
            new("G1", day, "INV", "GCX", "GC", 1, 1000),
            new("G2", day, "INV", "GIFT CARD", "GC", 1, 2000));

        // DSR (already right on 1.12.0): VALUE 4,000, VOL 1, INVOICE 1 (G1), GIFT CARD 1,000 + 2,000 = 3,000.
        var ftd = await FtdAsync(store, day);
        Assert.Equal(4000m, ftd.TySales);
        Assert.Equal(1m, ftd.TyUnits);
        Assert.Equal(1, ftd.TyInvoices);
        Assert.Equal(3000m, ftd.TyGiftCards);

        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(database.ConnectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var scope = new ReportingQueryScope(day, day, [store]);

        // Sales summary by store: 4,000, 1 unit, 1 invoice (G2 has no merchandise line), 0 returns. Today 7,000 / 3 / 2.
        var byStore = Assert.Single((await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Store)).Rows);
        Assert.Equal((store, 4000m, 1m, 1, 0), (byStore.Key, byStore.SourceSignedNetAmount, byStore.SourceSignedQuantity, byStore.Invoices, byStore.Returns));

        // Brand-wise: the rows add up to the DSR VALUE and no row carries the gift cards (brand GC). Today an "Unmapped: GC"
        // row holds 3,000.
        var byBrand = (await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Brand)).Rows;
        Assert.Equal(4000m, byBrand.Sum(x => x.SourceSignedNetAmount));
        Assert.DoesNotContain(byBrand, x => x.Key.Contains("GC", StringComparison.OrdinalIgnoreCase) || x.Key.Contains("GIFT", StringComparison.OrdinalIgnoreCase));

        // Customer-wise (one row per document): G1 4,000; G2 adds nothing to the store total (listed at 0 or left out).
        var customer = await new OperationalReportRepository(database.ConnectionString).LoadInvoiceSummaryAsync(scope);
        Assert.Equal(4000m, customer.Sum(x => x.NetValue));
        Assert.Equal(4000m, customer.Single(x => x.DocumentNumber == "G1").NetValue);
        Assert.Equal(0m, customer.Where(x => x.DocumentNumber == "G2").Sum(x => x.NetValue));

        // Management Trend: value 4,000 (already), invoices 1 (today 2, G2 counted).
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(day, day), x => x.StoreCode == store);
        Assert.Equal((4000m, 1), (trend.NetSales, trend.Invoices));

        // Every report gives the DSR store total.
        Assert.All(new[] { byStore.SourceSignedNetAmount, byBrand.Sum(x => x.SourceSignedNetAmount), customer.Sum(x => x.NetValue), trend.NetSales },
            total => Assert.Equal(ftd.TySales, (decimal?)total));
    }

    private async Task<DsrManagementRow> FtdAsync(string store, DateOnly day) =>
        (await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(day, [store])).Single(x => x.Period == "FTD" && x.Store == store);

    /// <summary>One synthetic sales line. <paramref name="Ref"/> is the R025 INVREFNO of a BC row; <paramref name="Cro"/> adds a paired R013 row.</summary>
    private sealed record Line(string Doc, DateOnly Date, string Type, string Item, string Brand, decimal Qty, decimal Amount,
        string? Ref = null, DateOnly? RefDate = null, string? Cro = null);

    /// <summary>
    /// Writes one current R025 file for the store (covering the seed's dates) with its sales lines, an etp_r025 row per line
    /// (so dbo.v_sales_cancelled_same_day sees a BC's INVREFNO, as the importer leaves it) and, for each line with a CRO,
    /// an R013 enrichment that EnrichmentOccurrencePairing pairs with it (store, date, document, item, occurrence).
    /// GST-inclusive amount = net amount, tax 0, as in the neighbouring seeds.
    /// </summary>
    private async Task SeedAsync(string store, params Line[] lines)
    {
        static string D(DateOnly date) => $"'{date:yyyyMMdd}'";
        static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        static int FinancialYear(DateOnly date) => date.Month >= 4 ? date.Year + 1 : date.Year;
        var from = lines.Min(x => x.Date); var to = lines.Max(x => x.Date);
        var sql = new StringBuilder($"""
            DECLARE @store varchar(30)='{store}',@batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'m1-{store.ToLowerInvariant()}-r025.xlsx','{Guid.NewGuid():N}{Guid.NewGuid():N}',1,@store,'R025',{D(to)},{D(from)},{D(to)},1);
            SET @file=SCOPE_IDENTITY();

            """);
        var lineage = new List<string>();
        for (var n = 1; n <= lines.Length; n++) lineage.Add($"(@file,'Sales',{n},'sale')");
        for (var n = 1; n <= lines.Length; n++) if (lines[n - 1].Cro is not null) lineage.Add($"(@file,'Staff',{n},'staff')");
        sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES{string.Join(',', lineage)};");
        var invoices = lines.Select(x => (x.Doc, x.Date)).Distinct().Select(x => $"(@store,'{x.Doc}',{FinancialYear(x.Date)},{D(x.Date)})");
        sql.AppendLine($"INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES{string.Join(',', invoices)};");
        var values = string.Join(',', lines.Select((x, i) =>
            $"('{x.Doc}','{i + 1}',N'{x.Item}',N'{x.Brand}','{x.Type}',{N(x.Qty)},{N(x.Amount)},{i + 1},{D(x.Date)}," +
            $"{(x.Ref is null ? "CONVERT(nvarchar(80),NULL)" : $"N'{x.Ref}'")},{(x.RefDate is { } refDate ? $"CONVERT(date,{D(refDate)})" : "CONVERT(date,NULL)")}," +
            $"{(x.Cro is null ? "CONVERT(varchar(30),NULL)" : $"'{x.Cro}'")})"));
        const string columns = "v(doc,line,item,brand,type,qty,amount,rowNo,d,ref,refDate,cro)";
        sql.AppendLine($"""
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,source_brand_code,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,v.line,v.item,v.type,v.qty,v.amount,v.amount,0,v.brand,'INR',s.source_lineage_id
            FROM (VALUES{values}) {columns}
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc AND i.transaction_date=v.d
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=v.rowNo;
            INSERT dbo.etp_r025(import_file_id,source_lineage_id,content_key,source_transaction_type,store_code,invoice_number,transaction_date,reference_invoice_number,reference_invoice_date)
            SELECT @file,s.source_lineage_id,CONCAT(@store,':r025:',v.rowNo),v.type,@store,v.doc,v.d,v.ref,v.refDate
            FROM (VALUES{values}) {columns}
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=v.rowNo;
            INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,invoice_year,product_code,source_transaction_type,source_quantity,source_net_value,source_gross_value,
              source_cro_number,staff_name,matched_sales_line_id,match_status,source_lineage_id,content_key)
            SELECT 'R013',@store,v.d,v.doc,i.invoice_year,v.item,v.type,v.qty,v.amount/1.18,v.amount,v.cro,'Sample Staff '+v.cro,NULL,'Ambiguous',s.source_lineage_id,CONCAT(@store,':staff:',v.rowNo)
            FROM (VALUES{values}) {columns}
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc AND i.transaction_date=v.d
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Staff' AND s.source_row_number=v.rowNo
            WHERE v.cro IS NOT NULL;
            """);
        await database.ExecuteAsync(sql.ToString());
    }
}
