using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Control and tender identity (IF-006, migration 0041 section C2).
public sealed class SalesIdentitySqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Control_and_tender_indexes_reject_duplicates()
    {
        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@invoice bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic.xlsx',REPLICATE('c',64),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Revenue',1,'control'),(@file,'Revenue',2,'control'),(@file,'Tender',1,'tender'),(@file,'Tender',2,'tender'),(@file,'Tender',3,'tender');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('IDENTITY','I1',2027,'20260825');
            SET @invoice=SCOPE_IDENTITY();
            INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
            SELECT @invoice,'INV',1,100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND sheet_name='Revenue' AND source_row_number=1;
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT @invoice,'CASH',100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND sheet_name='Tender' AND source_row_number=1;
            """);

        var control = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("""
            INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'INV',1,100,'INR',l.source_lineage_id FROM dbo.sales_invoices i
            CROSS JOIN dbo.source_lineage l WHERE i.store_code='IDENTITY' AND l.sheet_name='Revenue' AND l.source_row_number=2
              AND l.import_file_id=(SELECT import_file_id FROM dbo.import_files WHERE source_sha256=REPLICATE('c',64));
            """));
        Assert.Equal(2601, control.Number);
        Assert.Contains("UX_sales_invoice_controls_invoice", control.Message);

        // persist_sales_tender compares the type without case, so the index must too.
        var tender = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Tender("cash", 2)));
        Assert.Equal(2601, tender.Number);
        Assert.Contains("UX_sales_tenders_invoice_type", tender.Message);

        await database.ExecuteAsync(Tender("CARD", 3));
        Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id WHERE i.store_code='IDENTITY'"));
        Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id WHERE i.store_code='IDENTITY'"));
    }

    private static string Tender(string type, int row) => $"""
        INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
        SELECT i.sales_invoice_id,'{type}',50,'INR',l.source_lineage_id FROM dbo.sales_invoices i
        CROSS JOIN dbo.source_lineage l WHERE i.store_code='IDENTITY' AND l.sheet_name='Tender' AND l.source_row_number={row}
          AND l.import_file_id=(SELECT import_file_id FROM dbo.import_files WHERE source_sha256=REPLICATE('c',64));
        """;
}
