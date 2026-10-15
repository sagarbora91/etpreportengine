using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane F1 (decision 68): the production money rules, cross-layer (C# repositories over the real SQL objects).
/// Synthetic store codes and amounts only; every expected figure is worked out in the comment beside it.
/// </summary>
public sealed class F1MoneyRulesSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // Decision 68 #3: I1 = 1,000 + 500; B1 cancels the 500 line only (BC qty -1) the same day; I2 = 800 cancelled whole by B2.
    [SqlFact]
    public async Task A_bill_partly_cancelled_the_same_day_stays_an_invoice_and_a_whole_cancel_does_not()
    {
        const string store = "F1PART";
        var day = new DateOnly(2033, 3, 7);
        await SeedAsync(store,
            new("I1", day, "INV", "ITEMA", "WB", 1, 1000),
            new("I1", day, "INV", "ITEMB", "WB", 1, 500),
            new("B1", day, "BC", "ITEMB", "WB", -1, -500, Ref: "I1"),
            new("I2", day, "INV", "ITEMC", "WB", 1, 800),
            new("B2", day, "BC", "ITEMC", "WB", -1, -800, Ref: "I2"));

        // Only I2 is listed: B1 takes back 1 of I1's 2 units, B2 all of I2's 1 unit. Before 1.12.1 both were listed.
        Assert.Equal("I2", await database.ExecuteAsync($"SELECT STRING_AGG(document_number,',') FROM dbo.v_sales_cancelled_same_day WHERE store_code='{store}'"));
        var ftd = (await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(day, [store])).Single(x => x.Period == "FTD" && x.Store == store);
        // VALUE 1,000 + 500 - 500 + 800 - 800 = 1,000; VOL 1; INVOICE 1 (I1; 0 before), so AVPT 1,000 and AUPT 1.
        Assert.Equal((1000m, 1m, 1, 1000m, 1m), (ftd.TySales, ftd.TyUnits, ftd.TyInvoices, ftd.Atv, ftd.Upt));
    }

    // A BC row whose own document is not in sales_invoices keeps the old whole-bill rule (the Tally read-back seed has this shape).
    [SqlFact]
    public async Task A_cancellation_whose_bc_document_is_not_imported_still_cancels_the_whole_bill()
    {
        const string store = "F1NOBC";
        var day = new DateOnly(2033, 3, 8);
        await SeedAsync(store, new Line("I1", day, "INV", "ITEMA", "WB", 1, 1000));
        await database.ExecuteAsync($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'f1-nobc.xlsx','{Guid.NewGuid():N}{Guid.NewGuid():N}',1,'{store}','R025','{day:yyyyMMdd}','{day:yyyyMMdd}','{day:yyyyMMdd}',1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sales',1,'sale');
            INSERT dbo.etp_r025(import_file_id,source_lineage_id,content_key,source_transaction_type,store_code,invoice_number,transaction_date,reference_invoice_number,reference_invoice_date)
            SELECT @file,s.source_lineage_id,'f1nobc',N'BC',N'{store}',N'BX9','{day:yyyyMMdd}',N'I1','{day:yyyyMMdd}' FROM dbo.source_lineage s WHERE s.import_file_id=@file;
            """);

        Assert.Equal("I1", await database.ExecuteAsync($"SELECT STRING_AGG(document_number,',') FROM dbo.v_sales_cancelled_same_day WHERE store_code='{store}'"));
    }

    // Decision 68 #1: gift cards, and line types the DSR does not count, are out of every store total. W9 = watch 3,000 +
    // gift card (brand GC) 500, both CRO C9; G9 = a gift-card-only bill 1,000; X9 = a line of type NEW, 200, no CRO.
    [SqlFact]
    public async Task Staff_sales_summary_customer_wise_and_trend_give_the_dsr_value_on_a_gift_card_day()
    {
        const string store = "F1GIFT";
        var day = new DateOnly(2033, 3, 9);
        await SeedAsync(store,
            new("W9", day, "INV", "WATCH9", "WB", 1, 3000, Cro: "C9"),
            new("W9", day, "INV", "GCX9", "GC", 1, 500, Cro: "C9"),
            new("G9", day, "INV", "GIFT CARD", "GC", 1, 1000),
            new("X9", day, "NEW", "ITEMX", "WB", 1, 200));
        var reports = new OperationalReportRepository(database.ConnectionString);
        var scope = new ReportingQueryScope(day, day, [store]);

        // DSR: VALUE 3,000, VOL 1, INVOICE 1 (W9), GIFT CARD 500 + 1,000 = 1,500.
        var ftd = (await reports.LoadDsrAsync(day, [store])).Single(x => x.Period == "FTD" && x.Store == store);
        Assert.Equal((3000m, 1m, 1, 1500m), (ftd.TySales, ftd.TyUnits, ftd.TyInvoices, ftd.TyGiftCards));

        // Staff: canonical 3,000 (was 4,700 = 3,000 + 500 + 1,000 + 200), C9 3,000 / 1 unit / 1 bill (was 3,500 / 2 units), Passed.
        var staff = await reports.LoadStaffPerformanceAsync(scope);
        Assert.Equal((3000m, 3000m, 0m, ReconciliationStatus.Passed), (staff.CanonicalSales, staff.AttributedSales, staff.Variance, staff.Status));
        var c9 = Assert.Single(staff.Rows);
        Assert.Equal((3000m, 1m, 1, 3000m, 100m), (c9.NetSales, c9.NetQuantity, c9.Transactions, c9.Atv, c9.ContributionPercent));

        // Sales summary (store and brand): 3,000 and 1 invoice (was 4,500 and 2; X9 was already skipped with a warning).
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(database.ConnectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var byStore = Assert.Single((await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Store)).Rows);
        Assert.Equal((3000m, 1), (byStore.SourceSignedNetAmount, byStore.Invoices));
        Assert.Equal(3000m, (await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Brand)).Rows.Sum(x => x.SourceSignedNetAmount));

        // Customer-wise: only W9, at 3,000 (was W9 3,500 + G9 1,000 + X9 200 = 4,700).
        var customer = Assert.Single(await reports.LoadInvoiceSummaryAsync(scope));
        Assert.Equal(("W9", "INV", 3000m, 1m), (customer.DocumentNumber, customer.TransactionTypes, customer.NetValue, customer.Quantity));

        // Management Trend: 3,000 (already) and 1 invoice (was 2: G9 counted).
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(day, day), x => x.StoreCode == store);
        Assert.Equal((3000m, 1), (trend.NetSales, trend.Invoices));
    }

    // Decision 68 #2 / decision 41: C7 sells K1 2,000 (cancelled whole the same day by KB1 -2,000) and K2 600 and K3 400.
    [SqlFact]
    public async Task A_bill_cancelled_the_same_day_is_not_a_cro_transaction_nor_a_trend_invoice()
    {
        const string store = "F1CANC";
        var day = new DateOnly(2033, 3, 10);
        await SeedAsync(store,
            new("K1", day, "INV", "ITEMK1", "WB", 1, 2000, Cro: "C7"),
            new("KB1", day, "BC", "ITEMK1", "WB", -1, -2000, Ref: "K1", Cro: "C7"),
            new("K2", day, "INV", "ITEMK2", "WB", 1, 600, Cro: "C7"),
            new("K3", day, "INV", "ITEMK3", "WB", 2, 400, Cro: "C7"));

        // C7: value 2,000 - 2,000 + 600 + 400 = 1,000; units 1 - 1 + 1 + 2 = 3; transactions K2, K3 = 2 (was 3: K1 counted),
        // so ATV 1,000 / 2 = 500 (was 333.33) and UPT 3 / 2 = 1.5 (was 1).
        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(day, day, [store]));
        var c7 = Assert.Single(staff.Rows);
        Assert.Equal((1000m, 3m, 2, 500m, 1.5m), (c7.NetSales, c7.NetQuantity, c7.Transactions, c7.Atv, c7.Upt));
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);

        // Trend: 1,000, 3 units, 2 invoices (was 3), 1 return (KB1). The DSR INVOICE agrees.
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(day, day), x => x.StoreCode == store);
        Assert.Equal((1000m, 3m, 2, 1), (trend.NetSales, trend.Units, trend.Invoices, trend.Returns));
        var ftd = (await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(day, [store])).Single(x => x.Period == "FTD" && x.Store == store);
        Assert.Equal(trend.Invoices, ftd.TyInvoices);
    }

    /// <summary>One synthetic sales line; <paramref name="Ref"/> is the R025 INVREFNO of a BC row (same day); <paramref name="Cro"/> adds a paired R013 row.</summary>
    private sealed record Line(string Doc, DateOnly Date, string Type, string Item, string Brand, decimal Qty, decimal Amount, string? Ref = null, string? Cro = null);

    /// <summary>One current R025 file covering the lines' dates: sales lines, an etp_r025 row per line, an R013 row per line with a CRO.</summary>
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
            VALUES(@batch,'f1-{store.ToLowerInvariant()}-r025.xlsx','{Guid.NewGuid():N}{Guid.NewGuid():N}',1,@store,'R025',{D(to)},{D(from)},{D(to)},1);
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
            $"{(x.Ref is null ? "CONVERT(nvarchar(80),NULL)" : $"N'{x.Ref}'")},{(x.Ref is null ? "CONVERT(date,NULL)" : $"CONVERT(date,{D(x.Date)})")}," +
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
