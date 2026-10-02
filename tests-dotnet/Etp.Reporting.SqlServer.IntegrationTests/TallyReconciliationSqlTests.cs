using System.Text;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Migration 0039 and the manual-file path of plan tasks 7, 9 and 10, end to end on the fixture database with a
// synthetic G01 cash sale. Each test uses its own store and profile codes.
public sealed class TallyReconciliationSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>, IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpTallyReconciliationSql", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task G01_file_round_trip_reconciles_and_a_second_compare_leaves_the_first_run_untouched()
    {
        var (batch, voucher) = await SeedAsync("TRA");
        var service = new SqlServerTallyReconciliationService(database.ConnectionString, root);

        var readback = await service.LoadManualReadbackAsync(batch, Day, Day, Xml("TEST - ETP TRA", "TRA"));
        Assert.True(readback.IsComplete);
        Assert.Equal(1, readback.VoucherCount);
        Assert.Equal("ETP:TRA:2027:INV-1:SALES:1", await database.ExecuteAsync($"SELECT correspondence_key FROM dbo.tally_actual_vouchers WHERE tally_readback_id={readback.Id}"));
        Assert.Equal(1180m, await database.ExecuteAsync($"SELECT total_amount FROM dbo.tally_actual_vouchers WHERE tally_readback_id={readback.Id}"));

        var first = await service.CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.Equal("RECONCILED", first.Result.RunOutcome);
        Assert.Empty(first.Result.Differences);
        Assert.Equal("RECONCILED", await database.ExecuteAsync($"SELECT voucher_status FROM dbo.accounting_vouchers WHERE accounting_voucher_id={voucher}"));
        Assert.Equal("Compared with a Tally read-back", await database.ExecuteAsync(
            $"SELECT TOP(1) reason FROM dbo.accounting_status_history WHERE subject_type='VOUCHER' AND subject_id={voucher} AND to_status='RECONCILED'"));
        Assert.True(File.Exists(Path.Combine(root, "GTRA", "TRA", "2026-08", $"batch-{batch}", "reconciliation", "run-1.json")));
        var firstRun = await database.ExecuteAsync($"SELECT CONCAT(outcome,'|',summary_json,'|',evidence_artifact_id) FROM dbo.tally_reconciliation_runs WHERE run_id={first.RunId}");

        var second = await service.CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(firstRun, await database.ExecuteAsync($"SELECT CONCAT(outcome,'|',summary_json,'|',evidence_artifact_id) FROM dbo.tally_reconciliation_runs WHERE run_id={first.RunId}"));
        Assert.Equal(2, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_reconciliation_runs WHERE accounting_batch_id={batch}"));

        var manifest = await service.BuildManifestAsync(batch, "test-build");
        Assert.Equal(manifest.Sha256, await database.ExecuteAsync($"SELECT manifest_sha256 FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        var text = await File.ReadAllTextAsync(Path.Combine(root, "GTRA", "TRA", "2026-08", $"batch-{batch}", "manifest.json"));
        foreach (var expected in new[] { "\"intendedCompany\": \"TEST - ETP TRA\"", "\"voucherCount\": 1", "\"tax\": 180", "\"tender\": 1180", "payload.xml", "readback-1.xml", "run-2.json" })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest.json", text, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildManifestAsync(batch, "test-build"));
    }

    [Fact]
    public async Task A_package_with_a_changed_file_gets_no_manifest()
    {
        var (batch, _) = await SeedAsync("TRE");
        var service = new SqlServerTallyReconciliationService(database.ConnectionString, root);
        await File.AppendAllTextAsync(Path.Combine(root, "GTRE", "TRE", "2026-08", $"batch-{batch}", "payload.xml"), " ");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildManifestAsync(batch, "test-build"));
        Assert.Contains("CHANGED", refused.Message, StringComparison.Ordinal);
        Assert.Equal(DBNull.Value, await database.ExecuteAsync($"SELECT manifest_sha256 FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
    }

    [Fact]
    public async Task A_file_from_another_company_is_stored_incomplete_and_the_comparison_fails()
    {
        var (batch, voucher) = await SeedAsync("TRB");
        var service = new SqlServerTallyReconciliationService(database.ConnectionString, root);
        var readback = await service.LoadManualReadbackAsync(batch, Day, Day, Xml("TEST - Somebody Else", "TRB"));
        Assert.False(readback.IsComplete);
        Assert.Equal("WRONG_COMPANY", readback.IncompleteReason);

        var comparison = await service.CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.Equal("FAILED_RECONCILIATION", comparison.Result.RunOutcome);
        Assert.Equal("WRONG_COMPANY", await database.ExecuteAsync($"SELECT difference_type FROM dbo.tally_reconciliation_differences WHERE run_id={comparison.RunId}"));
        Assert.Equal("DIFFERENCE", await database.ExecuteAsync($"SELECT voucher_status FROM dbo.accounting_vouchers WHERE accounting_voucher_id={voucher}"));

        var failure = await database.ExecuteAsync($"SELECT difference_id FROM dbo.tally_reconciliation_differences WHERE run_id={comparison.RunId}");
        await Assert.ThrowsAsync<SqlException>(() => service.AcceptDifferenceAsync((long)failure!, "Failures cannot be waved through"));
        var deleted = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.tally_reconciliation_differences WHERE run_id={comparison.RunId}"));
        Assert.Equal(51573, deleted.Number);
        var changed = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.tally_readbacks SET is_complete=1,incomplete_reason=NULL WHERE tally_readback_id={readback.Id}"));
        Assert.Equal(51573, changed.Number);

        var plan = await service.SaveRecoveryPlanAsync(batch, comparison);
        Assert.Equal("RECOVERY_PLAN", plan.Kind);
        var planText = await File.ReadAllTextAsync(Path.Combine(root, plan.RelativePath.Replace('\\', Path.DirectorySeparatorChar)));
        Assert.Contains("READ_BACK_AGAIN", planText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not xml", "NOT_XML")]
    [InlineData("<ENVELOPE><BODY><DATA><LINEERROR>Could not find Company 'TEST - ETP TRC'</LINEERROR></DATA></BODY></ENVELOPE>", "COMPANY_NOT_OPEN")]
    [InlineData("<ENVELOPE><BODY><EXPORTDATA><REQUESTDATA/></EXPORTDATA></BODY></ENVELOPE>", "COMPANY_NOT_REPORTED")]
    public async Task Unusable_files_are_kept_as_evidence_and_marked_incomplete(string content, string reason)
    {
        var (batch, _) = await SeedAsync("TRC" + reason.Length);
        var readback = await new SqlServerTallyReconciliationService(database.ConnectionString, root).LoadManualReadbackAsync(batch, Day, Day, Encoding.UTF8.GetBytes(content));
        Assert.Equal(reason, readback.IncompleteReason);
        Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_artifacts WHERE accounting_batch_id={batch} AND artifact_kind='READBACK_XML'"));
    }

    [Fact]
    public async Task Findings_are_recorded_and_only_a_warning_can_be_accepted_once()
    {
        var (batch, _) = await SeedAsync("TRD");
        var service = new SqlServerTallyReconciliationService(database.ConnectionString, root);
        var saved = await service.SaveFindingsAsync(batch,
        [
            new("RULE-AMT-004", 1, "WARN", 1, "Invoice INV-1", "590000.00", "at most 500000.00", "This invoice is unusually large.", "Confirm, then accept."),
            new("RULE-SRC-001", 1, "FAIL", null, null, "no final report", "final report generation", "No final report.", "Finalise the day."),
            new("RULE-ENV-001", 1, "PASS", null, null, null, null, "Passing rules are not stored.", null)
        ]);
        Assert.Equal(2, saved);
        var warning = (long)(await database.ExecuteAsync($"SELECT finding_id FROM dbo.accounting_validation_findings WHERE accounting_batch_id={batch} AND severity='WARN'"))!;
        var fail = (long)(await database.ExecuteAsync($"SELECT finding_id FROM dbo.accounting_validation_findings WHERE accounting_batch_id={batch} AND severity='FAIL'"))!;
        Assert.NotNull(await database.ExecuteAsync($"SELECT accounting_voucher_id FROM dbo.accounting_validation_findings WHERE finding_id={warning}"));

        await service.AcceptWarningAsync(warning, "Owner confirmed the wedding order");
        Assert.Equal("Owner confirmed the wedding order", await database.ExecuteAsync($"SELECT waiver_reason FROM dbo.accounting_validation_findings WHERE finding_id={warning}"));
        await Assert.ThrowsAsync<SqlException>(() => service.AcceptWarningAsync(warning, "Again"));
        await Assert.ThrowsAsync<SqlException>(() => service.AcceptWarningAsync(fail, "Failures cannot be accepted"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AcceptWarningAsync(warning, " "));
        var deleted = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.accounting_validation_findings WHERE finding_id={fail}"));
        Assert.Equal(51573, deleted.Number);

        await Assert.ThrowsAsync<SqlException>(() => service.SaveFindingsAsync(batch, [new("RULE-AMT-001", 1, "FAIL", 99, null, null, null, "Unknown voucher.", null)]));
        Assert.Equal(2, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.accounting_validation_findings WHERE accounting_batch_id={batch}"));
    }

    [Fact]
    public async Task New_tables_are_denied_to_store_managers_and_viewers()
    {
        string[] tables = ["accounting_validation_findings", "tally_readbacks", "tally_actual_vouchers", "tally_actual_ledger_entries", "tally_reconciliation_runs", "tally_reconciliation_differences"];
        Assert.Equal(tables.Length * 2 * 4, await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id JOIN sys.objects o ON o.object_id=p.major_id
            WHERE p.state_desc='DENY' AND p.permission_name IN('SELECT','INSERT','UPDATE','DELETE') AND r.name IN('etp_store_manager','etp_viewer')
              AND SCHEMA_NAME(o.schema_id)='dbo' AND o.name IN({string.Join(',', tables.Select(t => $"'{t}'"))})
            """));
    }

    private static readonly DateOnly Day = new(2026, 8, 25);

    // Profile G{store}, company "TEST - ETP {store}", one EXPORTED voucher INV-1 with the G01 entries and its written payload.
    private async Task<(long Batch, long Voucher)> SeedAsync(string store)
    {
        var profile = await database.ExecuteAsync($"""
            INSERT dbo.tally_profiles(profile_code,company_name,environment,voucher_granularity,party_policy,single_party_ledger,tender_model,posting_model,voucher_view,change_reason)
            VALUES('G{store}',N'TEST - ETP {store}','TEST','PER_INVOICE','SINGLE_LEDGER',N'Cash Sales','IN_VOUCHER','ACCOUNTING_ONLY','ACCOUNTING',N'Synthetic reconciliation profile');
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """);
        var batch = (long)(await database.ExecuteAsync($"""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('{store}','20260825',1,REPLICATE('a',64),N'{"{}"}',SUSER_SNAME(),1);
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,batch_kind,tally_profile_id)
            SELECT store_code,business_date,daily_report_generation_id,1,1180,1180,'DRAFT',SUSER_SNAME(),'SALES_VOUCHERS',{profile}
            FROM dbo.daily_report_generations WHERE store_code='{store}';
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """))!;
        var voucher = (long)(await database.ExecuteAsync($"""
            INSERT dbo.accounting_vouchers(accounting_batch_id,voucher_sequence,component_role,voucher_type,store_code,invoice_year,document_number,voucher_date,expected_total,correspondence_key,source_sha256,plan_sha256)
            VALUES({batch},1,'SALES',N'Sales','{store}',2027,N'INV-1','20260825',1180,N'ETP:{store}:2027:INV-1:SALES:1',REPLICATE('a',64),REPLICATE('b',64));
            DECLARE @v bigint=SCOPE_IDENTITY();
            INSERT dbo.accounting_entries(accounting_batch_id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,source_reference,accounting_voucher_id) VALUES
             ({batch},1,'TENDER_CASH',N'Cash',1180,0,N'G01',N'fixture',@v),({batch},2,'SALES_REVENUE',N'Sales',0,1000,N'G01',N'fixture',@v),
             ({batch},3,'OUTPUT_CGST_9',N'Output CGST 9%',0,90,N'G01',N'fixture',@v),({batch},4,'OUTPUT_SGST_9',N'Output SGST 9%',0,90,N'G01',N'fixture',@v);
            UPDATE dbo.accounting_vouchers SET voucher_status='EXPORTED' WHERE accounting_voucher_id=@v;
            SELECT @v;
            """))!;
        await new TallyEvidenceStore(database.ConnectionString, root).WriteAsync(batch, "PAYLOAD_XML", $@"G{store}\{store}\2026-08\batch-{batch}\payload.xml", Xml($"TEST - ETP {store}", store));
        return (batch, voucher);
    }

    private static byte[] Xml(string company, string store) => Encoding.UTF8.GetBytes(
        "<ENVELOPE><HEADER><TALLYREQUEST>Export Data</TALLYREQUEST></HEADER><BODY><EXPORTDATA><REQUESTDESC><REPORTNAME>Day Book</REPORTNAME><STATICVARIABLES>"
        + $"<SVCURRENTCOMPANY>{company}</SVCURRENTCOMPANY></STATICVARIABLES></REQUESTDESC><REQUESTDATA><TALLYMESSAGE>"
        + $"<VOUCHER VCHTYPE=\"Sales\" ACTION=\"Create\"><DATE>20260825</DATE><VOUCHERTYPENAME>Sales</VOUCHERTYPENAME><NARRATION>ETP:{store}:2027:INV-1:SALES:1 | ETP {store} invoice INV-1 dated 25-Aug-2026</NARRATION>"
        + "<ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-1180.00</AMOUNT></ALLLEDGERENTRIES.LIST>"
        + "<ALLLEDGERENTRIES.LIST><LEDGERNAME>Sales</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>1000.00</AMOUNT></ALLLEDGERENTRIES.LIST>"
        + "<ALLLEDGERENTRIES.LIST><LEDGERNAME>Output CGST 9%</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>90.00</AMOUNT></ALLLEDGERENTRIES.LIST>"
        + "<ALLLEDGERENTRIES.LIST><LEDGERNAME>Output SGST 9%</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>90.00</AMOUNT></ALLLEDGERENTRIES.LIST>"
        + "</VOUCHER></TALLYMESSAGE></REQUESTDATA></EXPORTDATA></BODY></ENVELOPE>");

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
