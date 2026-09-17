using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// Regression for the DSR crash found on the acceptance VM. An R025 import supplies
/// source_net_amount but leaves source_gross_amount NULL. SUM over an all-NULL group
/// returns NULL, and the unguarded GetDecimal that read it threw SqlNullValueException,
/// which failed the whole Daily Sales Report rather than one row.
/// </summary>
public sealed class NullAmountReportResilienceTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day = new(2033, 3, 3);

    [Fact]
    public async Task Daily_sales_report_renders_when_every_amount_in_a_brand_group_is_null()
    {
        await db.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(); DECLARE @file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date)
            VALUES(@batch,'null-gross-r025.xlsx',REPLICATE('e',64),1,'WLMHW','R025','20330303');
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sales',1,'sale');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('WLMHW','NULLGROSS-1',2033,'20330303');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,
                                   source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1','ITEM','INV',1,NULL,1000,'INR',s.source_lineage_id
            FROM dbo.sales_invoices i
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=1
            WHERE i.store_code='WLMHW' AND i.document_number='NULLGROSS-1';
            """);

        // Before the fix this threw System.Data.SqlTypes.SqlNullValueException.
        var document = await new OperationalReportRepository(db.ConnectionString)
            .LoadDailySalesReportDocumentAsync(Day);

        // The sheet is genuinely built, not an empty shell returned by a swallowed error.
        var sheet = document.EveningSheets.Single(x => x.StoreCode == "WLMHW");
        Assert.Contains(sheet.Rows, x => x.Metric == "VALUE");
        Assert.Contains(sheet.Rows, x => x.Metric == "VOL");

        // The line carries no brand code, so it lands in the unmapped row. That row is
        // present and reports no amount rather than inventing one: the source supplied
        // no gross value, and "unknown" is the truthful reading, not zero.
        var unmapped = sheet.Rows.SingleOrDefault(x => x.Metric == "Other / unmapped");
        Assert.NotNull(unmapped);
        Assert.Null(unmapped.Ftd);
    }
}
