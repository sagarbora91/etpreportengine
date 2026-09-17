using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class SalesTransactionConsistencyTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Sales_summary_and_dsr_exclude_unknown_types_without_blocking_known_signed_sales()
    {
        // Simulates legacy facts: today's import already warns and skips unknown types.
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes)
            VALUES(@batch,'synthetic-types.xlsx',REPLICATE('f',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Sales',3,'sale'),(@file,'Sales',4,'sale'),(@file,'Sales',5,'sale');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('UNKNOWN-TYPE','1',2027,'20260825'),('UNKNOWN-TYPE','2',2027,'20260825'),('UNKNOWN-TYPE','3',2027,'20260825'),('UNKNOWN-TYPE','4',2027,'20260825'),('UNKNOWN-TYPE','5',2027,'20260825');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','Synthetic item',v.kind,v.quantity,v.gross,v.net,v.gross-v.net,'INR',s.source_lineage_id
            FROM (VALUES(1,'INV',2,236,200),(2,'SR',-1,-118,-100),(3,'BC',-1,-59,-50),(4,'NEW',99,9900,9000),(5,NULL,99,9900,9000)) v(row_number,kind,quantity,gross,net)
            JOIN dbo.sales_invoices i ON i.document_number=CONVERT(varchar(10),v.row_number) AND i.store_code='UNKNOWN-TYPE'
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.source_row_number=v.row_number;
            """);
        var scope = new ReportingQueryScope(new(2026, 8, 25), new(2026, 8, 25), ["UNKNOWN-TYPE"]);
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(database.ConnectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var summary = await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Store);
        var dsr = Assert.Single(await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(scope.DateTo, scope.StoreCodes),
            row => row.Store == "UNKNOWN-TYPE" && row.Period == "FTD");
        Assert.Equal(ReconciliationStatus.Passed, summary.Status);
        Assert.Equal(59m, Assert.Single(summary.Rows).SourceSignedNetAmount);
        Assert.Equal(59m, dsr.TySales);
        Assert.Equal(0m, summary.Rows[0].SourceSignedQuantity);
        Assert.Equal(0m, dsr.TyUnits);
        Assert.Equal(1, dsr.TyInvoices);
        Assert.Contains("skipped 2 rows", summary.Message);
    }
}
