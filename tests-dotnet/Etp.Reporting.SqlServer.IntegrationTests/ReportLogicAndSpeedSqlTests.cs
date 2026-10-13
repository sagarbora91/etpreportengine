using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.2, lane L5. Cross-layer: realistic values go through the C# repositories and services into the SQL objects of 0059
/// (lane L1's v_invoice_customer_names) and 0060 (the pack setting, v_sales_cancelled_same_day). Synthetic data only
/// ("Sample Customer NN"). Impersonation runs on non-pooled connections and REVERTs in the same batch.
/// Needs L1's 0059 merged (the invoice summary reads its view).
/// </summary>
public sealed class ReportLogicAndSpeedSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Customer = "Sample Customer 07";

    // One INV of 1,000 on @date for @store: an R025 file with the sales line and an R024 file with the invoice's customer.
    private const string NameSeed = """
        DECLARE @batch uniqueidentifier=NEWID(),@sales bigint,@names bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
        VALUES(@batch,'names-r025.xlsx',REPLICATE('a',64),1,@store,'R025',@date,@date,@date,1);
        SET @sales=SCOPE_IDENTITY();
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
        VALUES(@batch,'names-r024.xlsx',REPLICATE('b',64),1,@store,'R024',@date,@date,@date,1);
        SET @names=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@sales,'Sales',1,'sale'),(@names,'Customers',1,'customer');
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,'N1',2031,@date);
        INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
        SELECT i.sales_invoice_id,'1','ITEMN','INV',1,1000,847.4576,152.5424,'INR',s.source_lineage_id
        FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@sales WHERE i.store_code=@store;
        INSERT dbo.etp_r024(import_file_id,source_lineage_id,content_key,trans_type,store_code,inv_number,inv_date,customer_name)
        SELECT @names,s.source_lineage_id,'names-1','INV',@store,'N1',@date,@customer FROM dbo.source_lineage s WHERE s.import_file_id=@names;
        """;

    [SqlFact]
    public async Task Invoice_names_follow_the_role_and_the_shared_pack_blanks_them_until_the_owner_switches_the_setting_on()
    {
        const string store = "NAMEPK";
        var date = new DateOnly(2031, 3, 4);
        await database.ExecuteAsync($"DECLARE @store varchar(30)='{store}',@date date='{date:yyyyMMdd}',@customer nvarchar(200)=N'{Customer}';\n{NameSeed}");
        await new Phase2OperationsRepository(database.ConnectionString).UpsertMasterValueAsync("STORE", store, "Name pack shop", "APPROVED", true, "Synthetic name test");
        await EnsureUsersAsync();
        var scope = new ReportingQueryScope(date, date, [store]);

        // C# -> SQL as the database owner (the Owner's own login): the name is read.
        var reports = new OperationalReportRepository(database.ConnectionString);
        Assert.Equal(Customer, Assert.Single(await reports.LoadInvoiceSummaryAsync(scope)).CustomerName);

        // The same SQL text by role. A Viewer gets the invoice and no name, also once the R024 customer column is denied to it
        // (0059's rule, repeated here so the test holds on its own): the name is read only through the by-role view.
        await database.ExecuteAsync("DENY SELECT ON dbo.etp_r024(customer_name) TO etp_viewer;");
        foreach (var (user, expected) in new[] { ("l5_owner", Customer), ("l5_manager", Customer), ("l5_viewer", (string?)null) })
        {
            var row = Assert.Single(await InvoiceSummaryAsAsync(user, store, date));
            Assert.Equal(1000m, row.Value);
            Assert.Equal(expected, row.Name);
        }

        // The pack setting starts off: the generated pack (and its archive) carries the Customer column blank.
        var packs = new DailyReportingPackService(database.ConnectionString);
        Assert.False(await packs.LoadSharePackCustomerNamesAsync());
        Assert.Null(CustomerCell(await packs.GenerateAsync(store, date, "test")));

        // Only the Owner may switch it; a Store Manager or Viewer is refused by SQL, whatever the app shows.
        foreach (var user in new[] { "l5_manager", "l5_viewer" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsAsync(user, DailyReportingPackService.SetSharePackCustomerNamesSql,
                ("@include", true), ("@reason", "Synthetic refusal")));
            Assert.Equal(51060, refused.Number);
        }
        Assert.False(await packs.LoadSharePackCustomerNamesAsync());

        await packs.SetSharePackCustomerNamesAsync(true, "Synthetic: names in packs for the test");
        Assert.True(await packs.LoadSharePackCustomerNamesAsync());
        Assert.Equal(Customer, CustomerCell(await packs.GenerateAsync(store, date, "test")));
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(
            "SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='ConfigurationChange' AND safe_detail=N'Shared pack customer names setting changed'")));

        await packs.SetSharePackCustomerNamesAsync(false, "Synthetic: back to the default");
        Assert.Null(CustomerCell(await packs.GenerateAsync(store, date, "test")));
    }

    [SqlFact]
    public async Task Staff_sales_equal_the_store_total_when_R013_states_a_different_value_for_a_return()
    {
        // As in AUDIT-1 D6: R013 states the return +620 while R025 says -500. Staff sales come from the paired R025 line.
        const string store = "STAFFD6";
        var date = new DateOnly(2031, 4, 7);
        await database.ExecuteAsync($"""
            DECLARE @store varchar(30)='{store}',@date date='{date:yyyyMMdd}',@batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'staff-d6.xlsx',REPLICATE('c',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Staff',1,'staff'),(@file,'Staff',2,'staff');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,'I1',2031,@date),(@store,'R1',2031,@date);
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','ITEMS',v.type,v.qty,v.amount,v.amount/1.18,v.amount-v.amount/1.18,'INR',s.source_lineage_id
            FROM (VALUES('I1','INV',1,1180.0,1),('R1','SR',-1,-500.0,2)) v(doc,type,qty,amount,rowNo)
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=v.rowNo;
            INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,invoice_year,product_code,source_transaction_type,source_quantity,source_net_value,source_gross_value,
              source_cro_number,staff_name,matched_sales_line_id,match_status,source_lineage_id,content_key)
            SELECT 'R013',@store,@date,v.doc,2031,'ITEMS',v.type,v.qty,v.gross/1.18,v.gross,'CROD6','Sample Staff',NULL,'Ambiguous',s.source_lineage_id,CONCAT(@store,':staff:',v.rowNo)
            FROM (VALUES('I1','INV',1,1180.0,1),('R1','SR',-1,620.0,2)) v(doc,type,qty,gross,rowNo)
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Staff' AND s.source_row_number=v.rowNo;
            """);

        var staff = await new OperationalReportRepository(database.ConnectionString).LoadStaffPerformanceAsync(new(date, date, [store]));

        Assert.Equal(680m, staff.CanonicalSales);
        Assert.Equal(680m, staff.AttributedSales); // 1,800 (1,180 + 620) before 1.11.2
        Assert.Equal(0m, staff.Variance);
        Assert.Equal(ReconciliationStatus.Passed, staff.Status);
        var row = Assert.Single(staff.Rows);
        Assert.Equal((680m, 0m, 1), (row.NetSales, row.NetQuantity, row.Transactions));
    }

    [SqlFact]
    public async Task A_bill_cancelled_the_same_day_is_not_a_DSR_invoice_but_one_cancelled_later_still_is()
    {
        // Decision 41. I1 is cancelled by B1 on its own day (R025 INVREFNO = I1); I3 is cancelled by B2 two days later.
        const string store = "CANCEL41";
        var day = new DateOnly(2031, 5, 10);
        await database.ExecuteAsync($"""
            DECLARE @store varchar(30)='{store}',@d1 date='{day:yyyyMMdd}',@d2 date='{day.AddDays(1):yyyyMMdd}',@d3 date='{day.AddDays(2):yyyyMMdd}',
                    @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
            VALUES(@batch,'cancel-r025.xlsx',REPLICATE('d',64),1,@store,'R025',@d3,'20310501',@d3,1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            SELECT @file,'Sales',n,'sale' FROM (VALUES(1),(2),(3),(4),(5),(6),(7)) v(n);
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES(@store,'I1',2031,@d1),(@store,'I2',2031,@d1),(@store,'B1',2031,@d1),(@store,'I3',2031,@d2),(@store,'B2',2031,@d3);
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,CONVERT(nvarchar(10),v.rowNo),'ITEMC',v.type,v.qty,v.amount,v.amount,0,'INR',s.source_lineage_id
            FROM (VALUES('I1','INV',1,1000.0,1),('I1','INV',1,500.0,2),('I2','INV',1,700.0,3),('B1','BC',-1,-1000.0,4),('B1','BC',-1,-500.0,5),
                         ('I3','INV',1,300.0,6),('B2','BC',-1,-300.0,7)) v(doc,type,qty,amount,rowNo)
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=v.rowNo;
            INSERT dbo.etp_r025(import_file_id,source_lineage_id,content_key,source_transaction_type,store_code,invoice_number,transaction_date,reference_invoice_number,reference_invoice_date)
            SELECT @file,s.source_lineage_id,CONCAT('c',v.rowNo),v.type,@store,v.doc,v.d,v.ref,v.refDate
            FROM (VALUES('I1','INV',@d1,NULL,NULL,1),('I1','INV',@d1,NULL,NULL,2),('I2','INV',@d1,NULL,NULL,3),('B1','BC',@d1,'I1',@d1,4),('B1','BC',@d1,'I1',@d1,5),
                         ('I3','INV',@d2,NULL,NULL,6),('B2','BC',@d3,'I3',@d2,7)) v(doc,type,d,ref,refDate,rowNo)
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=v.rowNo;
            """);

        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.v_sales_cancelled_same_day WHERE store_code='{store}'")));
        var dsr = await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(day.AddDays(2), [store]);
        var mtd = dsr.Single(x => x.Period == "MTD" && x.Store == store);
        Assert.Equal(700m, mtd.TySales);   // 1,000 + 500 + 700 + 300 - 1,500 - 300: value is unchanged by the rule
        Assert.Equal(2, mtd.TyInvoices);   // I2 and I3; I1 (cancelled the same day) is left out. 3 before 1.11.2.
        Assert.Equal(0, dsr.Single(x => x.Period == "FTD" && x.Store == store).TyInvoices);
    }

    [SqlFact]
    public async Task Source_tender_shortfall_lists_only_short_documents_with_every_tender_row_counted()
    {
        const string store = "TSHORT";
        var date = new DateOnly(2031, 6, 3);
        await database.ExecuteAsync($"""
            DECLARE @store varchar(30)='{store}',@date date='{date:yyyyMMdd}',@batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'tender-short.xlsx',REPLICATE('e',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            SELECT @file,v.sheet,v.n,'row' FROM (VALUES('Revenue',1),('Revenue',2),('Revenue',3),('Revenue',4),('Tender',1),('Tender',2),('Tender',3),('Tender',4),('Tender',5),('Tender',6)) v(sheet,n);
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,'T1',2031,@date),(@store,'T2',2031,@date),(@store,'T3',2031,@date),(@store,'T4',2031,@date);
            INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'INV',1,v.control,'INR',s.source_lineage_id
            FROM (VALUES('T1',1000.0,1),('T2',500.0,2),('T3',300.0,3),('T4',200.0,4)) v(doc,control,rowNo)
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Revenue' AND s.source_row_number=v.rowNo;
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,is_reporting_eligible,exclusion_reason,source_lineage_id)
            SELECT i.sales_invoice_id,v.tender,v.amount,'INR',v.eligible,CASE WHEN v.eligible=0 THEN N'Synthetic exclusion' END,s.source_lineage_id
            FROM (VALUES('T1','CASH',900.0,1,1),('T2','CASH',400.0,1,2),('T2','CARD',100.0,0,3),('T3','CASH',200.0,1,4),('T3','CARD',100.0,1,5),
                         ('T4','CASH',250.0,1,6)) v(doc,tender,amount,eligible,rowNo)
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.document_number=v.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Tender' AND s.source_row_number=v.rowNo;
            """);

        var rows = await new OperationalReportRepository(database.ConnectionString).LoadTenderSourceShortfallsAsync(new(date, date, [store]));

        // T1: the export pays 100 short. T2: short only because a row was excluded (an ETP rule, not the source).
        // T3 balances only when both of its tender rows (CASH 200 + CARD 100) are counted; T4 is an excess.
        // One row per invoice and tender type (UX_sales_tenders_invoice_type, 0041), so a split payment uses two types.
        var row = Assert.Single(rows);
        Assert.Equal(("T1", 100m), (row.DocumentNumber, row.Shortfall));
    }

    private async Task EnsureUsersAsync() => await database.ExecuteAsync("""
        IF USER_ID(N'l5_viewer') IS NULL BEGIN CREATE USER l5_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER l5_viewer; END;
        IF USER_ID(N'l5_manager') IS NULL BEGIN CREATE USER l5_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER l5_manager; END;
        IF USER_ID(N'l5_owner') IS NULL BEGIN CREATE USER l5_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER l5_owner; ALTER ROLE db_owner ADD MEMBER l5_owner; END;
        """);

    private static object? CustomerCell(DailyReportPackResult pack)
    {
        var table = pack.Document.Tables.Single(x => x.Name == "Invoice Summary");
        var column = table.Data.Columns.Select(x => x.Header).ToList().IndexOf("Customer");
        return table.Data.Rows.Single()[column];
    }

    private SqlConnection NonPooled() => new(TestSqlConnections.ForDatabase(database.Name, pooling: false));

    private static string As(string user, string sql) =>
        $"EXECUTE AS USER='{user}'; BEGIN TRY {sql} END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;";

    private async Task<List<(decimal Value, string? Name)>> InvoiceSummaryAsAsync(string user, string store, DateOnly date)
    {
        await using var connection = NonPooled();
        await connection.OpenAsync();
        await using var command = new SqlCommand(As(user, OperationalReportRepository.InvoiceSummarySql), connection);
        command.Parameters.AddWithValue("@from", date);
        command.Parameters.AddWithValue("@to", date);
        command.Parameters.AddWithValue("@stores", $"[\"{store}\"]");
        var rows = new List<(decimal, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add((reader.GetDecimal(5), reader.IsDBNull(7) ? null : reader.GetString(7)));
        return rows;
    }

    private async Task ExecuteAsAsync(string user, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = NonPooled();
        await connection.OpenAsync();
        await using var command = new SqlCommand(As(user, sql), connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
