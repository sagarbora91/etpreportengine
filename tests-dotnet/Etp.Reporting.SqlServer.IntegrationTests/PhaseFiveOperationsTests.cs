using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveOperationsTests(SqlDatabaseFixture db) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Registers_preserve_eight_types_and_enforce_draft_verification_and_locked_day_roles()
    {
        await ExecuteAsync("CREATE USER p5_register_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER p5_register_manager; CREATE USER p5_register_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER p5_register_owner; CREATE USER p5_register_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER p5_register_viewer;");
        var types = new[] { "INWARD", "OUTWARD", "CREDIT_NOTE", "SERVICE_RECEIPT", "COURIER", "STOCK_TRANSFER", "EXPENSE", "VENDOR_INVOICE" };
        foreach (var type in types)
        {
            var id = await ExecuteAsync($"EXECUTE AS USER='p5_register_manager'; {Save(type, "DRAFT", "Initial receipt")}");
            Assert.True(Convert.ToInt64(id) > 0);
        }
        var rows = await SqlServerDigitalRegisterService.LoadDayAsync(db.ConnectionString, "P5REG", new(2026, 8, 25));
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row => Assert.Equal("DRAFT", row.VerificationStatus));
        foreach (var sql in new[] { Save("COURIER", "VERIFIED", "No authority"), "UPDATE dbo.register_entries SET verification_status='VERIFIED' WHERE store_code='P5REG'", "EXEC dbo.submit_controlled_adjustment 'P5REG','20260825','CORRECTION',1,N'Not owner'" })
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='p5_register_manager'; " + sql));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='p5_register_viewer'; " + Save("COURIER", "DRAFT", "Not permitted")));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='p5_register_owner'; " + Save("COURIER", "VERIFIED", " ")));
        await ExecuteAsync("EXECUTE AS USER='p5_register_owner'; " + Save("COURIER", "VERIFIED", "Courier evidence checked"));
        Assert.Equal("VERIFIED", await ExecuteAsync("SELECT verification_status FROM dbo.register_entries WHERE store_code='P5REG' AND register_type='COURIER'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='p5_register_manager'; " + Save("COURIER", "DRAFT", "Cannot unverify")));
        await ExecuteAsync("INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('P5REG','20260825','LOCKED',SUSER_SNAME(),SYSUTCDATETIME())");
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='p5_register_owner'; " + Save("INWARD", "VERIFIED", "Closed day")));
        Assert.Empty(await SqlServerDigitalRegisterService.LoadDayAsync(db.ConnectionString, "P5REG", new(2026,8,26)));
        var hit = Assert.Single(await new ProductisationRepository(db.ConnectionString).SearchAsync("TEST-COURIER"));
        Assert.Equal("register-courier",hit.TargetTaskId);
        Assert.Equal("P5REG",hit.StoreCode);
        Assert.Equal(new DateOnly(2026,8,25),hit.BusinessDate);
        Assert.True(hit.TargetId > 0);
    }

    [Fact]
    public async Task Restatement_approval_is_bound_to_source_scope_reason_and_consumed_with_atomic_import()
    {
        await ExecuteAsync("CREATE USER p5_requester WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER p5_requester; CREATE USER p5_decider WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER p5_decider;");
        var previous = Convert.ToInt64(await ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'original.xlsx',REPLICATE('a',64),1,'R025','P5REST','20260825','20260701','20260825',1);
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """));
        var requestSql = $"EXEC dbo.request_import_restatement {previous},'{new string('b',64)}','R025','P5REST','20260701','20260825',N'Correct source';";
        var request = Convert.ToInt64(await ExecuteAsync("EXECUTE AS USER='p5_requester'; " + requestSql));
        Assert.Equal(request, Convert.ToInt64(await ExecuteAsync("EXECUTE AS USER='p5_requester'; " + requestSql)));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='p5_requester'; EXEC dbo.decide_approval_request {request},1,N'Forgery'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='p5_requester'; UPDATE dbo.import_restatement_approvals SET request_reason=N'Changed' WHERE approval_request_id={request}"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(Replace(previous,'b',"Correct source")));
        Assert.Equal(0, await ExecuteAsync($"SELECT CONVERT(int,is_superseded) FROM dbo.import_files WHERE import_file_id={previous}"));
        await ExecuteAsync($"EXECUTE AS USER='p5_decider'; EXEC dbo.decide_approval_request {request},1,N'Checked hash and period' ");
        foreach (var sql in new[] { Replace(previous,'c',"Correct source"), Replace(previous,'b',"Different reason"), Replace(previous,'b',"Correct source", "20260702"), Replace(previous,'b',"Correct source", failAfterPreparation:true) })
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(sql));
        Assert.Equal(DBNull.Value, await ExecuteAsync($"SELECT applied_import_file_id FROM dbo.import_restatement_approvals WHERE approval_request_id={request}"));
        await ExecuteAsync("EXECUTE AS USER='p5_requester'; " + Replace(previous,'b',"Correct source"));
        Assert.Equal(1, await ExecuteAsync($"SELECT CONVERT(int,is_superseded) FROM dbo.import_files WHERE import_file_id={previous}"));
        Assert.NotEqual(DBNull.Value, await ExecuteAsync($"SELECT applied_import_file_id FROM dbo.import_restatement_approvals WHERE approval_request_id={request}"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(Replace(previous,'b',"Correct source")));
        var row = Assert.Single(await new ProductisationRepository(db.ConnectionString).LoadApprovalsAsync(null), x => x.Id == request);
        Assert.Equal("Correct source", row.RequestReason);
        Assert.Equal(new string('b',64), row.SourceFingerprint);
        Assert.Equal("Checked hash and period", row.DecisionReason);
        Assert.Equal("APPROVED", row.Status);
    }

    [Theory]
    [InlineData("REOPEN_DAY")]
    [InlineData("MASTER_MAPPING")]
    [InlineData("CONTROL_WAIVER")]
    public async Task Retired_types_cannot_be_requested(string type) =>
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXEC dbo.submit_approval_request '{type}','Unused','Unused',NULL,NULL,N'{{}}'"));

    private async Task<object?> ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(db.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static string Save(string type,string status,string reason) =>
        $"EXEC dbo.save_register_entry @type='{type}',@store='P5REG',@date='20260825',@number='TEST-{type}',@verification='{status}',@reason=N'{reason}';";
    private static string Replace(long previous,char hash,string reason,string start="20260701",bool failAfterPreparation=false) => $$"""
        SET XACT_ABORT ON; BEGIN TRANSACTION;
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Processing',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
        VALUES(@batch,'replacement.xlsx',REPLICATE('{{hash}}',64),1,'R025','P5REST','20260825','{{start}}','20260825',1);
        SET @file=SCOPE_IDENTITY();
        EXEC dbo.prepare_import_restatement {{previous}},@file,N'Untrusted caller actor',N'{{reason}}';
        {{(failAfterPreparation ? "THROW 51999,'Simulated persistence failure',1;" : "")}}
        COMMIT TRANSACTION;
        """;
}
