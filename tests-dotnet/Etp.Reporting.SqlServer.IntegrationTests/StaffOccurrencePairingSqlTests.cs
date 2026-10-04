using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.4, R-WLMHW-01 (report audit of 3 Oct 2026). R013 staff rows are paired with R025 sales lines by
/// occurrence: the nth R013 row for (store, date, document, item) takes the nth line, a line takes at most one
/// R013 row, and a surplus R013 row reads as Missing. The stored match_status (decided at import by counting
/// lines) no longer decides attribution. R003 (one row per discount) keeps its stored status.
/// </summary>
public sealed class StaffOccurrencePairingSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // One invoice per store; @lines R025 lines of one item and @staff R013 rows for it, stored with @status.
    private const string Seed = """
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic.xlsx',REPLICATE(@sha,64),1);
        SET @file=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
        SELECT @file,v.sheet,v.n,v.kind FROM (VALUES('Sales',1,'sale'),('Sales',2,'sale'),('Staff',1,'staff'),('Staff',2,'staff'),('Discount',1,'discount'),('Discount',2,'discount')) v(sheet,n,kind);
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,'D1',2027,@date);
        INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
        SELECT i.sales_invoice_id,CONVERT(nvarchar(10),s.source_row_number),'ITEMX','INV',1,
               CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @gross2 END,CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @gross2 END/1.18,
               CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @gross2 END-CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @gross2 END/1.18,'INR',s.source_lineage_id
        FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number<=@lines
        WHERE i.store_code=@store;
        INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,invoice_year,product_code,source_transaction_type,source_quantity,source_net_value,source_gross_value,
          source_cro_number,staff_name,matched_sales_line_id,match_status,source_lineage_id,content_key)
        SELECT CASE s.sheet_name WHEN 'Staff' THEN 'R013' ELSE 'R003' END,@store,@date,'D1',2027,'ITEMX','INV',1,
               CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @staffGross2 END/1.18,CASE s.source_row_number WHEN 1 THEN @gross1 ELSE @staffGross2 END,
               CASE WHEN s.sheet_name='Staff' THEN CASE s.source_row_number WHEN 1 THEN @cro1 ELSE @cro2 END END,
               CASE WHEN s.sheet_name='Staff' THEN 'Sample Staff' END,
               CASE WHEN @status='Matched' THEN (SELECT MIN(l.sales_line_id) FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code=@store) END,
               @status,s.source_lineage_id,CONCAT(@store,':',s.sheet_name,':',s.source_row_number)
        FROM dbo.source_lineage s WHERE s.import_file_id=@file AND s.sheet_name IN('Staff','Discount');
        """;

    private Task SeedAsync(string store, string sha, DateOnly date, int lines, decimal gross1, decimal gross2, decimal staffGross2,
        string cro1, string cro2, string status) =>
        database.ExecuteAsync($"""
            DECLARE @store varchar(30)='{store}',@sha char(1)='{sha}',@date date='{date:yyyyMMdd}',@lines int={lines},
                    @gross1 decimal(19,4)={gross1},@gross2 decimal(19,4)={gross2},@staffGross2 decimal(19,4)={staffGross2},
                    @cro1 nvarchar(80)='{cro1}',@cro2 nvarchar(80)='{cro2}',@status varchar(20)='{status}';
            {Seed}
            """);

    [Fact]
    public async Task Same_item_on_two_lines_pairs_each_staff_row_with_its_own_line()
    {
        // As imported in 1.9.3: two lines of ITEMX make both R013 rows Ambiguous, so the report dropped both.
        var date = new DateOnly(2026, 8, 10);
        await SeedAsync("OCCTWO", "c", date, lines: 2, gross1: 1180m, gross2: 590m, staffGross2: 590m, "CROA", "CROB", "Ambiguous");
        var scope = new ReportingQueryScope(date, date, ["OCCTWO"]);
        var reports = new OperationalReportRepository(database.ConnectionString);

        var staff = await reports.LoadStaffPerformanceAsync(scope);
        Assert.Equal(1770m, staff.CanonicalSales);
        Assert.Equal(1770m, staff.AttributedSales);
        Assert.Equal(0m, staff.Variance);
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        Assert.Equal(1180m, Assert.Single(staff.Rows, x => x.CroNumber == "CROA").NetSales);
        Assert.Equal(590m, Assert.Single(staff.Rows, x => x.CroNumber == "CROB").NetSales);

        var lineage = await reports.LoadInvoiceLineageAsync(scope);
        Assert.Equal(2, lineage.Count);
        Assert.Equal("CROA", Assert.Single(lineage, x => x.LineIdentifier == "1").CroNumber);
        Assert.Equal("CROB", Assert.Single(lineage, x => x.LineIdentifier == "2").CroNumber);

        // Both R013 rows are paired; the R003 rows keep their stored (Ambiguous) status and still count.
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(date, date), x => x.StoreCode == "OCCTWO");
        Assert.Equal(2, trend.UnmatchedEnrichmentRows);
    }

    [Fact]
    public async Task A_duplicate_staff_row_on_one_line_counts_once_and_the_surplus_reads_missing()
    {
        // As imported in 1.9.3: one line, two identical R013 rows, both stored Matched to that line (counted twice).
        var date = new DateOnly(2026, 8, 17);
        await SeedAsync("OCCDUP", "d", date, lines: 1, gross1: 1850m, gross2: 0m, staffGross2: 1850m, "CROC", "CROC", "Matched");
        var scope = new ReportingQueryScope(date, date, ["OCCDUP"]);
        var reports = new OperationalReportRepository(database.ConnectionString);

        var staff = await reports.LoadStaffPerformanceAsync(scope);
        Assert.Equal(1850m, staff.CanonicalSales);
        Assert.Equal(1850m, staff.AttributedSales);
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        var row = Assert.Single(staff.Rows);
        Assert.Equal(1850m, row.NetSales);
        Assert.Equal(1m, row.NetQuantity);
        Assert.Equal(1, row.Transactions);

        Assert.Equal("CROC", Assert.Single(await reports.LoadInvoiceLineageAsync(scope)).CroNumber);

        // Only the surplus R013 row is unmatched; the two R003 discount rows on the one line stay Matched.
        var trend = Assert.Single(await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(date, date), x => x.StoreCode == "OCCDUP");
        Assert.Equal(1, trend.UnmatchedEnrichmentRows);
    }
}
