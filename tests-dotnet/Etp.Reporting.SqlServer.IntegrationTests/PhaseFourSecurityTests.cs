using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFourSecurityTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Locked_day_rejects_raw_reopen_delete_and_key_move_even_with_legacy_writer_permissions()
    {
        await ExecuteAsync("CREATE USER phase4_writer WITHOUT LOGIN; ALTER ROLE db_datawriter ADD MEMBER phase4_writer; ALTER ROLE db_datareader ADD MEMBER phase4_writer;");
        await SeedDay("LOCKGUARD");
        foreach (var action in new[] {
            "UPDATE dbo.daily_reporting_days SET status='OPEN',reopen_reason=N'Forged approval' WHERE store_code='LOCKGUARD'",
            "DELETE dbo.daily_reporting_days WHERE store_code='LOCKGUARD'",
            "UPDATE dbo.daily_reporting_days SET business_date='20260826' WHERE store_code='LOCKGUARD'" })
        {
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_writer'; " + action));
            Assert.Equal("LOCKED", await ExecuteAsync("SELECT status FROM dbo.daily_reporting_days WHERE store_code='LOCKGUARD'"));
        }
        Assert.Equal(0, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.daily_reporting_events WHERE store_code='LOCKGUARD'")));
    }

    [Fact]
    public async Task Owner_without_windows_elevation_reopens_once_with_sql_actor_and_required_reason()
    {
        await ExecuteAsync("CREATE USER phase4_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER phase4_owner;");
        await SeedDay("OWNERGUARD");
        foreach (var reason in new[] { "", " ", "\t\r\n" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='phase4_owner'; EXEC dbo.reopen_reporting_day 'OWNERGUARD','20260825',N'{reason}';"));
            Assert.Equal(51302, error.Number);
        }
        await ExecuteAsync("EXECUTE AS USER='phase4_owner'; EXEC dbo.reopen_reporting_day 'OWNERGUARD','20260825',N'Corrected source export';");
        Assert.Equal("OPEN", await ExecuteAsync("SELECT status FROM dbo.daily_reporting_days WHERE store_code='OWNERGUARD'"));
        Assert.Equal("Corrected source export", await ExecuteAsync("SELECT reason FROM dbo.daily_reporting_events WHERE store_code='OWNERGUARD'"));
        Assert.Equal(1, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.daily_reporting_events WHERE store_code='OWNERGUARD'")));
        Assert.Equal(await ExecuteAsync("EXECUTE AS USER='phase4_owner'; SELECT SUSER_SNAME()"), await ExecuteAsync("SELECT performed_by FROM dbo.daily_reporting_events WHERE store_code='OWNERGUARD'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_owner'; EXEC dbo.reopen_reporting_day 'OWNERGUARD','20260825',N'Again';"));
    }


    [Fact]
    public async Task Staff_permissions_reject_fact_edits_spoofed_audit_and_owner_operations_but_preserve_imports()
    {
        await ExecuteAsync("CREATE USER phase4_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER phase4_manager; CREATE USER phase4_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER phase4_viewer;");
        foreach (var sql in new[] {
            "UPDATE dbo.import_files SET is_superseded=1",
            "UPDATE dbo.sales_lines SET source_net_amount=0",
            "DELETE dbo.operational_audit",
            "INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason) VALUES('FORGED','20260825','DayReopened','Owner','Pretend approval')",
            "INSERT dbo.operational_audit(event_type,outcome,application_version,actor_name) VALUES('Backup','Succeeded','test','spoofed')",
            "EXEC dbo.configure_application_role N'Fake\\User','OWNER',1",
            "EXEC dbo.prepare_import_restatement 1,2,N'spoofed',N'Forged reason'" })
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_manager'; " + sql));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_viewer'; UPDATE dbo.daily_reporting_days SET status='OPEN'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_viewer'; DELETE dbo.operational_audit"));
        Assert.Equal(0, await ExecuteAsync("EXECUTE AS USER='phase4_manager'; SELECT IS_ROLEMEMBER('db_datawriter')"));
        await ExecuteAsync("EXECUTE AS USER='phase4_viewer'; EXEC dbo.record_operational_audit 'ReportRun','Succeeded',N'Report completed',N'test';");
        Assert.Equal(await ExecuteAsync("EXECUTE AS USER='phase4_viewer'; SELECT SUSER_SNAME()"), await ExecuteAsync("SELECT TOP(1) actor_name FROM dbo.operational_audit WHERE application_version='test' ORDER BY operational_audit_id DESC"));
        await ExecuteAsync("""
            EXECUTE AS USER='phase4_manager';
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Processing',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date)
             VALUES(@batch,'synthetic.xlsx',REPLICATE('e',64),1,'R025','ROLEIMPORT','20260825');
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Test',1,'R025');
            SET @lineage=SCOPE_IDENTITY();
            EXEC dbo.persist_sales_line 'ROLEIMPORT','TEST-INVOICE',2026,'20260825','1','TEST-PRODUCT','SA',1,118,100,NULL,NULL,NULL,'INR',@lineage;
            EXEC dbo.refresh_enrichment_matches;
            UPDATE dbo.import_batches SET status='Completed',source_row_count=1,completed_utc=SYSUTCDATETIME() WHERE import_batch_id=@batch;
            """);
        Assert.Equal(1, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code='ROLEIMPORT'")));
    }

    [Fact]
    public async Task Audit_is_append_only_for_owner_and_archiving_keeps_original_history()
    {
        await ExecuteAsync("INSERT dbo.operational_audit(event_type,outcome,application_version,actor_name) VALUES('Backup','Succeeded','direct-owner','forged-owner')");
        Assert.Equal(await ExecuteAsync("SELECT SUSER_SNAME()"), await ExecuteAsync("SELECT actor_name FROM dbo.operational_audit WHERE application_version='direct-owner'"));
        await ExecuteAsync("EXEC dbo.record_operational_audit 'Backup','Succeeded',N'Backup verified',N'archive-test'");
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("DELETE dbo.operational_audit WHERE application_version='archive-test'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE dbo.operational_audit SET actor_name='forged' WHERE application_version='archive-test'"));
        await ExecuteAsync("INSERT dbo.operational_audit(event_utc,event_type,outcome,application_version,actor_name) VALUES(DATEADD(year,-2,SYSUTCDATETIME()),'Backup','Succeeded','old-audit',SUSER_SNAME()); EXEC dbo.archive_operational_audit 365; EXEC dbo.archive_operational_audit 365;");
        Assert.Equal(1, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit_archive WHERE application_version='old-audit'")));
        Assert.Equal(1, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE application_version='old-audit'")));
        var repository = new OperationalAuditRepository(database.ConnectionString);
        await repository.RecordAsync("ReportRun", "Succeeded", "Actor comes from SQL", actorName: "forged-owner");
        Assert.Equal(await ExecuteAsync("SELECT SUSER_SNAME()"), await ExecuteAsync("SELECT TOP(1) actor_name FROM dbo.operational_audit ORDER BY operational_audit_id DESC"));
    }

    [Fact]
    public async Task Staff_submissions_cannot_forge_approval_and_locked_day_adjustments_enter_accounting_only_after_owner_decision()
    {
        await ExecuteAsync("""
            CREATE USER phase4_submitter WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER phase4_submitter;
            CREATE USER phase4_approval_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER phase4_approval_viewer;
            CREATE USER phase4_decider WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER phase4_decider;
            """);
        var genericRequest = Convert.ToInt64(await ExecuteAsync("""
            EXECUTE AS USER='phase4_decider';
            EXEC dbo.submit_approval_request 'ACCOUNTING_MAPPING','Mapping','Proposal','APPROVALGUARD','20260825',
                N'{"status":"APPROVED","decided_by":"forged-owner"}';
            """));
        Assert.True(genericRequest > 0);
        Assert.Equal("PENDING", await ExecuteAsync($"SELECT status FROM dbo.approval_requests WHERE approval_request_id={genericRequest}"));
        Assert.Equal(1, await ExecuteAsync($"SELECT COUNT(*) FROM dbo.approval_requests WHERE approval_request_id={genericRequest} AND decided_by IS NULL AND decided_utc IS NULL AND decision_reason IS NULL"));
        var staffActor = await ExecuteAsync("EXECUTE AS USER='phase4_decider'; SELECT SUSER_SNAME()");
        Assert.Equal(staffActor, await ExecuteAsync($"SELECT requested_by FROM dbo.approval_requests WHERE approval_request_id={genericRequest}"));

        foreach (var mutation in new[]
        {
            "INSERT dbo.approval_requests(approval_type,subject_type,subject_id,request_payload_json,requested_by,status,decided_by) VALUES('ACCOUNTING_MAPPING','Mapping','Forged',N'{}',N'forged','APPROVED',N'Owner')",
            $"INSERT dbo.controlled_adjustments(store_code,business_date,adjustment_type,amount,reason,approval_request_id,created_by,status) VALUES('APPROVALGUARD','20260825','CORRECTION',999,N'Forged approval',{genericRequest},N'Owner','APPROVED')",
            $"UPDATE dbo.approval_requests SET status='APPROVED',decided_by=N'Owner' WHERE approval_request_id={genericRequest}",
            $"EXEC dbo.decide_approval_request {genericRequest},1,N'Forged approval'"
        })
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_submitter'; " + mutation));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_approval_viewer'; EXEC dbo.submit_approval_request 'REOPEN_DAY','Day','Proposal',NULL,NULL,N'{}'"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_approval_viewer'; EXEC dbo.submit_controlled_adjustment 'APPROVALGUARD','20260825','CORRECTION',1,N'Not permitted'"));
        await ExecuteAsync("GRANT EXECUTE ON dbo.decide_approval_request TO phase4_submitter;");
        var denied = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='phase4_submitter'; EXEC dbo.decide_approval_request {genericRequest},1,N'Even an extra execute grant is insufficient'"));
        Assert.Equal(51315, denied.Number);

        await SeedDay("APPROVALGUARD");
        await ExecuteAsync("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('APPROVALGUARD','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
            """);
        var adjustment = Convert.ToInt64(await ExecuteAsync("EXECUTE AS USER='phase4_decider'; EXEC dbo.submit_controlled_adjustment 'APPROVALGUARD','20260825','CORRECTION',125.75,N'Evidence for Owner review'"));
        var request = Convert.ToInt64(await ExecuteAsync($"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.NotEqual(genericRequest, request);
        Assert.Equal("PENDING", await ExecuteAsync($"SELECT status FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.Equal(staffActor, await ExecuteAsync($"SELECT created_by FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.Equal("LOCKED", await ExecuteAsync("SELECT status FROM dbo.daily_reporting_days WHERE store_code='APPROVALGUARD'"));
        var repository = new ProductisationRepository(database.ConnectionString);
        var beforeDecision = await repository.LoadAccountingSourceAsync("APPROVALGUARD", new DateOnly(2026, 8, 25));
        Assert.DoesNotContain(beforeDecision.Events, entry => entry.EventCode == "ADJUSTMENT");
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='phase4_submitter'; UPDATE dbo.controlled_adjustments SET status='APPROVED' WHERE controlled_adjustment_id={adjustment}"));

        var countBeforeFailure = await ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests");
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='phase4_decider'; EXEC dbo.submit_controlled_adjustment 'APPROVALGUARD','20260825','CORRECTION',2,N'Missing supporting document',-1"));
        Assert.Equal(countBeforeFailure, await ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests"));
        var missingReason = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='phase4_decider'; EXEC dbo.decide_approval_request {request},1,N' '"));
        Assert.Equal(51314, missingReason.Number);

        await ExecuteAsync($"EXECUTE AS USER='phase4_decider'; EXEC dbo.decide_approval_request {request},1,N'Supporting evidence checked'");
        Assert.Equal("APPROVED", await ExecuteAsync($"SELECT status FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.Equal(await ExecuteAsync("EXECUTE AS USER='phase4_decider'; SELECT SUSER_SNAME()"), await ExecuteAsync($"SELECT decided_by FROM dbo.approval_requests WHERE approval_request_id={request}"));
        Assert.Equal("Supporting evidence checked", await ExecuteAsync($"SELECT decision_reason FROM dbo.approval_requests WHERE approval_request_id={request}"));
        Assert.Equal(1, await ExecuteAsync($"SELECT COUNT(*) FROM dbo.approval_requests WHERE approval_request_id={request} AND decided_utc IS NOT NULL"));
        var approved = await repository.LoadAccountingSourceAsync("APPROVALGUARD", new DateOnly(2026, 8, 25));
        Assert.Equal(125.75m, Assert.Single(approved.Events, entry => entry.EventCode == "ADJUSTMENT").Amount);
        Assert.Equal("LOCKED", await ExecuteAsync("SELECT status FROM dbo.daily_reporting_days WHERE store_code='APPROVALGUARD'"));
        Assert.Equal(0, await ExecuteAsync("SELECT COUNT(*) FROM dbo.daily_reporting_events WHERE store_code='APPROVALGUARD' AND event_type='DayReopened'"));
        var alreadyDecided = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='phase4_decider'; EXEC dbo.decide_approval_request {request},0,N'Cannot reverse an existing decision'"));
        Assert.Equal(51211, alreadyDecided.Number);
    }

    [Fact]
    public async Task Repository_submission_and_owner_rejection_preserve_ids_and_pending_workflow()
    {
        var repository = new ProductisationRepository(database.ConnectionString);
        var approval = await repository.CreateApprovalAsync("ACCOUNTING_MAPPING", "Mapping", "Repository mapping", new { ledger = "Test ledger" });
        Assert.True(approval > 0);
        Assert.Equal("PENDING", await ExecuteAsync($"SELECT status FROM dbo.approval_requests WHERE approval_request_id={approval}"));
        await repository.DecideApprovalAsync(approval, true, "Mapping evidence checked");
        Assert.Equal("APPROVED", await ExecuteAsync($"SELECT status FROM dbo.approval_requests WHERE approval_request_id={approval}"));

        var adjustment = await repository.CreateAdjustmentRequestAsync("REPOAPPROVAL", new DateOnly(2026, 8, 25), "CORRECTION", -5m, "Proposed correction");
        Assert.True(adjustment > 0);
        var adjustmentApproval = Convert.ToInt64(await ExecuteAsync($"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.Equal("PENDING", await ExecuteAsync($"SELECT status FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        await repository.DecideApprovalAsync(adjustmentApproval, false, "Insufficient evidence");
        Assert.Equal("REJECTED", await ExecuteAsync($"SELECT status FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}"));
        Assert.Equal("REJECTED", await ExecuteAsync($"SELECT status FROM dbo.approval_requests WHERE approval_request_id={adjustmentApproval}"));
    }

    private async Task<object?> ExecuteAsync(string sql)
    {
        // Impersonation is scoped to a disposable, unpooled test connection.
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private Task<object?> SeedDay(string store) => ExecuteAsync($"INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('{store}','20260825','LOCKED',SUSER_SNAME(),SYSUTCDATETIME())");
}
