using System.Security.Cryptography;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class AccountingFoundationTests
{
    [Fact]
    public async Task Missing_company_blocks_saved_batch_and_rejection_releases_invoice_for_replacement()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync(); var service = await SeedAsync(db);
            var scope = new AccountingScope("ACCOUNTING",new(2026,8,25));
            var preview = await service.PreviewAsync(scope);
            var first = await service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch));
            var blocked = Assert.Single(await service.LoadBatchesAsync());
            Assert.Equal("BLOCKED",blocked.Status); Assert.Contains("D12",blocked.BlockingReason);
            await Assert.ThrowsAsync<SqlException>(()=>service.ApproveAsync(new(first,"Checked")));
            Assert.Equal(DBNull.Value,await db.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={first}"));
            var duplicate = await Assert.ThrowsAsync<SqlException>(()=>service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch)));
            Assert.Contains($"batch {first}",duplicate.Message);
            await service.SaveDestinationAsync(new("TEST Accounting","TEST","","Test destination"));
            await service.RejectAsync(new(first,"Company now configured; prepare again"));
            Assert.Equal(0,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE is_active=1"));
            var next = await service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch));
            await service.ApproveAsync(new(next,"Compared with source"));
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE is_active=1"));
            await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='DRAFT' WHERE accounting_batch_id={first}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync($"UPDATE dbo.accounting_batch_invoices SET is_active=0 WHERE accounting_batch_id={next}"));
            await Assert.ThrowsAsync<SqlException>(()=>service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch)));
            Assert.Equal(2,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batches"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Concurrent_prepare_reserves_one_invoice_and_export_receipt_matches_bytes_and_settings()
    {
        var db = new SqlDatabaseFixture(); var folder = Path.Combine(Path.GetTempPath(),"EtpAccounting_"+Guid.NewGuid().ToString("N"));
        try
        {
            await db.InitializeAsync(); var service = await SeedAsync(db);
            await service.SaveDestinationAsync(new("TEST Accounting","TEST","","Test destination"));
            var scope = new AccountingScope("ACCOUNTING",new(2026,8,25)); var preview = await service.PreviewAsync(scope);
            async Task<long?> TrySave()
            {
                try { return await service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch)); }
                catch(SqlException error) { Assert.Equal(51452,error.Number); return null; }
            }
            var outcomes = await Task.WhenAll(TrySave(),TrySave()); var id=Assert.Single(outcomes,x=>x.HasValue)!.Value;
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE is_active=1"));
            await Assert.ThrowsAsync<ArgumentException>(()=>service.ApproveAsync(new(id," ")));
            Assert.Equal(DBNull.Value,await db.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={id}"));
            await service.ApproveAsync(new(id,"Source reviewed"));
            var path = Path.Combine(folder,"batch.xml");
            var receipt = await service.ExportAsync(new(id,"",path));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant(),receipt.Sha256);
            Assert.Equal("TEST Accounting",receipt.CompanyName); Assert.Equal("TEST",receipt.EnvironmentLabel);
            Assert.Contains("TEST Accounting",await File.ReadAllTextAsync(path));
            var history = Assert.Single(await new SqlServerAccountingService(db.ConnectionString).LoadExportHistoryAsync());
            Assert.Equal(receipt,history);
            Assert.Equal("EXPORTED_AWAITING_IMPORT",Assert.Single(await service.LoadBatchesAsync()).Status);
            await Assert.ThrowsAsync<SqlException>(()=>service.RejectAsync(new(id,"Too late")));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ExportAsync(new(id,"",Path.Combine(folder,"second.xml"))));
            await Assert.ThrowsAsync<SqlException>(()=>service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch)));
            await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync("UPDATE dbo.accounting_export_receipts SET tally_company_name='Changed'"));
            await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync("DELETE dbo.accounting_export_receipts"));
            await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync($"UPDATE dbo.accounting_entries SET narration='Changed' WHERE accounting_batch_id={id}"));
            foreach(var role in new[]{"etp_store_manager","etp_viewer"})
            {
                await db.ExecuteAsync($"CREATE USER accounting_probe WITHOUT LOGIN; ALTER ROLE {role} ADD MEMBER accounting_probe;");
                foreach(var sql in new[]{"SELECT * FROM dbo.accounting_export_receipts", "SELECT * FROM dbo.accounting_batches", $"UPDATE dbo.accounting_batch_invoices SET is_active=0 WHERE accounting_batch_id={id}"})
                    await Assert.ThrowsAsync<SqlException>(()=>db.ExecuteAsync($"EXECUTE AS USER='accounting_probe'; BEGIN TRY {sql}; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;"));
                await db.ExecuteAsync("DROP USER accounting_probe;");
            }
        }
        finally { await db.DisposeAsync(); if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }

    [Fact]
    public async Task Receipt_failure_rolls_back_export_and_removes_new_file_without_overwriting_existing_file()
    {
        var db = new SqlDatabaseFixture(); var folder = Path.Combine(Path.GetTempPath(),"EtpAccounting_"+Guid.NewGuid().ToString("N"));
        try
        {
            await db.InitializeAsync(); var service = await SeedAsync(db); var id=await PrepareApprovedAsync(service);
            var path=Path.Combine(folder,"batch.xml"); Directory.CreateDirectory(folder); await File.WriteAllTextAsync(path,"existing evidence");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ExportAsync(new(id,"",path)));
            Assert.Equal("existing evidence",await File.ReadAllTextAsync(path)); File.Delete(path);
            await db.ExecuteAsync("CREATE TRIGGER dbo.fail_receipt ON dbo.accounting_export_receipts AFTER INSERT AS THROW 51990,'Fixture receipt failure',1;");
            await Assert.ThrowsAsync<SqlException>(()=>service.ExportAsync(new(id,"",path)));
            Assert.False(File.Exists(path)); Assert.Empty(await service.LoadExportHistoryAsync());
            Assert.Equal("APPROVED_READY",Assert.Single(await service.LoadBatchesAsync()).Status);
            await db.ExecuteAsync("DROP TRIGGER dbo.fail_receipt;");
            await service.ExportAsync(new(id,"",path)); Assert.True(File.Exists(path));
        }
        finally { await db.DisposeAsync(); if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }

    [Fact]
    public async Task Rejection_cannot_overtake_an_export_and_rejected_batch_cannot_be_exported()
    {
        var db = new SqlDatabaseFixture(); var folder=Path.Combine(Path.GetTempPath(),"EtpAccounting_"+Guid.NewGuid().ToString("N"));
        try
        {
            await db.InitializeAsync(); var service=await SeedAsync(db); var id=await PrepareApprovedAsync(service);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var path=Path.Combine(folder,"batch.xml");
            var export = new ProductisationRepository(db.ConnectionString).ExportAccountingBatchAsync(id,path,new("TEST Accounting","TEST"),async token=>
            {
                entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20),token);
                Directory.CreateDirectory(folder); await File.WriteAllTextAsync(path,"fixture file",token);
                return Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path,token))).ToLowerInvariant();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var rejection=service.RejectAsync(new(id,"Racing rejection"));
            release.SetResult(); await export;
            await Assert.ThrowsAsync<SqlException>(()=>rejection);
            Assert.Equal("EXPORTED_AWAITING_IMPORT",Assert.Single(await service.LoadBatchesAsync()).Status);
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE is_active=1"));
        }
        finally { await db.DisposeAsync(); if(Directory.Exists(folder)) Directory.Delete(folder,true); }
    }

    [Fact]
    public async Task Missing_mapping_is_persisted_as_blocked_and_explains_the_required_action()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync(); var service=await SeedAsync(db);
            await service.SaveDestinationAsync(new("TEST Accounting","TEST","","Test destination"));
            await db.ExecuteAsync("UPDATE dbo.accounting_mappings SET is_active=0;");
            var scope=new AccountingScope("ACCOUNTING",new(2026,8,25)); var preview=await service.PreviewAsync(scope);
            Assert.Equal(new[]{"ADJUSTMENT"},preview.Batch.MissingMappings);
            var id=await service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch));
            var batch=Assert.Single(await service.LoadBatchesAsync()); Assert.Equal("BLOCKED",batch.Status);
            Assert.Contains("ADJUSTMENT",batch.BlockingReason);
            await Assert.ThrowsAsync<SqlException>(()=>service.ApproveAsync(new(id,"Must not approve missing mapping")));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Legacy_status_upgrade_retains_amounts_reasons_and_checksums_and_backfills_invoice_reservations()
    {
        var name="EtpPhase0Test_AccountingUpgrade_"+Guid.NewGuid().ToString("N");
        var connectionString=TestSqlConnections.ForDatabase(name);
        var source=new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory,"database","migrations"));
        async Task<object?> Execute(string sql)
        {
            await using var connection=new SqlConnection(connectionString); await connection.OpenAsync();
            await using var command=new SqlCommand(sql,connection); return await command.ExecuteScalarAsync();
        }
        try
        {
            await new SqlServerDatabaseBootstrapper(connectionString,new BeforeAccountingFoundation(source)).BootstrapAsync();
            var store=new SqlServerMigrationStore(connectionString); var before=await store.GetAppliedAsync();
            await Execute("""
                INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
                VALUES('UPGRADE','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1),('UPGRADE','20260826',1,REPLICATE('b',64),N'{}',SUSER_SNAME(),1),('UPGRADE','20260827',1,REPLICATE('c',64),N'{}',SUSER_SNAME(),1);
                INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('UPGRADE','INV-25',2027,'20260825'),('UPGRADE','INV-26',2027,'20260826'),('UPGRADE','INV-27',2027,'20260827');
                INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,approval_reason)
                SELECT 'UPGRADE',business_date,daily_report_generation_id,1,25,25,CASE DAY(business_date) WHEN 25 THEN 'REVIEW' WHEN 26 THEN 'APPROVED' ELSE 'EXPORTED' END,SUSER_SNAME(),CASE WHEN DAY(business_date)=26 THEN 'Legacy approval' ELSE NULL END FROM dbo.daily_report_generations;
                """);
            await new MigrationRunner(source,store).RunAsync();
            Assert.Equal("DRAFT,APPROVED_READY,EXPORTED_AWAITING_IMPORT",await Execute("SELECT STRING_AGG(status,',') WITHIN GROUP(ORDER BY business_date) FROM dbo.accounting_batches"));
            Assert.Equal(75m,await Execute("SELECT SUM(debit_total) FROM dbo.accounting_batches"));
            Assert.Equal("Legacy approval",await Execute("SELECT approval_reason FROM dbo.accounting_batches WHERE status='APPROVED_READY'"));
            Assert.Equal(3,await Execute("SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE is_active=1"));
            Assert.Equal(0,await Execute("SELECT COUNT(*) FROM dbo.accounting_export_receipts"));
            var after=await store.GetAppliedAsync(); foreach(var old in before) Assert.Equal(old,Assert.Single(after,x=>x.Id==old.Id));
            Assert.Empty(await new MigrationRunner(source,store).RunAsync());
        }
        finally
        {
            if(!name.StartsWith("EtpPhase0Test_AccountingUpgrade_",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name");
            SqlConnection.ClearAllPools(); var master=new SqlConnectionStringBuilder(connectionString){InitialCatalog="master"};
            await using var connection=new SqlConnection(master.ConnectionString); await connection.OpenAsync();
            await using var command=new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class BeforeAccountingFoundation(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token=default) =>
            (await source.DiscoverAsync(token)).Where(x=>string.CompareOrdinal(x.Id,"0033")<0).ToArray();
    }

    private static async Task<SqlServerAccountingService> SeedAsync(SqlDatabaseFixture db)
    {
        await db.ExecuteAsync("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('ACCOUNTING','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('ACCOUNTING','INV-1',2027,'20260825');
            """);
        var id=Convert.ToInt64(await db.ExecuteAsync("EXEC dbo.submit_controlled_adjustment 'ACCOUNTING','20260825','CORRECTION',25,N'Fixture';"));
        var request=await db.ExecuteAsync($"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={id}");
        await db.ExecuteAsync($"EXEC dbo.decide_approval_request {request},1,N'Fixture checked';");
        var service=new SqlServerAccountingService(db.ConnectionString);
        await service.ApproveMappingAsync(new(new("ACCOUNTING",new(2026,8,25)),"ADJUSTMENT","Adjustment expense","Adjustment control","{description}","Fixture mapping"));
        return service;
    }

    private static async Task<long> PrepareApprovedAsync(SqlServerAccountingService service)
    {
        await service.SaveDestinationAsync(new("TEST Accounting","TEST","","Test destination"));
        var scope=new AccountingScope("ACCOUNTING",new(2026,8,25)); var preview=await service.PreviewAsync(scope);
        var id=await service.SaveAsync(new(scope,preview.ReportGenerationId,preview.Batch)); await service.ApproveAsync(new(id,"Reviewed")); return id;
    }
}
