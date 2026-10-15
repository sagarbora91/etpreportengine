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
        await SeedAsync(store, new("I1", day, "INV", "ITEMA", "WB", 1, 1000));
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
