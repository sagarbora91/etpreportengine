using System.Security.Cryptography;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveAccountingReceiptPrivacyTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Export_receipt_persists_only_filename_and_matches_fresh_history()
    {
        var id = Convert.ToInt64(await db.ExecuteAsync("""
            UPDATE dbo.product_settings SET tally_company_name=N'TEST Receipt',tally_environment_label='TEST';
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('F17','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
            DECLARE @generation bigint=SCOPE_IDENTITY();
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,approval_reason)
            VALUES('F17','20260825',@generation,1,1,1,'APPROVED_READY',SUSER_SNAME(),N'Fixture approved');
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """));
        var folder = Path.Combine(Path.GetTempPath(), "EtpReceiptPrivacy", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "batch.xml");
        try
        {
            var receipt = await new ProductisationRepository(db.ConnectionString).ExportAccountingBatchAsync(id, path, new("TEST Receipt", "TEST"), async token =>
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(path, "<fixture />", token);
                return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, token))).ToLowerInvariant();
            });
            Assert.Equal("batch.xml", receipt.OutputPath);
            Assert.Equal("batch.xml", await db.ExecuteAsync($"SELECT output_path FROM dbo.accounting_export_receipts WHERE accounting_batch_id={id}"));
            Assert.Equal(receipt, Assert.Single(await new SqlServerAccountingService(db.ConnectionString).LoadExportHistoryAsync()));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(), receipt.Sha256);
            Assert.NotEqual(DBNull.Value, await db.ExecuteAsync("SELECT COL_LENGTH('dbo.accounting_batches','tally_reference')"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
