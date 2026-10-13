using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Tally;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// 1.12 D1 end to end on the fixture database (cross-layer, plan Appendix C item 5): C# prepares, approves and writes the Tally
// file; SQL triggers and constraints hold the statuses, reservations and evidence; the file read back reconciles; a tampered
// file is caught. The connected tests use a fake Tally that replays the synthetic answers in fixtures/tally-golden: nothing
// ever opens a socket. Each test uses its own store and profile codes.
public sealed class TallySalesExportReadbackSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>, IDisposable
{
    private static readonly DateOnly Day = new(2026, 8, 25);
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpTallyExportReadbackSql", Guid.NewGuid().ToString("N"));

    [SqlFact]
    public async Task Prepare_approve_write_and_a_manual_read_back_reconcile_and_a_tampered_file_is_caught()
    {
        var store = "TXA";
        var profile = await SeedAsync(store, "TEST - ETP TXA");
        var batch = await PrepareAndApproveAsync(profile, store);
        var sales = Sales();

        var receipt = await sales.ExportAsync(batch);

        Assert.Equal((1, 1180m, "TEST - ETP TXA"), (receipt.VoucherCount, receipt.VoucherTotal, receipt.CompanyName));
        Assert.Equal($@"GTXA\TXA\2026-08\batch-{batch}\payload.xml", receipt.RelativePath);
        var path = Path.Combine(root, receipt.RelativePath.Replace('\\', Path.DirectorySeparatorChar));
        var written = await File.ReadAllBytesAsync(path);
        Assert.Equal(receipt.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(written)).ToLowerInvariant());
        var voucher = XDocument.Parse(Encoding.UTF8.GetString(written)).Descendants("VOUCHER").Single();
        Assert.Equal(new[] { "Cash - TXA|-1180.00", "Sales - GST Retail|1000.00", "Output CGST 9%|90.00", "Output SGST 9%|90.00" },
            voucher.Elements("ALLLEDGERENTRIES.LIST").Select(line => $"{line.Element("LEDGERNAME")!.Value}|{line.Element("AMOUNT")!.Value}"));
        Assert.All(voucher.Descendants("COSTCENTREALLOCATIONS.LIST"), allocation => Assert.Equal("Synthetic Centre", allocation.Element("NAME")!.Value));

        Assert.Equal("EXPORTED_AWAITING_IMPORT|" + receipt.Sha256, await database.ExecuteAsync(
            $"SELECT CONCAT(status,'|',export_sha256) FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        Assert.Equal("INV-1:EXPORTED|INV-2:BLOCKED", await database.ExecuteAsync(
            $"SELECT STRING_AGG(CONCAT(document_number,':',voucher_status),'|') WITHIN GROUP(ORDER BY voucher_sequence) FROM dbo.accounting_vouchers WHERE accounting_batch_id={batch}"));
        Assert.Equal("FILE|XML|FILE_WRITTEN", await database.ExecuteAsync(
            $"SELECT CONCAT(delivery_mode,'|',payload_format,'|',attempt_status) FROM dbo.tally_attempts WHERE accounting_batch_id={batch}"));
        Assert.Equal("TEST - ETP TXA|TEST", await database.ExecuteAsync(
            $"SELECT CONCAT(tally_company_name,'|',environment_label) FROM dbo.accounting_export_receipts WHERE accounting_batch_id={batch}"));
        Assert.Equal("Tally voucher file written; Tally has not imported it yet", await database.ExecuteAsync(
            $"SELECT TOP(1) reason FROM dbo.accounting_status_history WHERE subject_type='BATCH' AND subject_id={batch} AND to_status='EXPORTED_AWAITING_IMPORT'"));

        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => sales.ExportAsync(batch));
        Assert.Contains("never written twice", twice.Message, StringComparison.Ordinal);

        // The file as Tally would hold it, exported back by hand, reconciles.
        var reconciliation = new SqlServerTallyReconciliationService(database.ConnectionString, root);
        var readback = await reconciliation.LoadManualReadbackAsync(batch, Day, Day, written);
        Assert.True(readback.IsComplete);
        var first = await reconciliation.CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.Equal(TallyBatchStatus.Reconciled, first.Result.RunOutcome);
        Assert.Empty(first.Result.Differences);

        // A file changed after it was written is never trusted again.
        await File.AppendAllTextAsync(path, " ");
        var second = await reconciliation.CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.NotEqual(TallyBatchStatus.Reconciled, second.Result.RunOutcome);
        Assert.Contains(second.Result.Differences, difference => difference is { CheckLevel: "INTEGRITY", RuleId: "RECO-INT-002" });
        Assert.Equal(1, await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM dbo.tally_reconciliation_differences d JOIN dbo.tally_reconciliation_runs r ON r.run_id=d.run_id
            WHERE r.accounting_batch_id={batch} AND d.check_level='INTEGRITY' AND d.rule_id='RECO-INT-002'
            """));
    }

    [SqlFact]
    public async Task Only_an_approved_voucher_batch_is_written()
    {
        var profile = await SeedAsync("TXB", "TEST - ETP TXB");
        var sales = Sales();
        var draft = await sales.SaveAsync(await sales.PreviewAsync(profile, "TXB", Day));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => sales.ExportAsync(draft));
        Assert.Contains("Approve the batch", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_artifacts WHERE accounting_batch_id={draft}"));
        Assert.Equal("DRAFT", await database.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={draft}"));
    }

    // Decision 41: INV-2 is cancelled by a BC row of a current R025 file on its own day, so it never reaches Tally, and the
    // rest of the day is still prepared.
    [SqlFact]
    public async Task A_bill_cancelled_the_same_day_is_left_out_of_the_vouchers()
    {
        var profile = await SeedAsync("TXC", "TEST - ETP TXC", cancelSecondSameDay: true);
        var preview = await Sales().PreviewAsync(profile, "TXC", Day);

        Assert.Null(preview.Plan.BlockingReason);
        var cancelled = preview.Plan.Vouchers.Single(voucher => voucher.DocumentNumber == "INV-2");
        Assert.Equal(TallySalesVoucherComposer.CancelledSameDayCode, cancelled.BlockCode);
        Assert.True(cancelled.LeftOutByScope);
        Assert.Equal(1180m, preview.Plan.DebitTotal);
        Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.v_sales_cancelled_same_day WHERE store_code='TXC'"));
    }

    [SqlFact]
    public async Task Educational_Tally_refuses_a_day_it_cannot_take_before_anything_is_written()
    {
        var profile = await SeedAsync("TXD", "TEST - ETP TXD", build: "TallyPrime 6.1 Educational");
        var batch = await PrepareAndApproveAsync(profile, "TXD");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Sales().ExportAsync(batch));
        Assert.Contains("Educational mode", refused.Message, StringComparison.Ordinal);
        Assert.Contains("25-Aug-2026", refused.Message, StringComparison.Ordinal);
        Assert.Equal("APPROVED_READY", await database.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_artifacts WHERE accounting_batch_id={batch}"));
    }

    [SqlFact]
    public async Task Read_back_over_HTTP_records_the_day_book_with_its_request_and_reconciles()
    {
        var profile = await SeedAsync("TXE", TallyTestCompany.Name, endpoint: "http://127.0.0.1:9000/");
        var batch = await PrepareAndApproveAsync(profile, "TXE");
        var receipt = await Sales().ExportAsync(batch);
        var tally = new FakeTally { DayBook = AsDayBook(await File.ReadAllBytesAsync(Path.Combine(root, receipt.RelativePath.Replace('\\', Path.DirectorySeparatorChar)))) };
        var connected = Connected(tally);

        var readback = await connected.ReadBackAsync(batch);

        Assert.True(readback.IsComplete);
        Assert.Equal((TallyTestCompany.Name, 1), (readback.CompanyNameReported!, readback.VoucherCount));
        Assert.Equal($"HTTP|{TallyTestCompany.Name}|{SqlServerTallyConnectedService.HttpAdapterVersion}|1", await database.ExecuteAsync(
            $"SELECT CONCAT(source,'|',company_name_reported,'|',adapter_version,'|',IIF(request_artifact_id IS NULL,0,1)) FROM dbo.tally_readbacks WHERE tally_readback_id={readback.Id}"));
        Assert.True(File.Exists(Path.Combine(root, "GTXE", "TXE", "2026-08", $"batch-{batch}", "actuals", "readback-1-request.xml")));
        Assert.Contains("<SVCURRENTCOMPANY>Saagar Traders Titan</SVCURRENTCOMPANY>",
            await File.ReadAllTextAsync(Path.Combine(root, "GTXE", "TXE", "2026-08", $"batch-{batch}", "actuals", "readback-1-request.xml")), StringComparison.Ordinal);
        Assert.Equal(TallyBatchStatus.Reconciled,
            (await new SqlServerTallyReconciliationService(database.ConnectionString, root).CompareAsync(batch, readback.Id, sourceUnchanged: true)).Result.RunOutcome);
    }

    [SqlFact]
    public async Task Connecting_is_refused_unless_set_up_for_the_test_company_and_Tally_has_it_open()
    {
        var off = await PrepareAndApproveAsync(await SeedAsync("TXF", TallyTestCompany.Name), "TXF");
        var other = await PrepareAndApproveAsync(await SeedAsync("TXG", "TEST - ETP TXG", endpoint: "http://127.0.0.1:9000/"), "TXG");
        var closed = await PrepareAndApproveAsync(await SeedAsync("TXH", TallyTestCompany.Name, endpoint: "http://localhost:9000/"), "TXH");
        var tally = new FakeTally();
        var connected = Connected(tally);

        Assert.Equal(TallyConnectedMode.NotConfigured, (await Assert.ThrowsAsync<InvalidOperationException>(() => connected.ReadBackAsync(off))).Message);
        Assert.Equal(TallyConnectedMode.OnlyTestCompany, (await Assert.ThrowsAsync<InvalidOperationException>(() => connected.ReadBackAsync(other))).Message);
        Assert.Equal(0, tally.Calls);

        tally.OpenCompanies.Clear();
        tally.OpenCompanies.Add("Some Other Company");
        var notOpen = await Assert.ThrowsAsync<InvalidOperationException>(() => connected.ReadBackAsync(closed));
        Assert.Equal($"Tally is running but the company '{TallyTestCompany.Name}' is not open. Open it in Tally, then try again.", notOpen.Message);
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_readbacks WHERE accounting_batch_id IN({off},{other},{closed})"));

        var silent = new SqlServerTallyConnectedService(database.ConnectionString, _ => new SilentTally(), null, root);
        Assert.StartsWith("Tally is not answering on this PC", (await Assert.ThrowsAsync<InvalidOperationException>(() => silent.ReadBackAsync(closed))).Message, StringComparison.Ordinal);

        // A company whose vouchers go by file never sends, even with an address.
        Assert.Contains("delivery is 'file'", (await Assert.ThrowsAsync<InvalidOperationException>(() => connected.SendAsync(closed))).Message, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task A_send_is_recorded_with_Tally_s_answer_and_a_second_send_is_refused_as_a_duplicate()
    {
        var profile = await SeedAsync("TXI", TallyTestCompany.Name, endpoint: "http://127.0.0.1:9000/", delivery: "HTTP", machine: Environment.MachineName);
        var batch = await PrepareAndApproveAsync(profile, "TXI");
        var tally = new FakeTally { ImportAnswer = Fixture("import-created.xml") };
        var connected = Connected(tally);

        var notWritten = await Assert.ThrowsAsync<InvalidOperationException>(() => connected.SendAsync(batch));
        Assert.Contains("Write the Tally file first", notWritten.Message, StringComparison.Ordinal);
        await Sales().ExportAsync(batch);

        var sent = await connected.SendAsync(batch);

        Assert.Equal(("RESPONDED", 1), (sent.AttemptStatus, sent.VoucherCount));
        Assert.True(sent.Result!.AllCreated(1));
        Assert.StartsWith("Tally says 1 of 1 voucher(s) created", sent.Message, StringComparison.Ordinal);
        Assert.Equal(1, tally.Imports);
        Assert.Equal($"HTTP|RESPONDED|200|1|{sent.AttemptId}", await database.ExecuteAsync($"""
            SELECT CONCAT(delivery_mode,'|',attempt_status,'|',http_status,'|',JSON_VALUE(tally_diagnostics_json,'$.created'),'|',tally_attempt_id)
            FROM dbo.tally_attempts WHERE accounting_batch_id={batch} AND delivery_mode='HTTP'
            """));
        var folder = Path.Combine(root, "GTXI", "TXI", "2026-08", $"batch-{batch}", "attempts");
        Assert.True(File.Exists(Path.Combine(folder, $"attempt-{sent.AttemptId}-request.xml")));
        Assert.True(File.Exists(Path.Combine(folder, $"attempt-{sent.AttemptId}-response.xml")));
        Assert.Equal(0, await database.ExecuteAsync($"SELECT voucher_count FROM dbo.tally_readbacks WHERE tally_readback_id={sent.ReadbackBeforeSendId}"));

        var again = await Assert.ThrowsAsync<InvalidOperationException>(() => connected.SendAsync(batch));
        Assert.Contains("already holds 1 of these 1 voucher(s)", again.Message, StringComparison.Ordinal);
        Assert.Equal(1, tally.Imports);
        Assert.Equal(1, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_attempts WHERE accounting_batch_id={batch} AND delivery_mode='HTTP'"));

        var readback = await connected.ReadBackAsync(batch);
        var comparison = await new SqlServerTallyReconciliationService(database.ConnectionString, root).CompareAsync(batch, readback.Id, sourceUnchanged: true);
        Assert.Equal(TallyBatchStatus.Reconciled, comparison.Result.RunOutcome);
    }

    [SqlFact]
    public async Task A_rejected_send_keeps_Tally_s_words_and_may_be_sent_again_once_Tally_holds_nothing()
    {
        var profile = await SeedAsync("TXJ", TallyTestCompany.Name, endpoint: "http://127.0.0.1:9000/", delivery: "HTTP", machine: Environment.MachineName);
        var batch = await PrepareAndApproveAsync(profile, "TXJ");
        await Sales().ExportAsync(batch);
        var tally = new FakeTally { ImportAnswer = Fixture("import-rejected-ledger.xml"), ImportPutsVouchersInTally = false };
        var connected = Connected(tally);

        var rejected = await connected.SendAsync(batch);

        Assert.Equal("RESPONDED", rejected.AttemptStatus);
        Assert.Equal(1, rejected.Result!.Errors);
        Assert.Contains("1 refused", rejected.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", (string)(await database.ExecuteAsync(
            $"SELECT tally_diagnostics_json FROM dbo.tally_attempts WHERE tally_attempt_id={rejected.AttemptId}"))!, StringComparison.Ordinal);
        Assert.Equal("Failed", await database.ExecuteAsync(
            "SELECT TOP(1) outcome FROM dbo.operational_audit WHERE event_type='AccountingExport' AND safe_detail LIKE N'Tally file sent to the test Tally%' ORDER BY operational_audit_id DESC"));

        // The accountant adds the missing ledger in Tally; Tally still holds nothing, so sending again is safe.
        tally.ImportAnswer = Fixture("import-created.xml");
        tally.ImportPutsVouchersInTally = true;
        var retried = await connected.SendAsync(batch);
        Assert.True(retried.Result!.AllCreated(1));
        Assert.Equal(2, tally.Imports);
        Assert.Equal("RESPONDED|RESPONDED", await database.ExecuteAsync(
            $"SELECT STRING_AGG(attempt_status,'|') WITHIN GROUP(ORDER BY tally_attempt_id) FROM dbo.tally_attempts WHERE accounting_batch_id={batch} AND delivery_mode='HTTP'"));
    }

    // Decision 53 (review M-3): a send needs this PC to be the test PC the profile names, and Tally to say it is in Educational
    // mode. Either refusal happens before anything is recorded or sent.
    [SqlFact]
    public async Task A_send_is_refused_on_any_other_PC_and_when_Tally_is_not_in_Educational_mode()
    {
        var profile = await SeedAsync("TXL", TallyTestCompany.Name, endpoint: "http://127.0.0.1:9000/", delivery: "HTTP", machine: "ETP-TEST-VM");
        var batch = await PrepareAndApproveAsync(profile, "TXL");
        await Sales().ExportAsync(batch);
        var tally = new FakeTally();

        var otherPc = new SqlServerTallyConnectedService(database.ConnectionString, _ => tally, _ => tally, root, () => "OFFICE-PC");
        Assert.Equal(TallyConnectedMode.OnlyTheTestPc, (await Assert.ThrowsAsync<InvalidOperationException>(() => otherPc.SendAsync(batch))).Message);
        Assert.Equal(0, tally.Calls);

        var testPc = new SqlServerTallyConnectedService(database.ConnectionString, _ => tally, _ => tally, root, () => "etp-test-vm");
        tally.Educational = Fixture("licence-licensed.xml");
        Assert.Equal(TallyConnectedMode.OnlyEducationalMode, (await Assert.ThrowsAsync<InvalidOperationException>(() => testPc.SendAsync(batch))).Message);
        tally.Educational = Fixture("malformed.xml");
        Assert.Equal(TallyConnectedMode.OnlyEducationalMode, (await Assert.ThrowsAsync<InvalidOperationException>(() => testPc.SendAsync(batch))).Message);
        Assert.Equal(0, tally.Imports);
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.tally_attempts WHERE accounting_batch_id={batch} AND delivery_mode='HTTP'"));

        // The PC name is kept upper-case, and the 0066 CHECK refuses a name Windows would not give a PC.
        Assert.Equal("ETP-TEST-VM", await database.ExecuteAsync($"SELECT test_machine_name FROM dbo.tally_profiles WHERE tally_profile_id={profile}"));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => database.ExecuteAsync(
            $"UPDATE dbo.tally_profiles SET test_machine_name=N'ETP VM' WHERE tally_profile_id={profile}"));
    }

    [SqlFact]
    public async Task Masters_are_planned_from_the_company_s_stores_and_approved_ledgers_and_written_once()
    {
        var profile = await SeedAsync("TXK", TallyTestCompany.Name);
        var masters = new SqlServerTallyMastersService(database.ConnectionString, () => Day, () => new DateTime(2026, 8, 25, 18, 0, 0));

        var plan = await masters.PlanAsync(profile);

        Assert.Equal(new[] { "Synthetic Centre|TXK|False" }, plan.CostCentres.Select(centre => $"{centre.Name}|{centre.StoreCode}|{centre.Proposed}"));
        foreach (var name in new[] { "Cash - TXK", "Sales - GST Retail", "Output CGST 9%", "Output SGST 9%" })
            Assert.True(plan.Ledgers.Single(ledger => ledger.Name == name).FromApprovedMapping, name);
        Assert.Contains(plan.Ledgers, ledger => ledger.Name == "UPI Settlement - TXK" && !ledger.FromApprovedMapping);
        Assert.Contains(new ProposedLedgerMapping("TXK", "ROUND_OFF", "Round Off"), plan.MappingsToApprove);

        var folder = Path.Combine(root, "masters");
        var receipt = await masters.ExportAsync(profile, folder);
        Assert.Equal(Path.Combine(folder, "tally-masters-GTXK.xml"), receipt.XmlPath);
        Assert.Equal(plan.Ledgers.Count, XDocument.Load(receipt.XmlPath).Descendants("LEDGER").Count());
        Assert.Contains("Synthetic Centre  for store TXK", await File.ReadAllTextAsync(receipt.ReviewPath), StringComparison.Ordinal);
        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() => masters.ExportAsync(profile, folder));
        Assert.Contains("never overwritten", twice.Message, StringComparison.Ordinal);
    }

    private SqlServerTallySalesBatchService Sales() => new(database.ConnectionString, () => Day, root);

    private SqlServerTallyConnectedService Connected(FakeTally tally) => new(database.ConnectionString, _ => tally, _ => tally, root);

    private async Task<long> PrepareAndApproveAsync(int profile, string store)
    {
        var sales = Sales();
        var batch = await sales.SaveAsync(await sales.PreviewAsync(profile, store, Day));
        await new SqlServerAccountingService(database.ConnectionString).ApproveAsync(new ApproveAccountingBatch(batch, "Checked synthetic vouchers for 1.12 D1"));
        return batch;
    }

    // One store in its own TEST company (cost centre "Synthetic Centre"), a G01 cash sale INV-1 of 1,180.00 and a split-payment
    // sale INV-2 on 25 Aug 2026, their R018 GST rows, approved ledger mappings and a final report generation. With
    // cancelSecondSameDay, a current R025 file cancels INV-2 on its own day (decision 41).
    private async Task<int> SeedAsync(string store, string company, string? endpoint = null, string delivery = "FILE", string? build = null, bool cancelSecondSameDay = false,
        string? machine = null)
    {
        await database.ExecuteAsync($"INSERT dbo.stores(store_code,store_name) VALUES('{store}',N'Synthetic {store}')");
        var profile = await new SqlServerTallyProfileService(database.ConnectionString).SaveAsync(
            TallyProfile.NewTest("G" + store, company, new[] { store }) with
            {
                EndpointUrl = endpoint, DefaultDeliveryMode = delivery, TallyBuildLabel = build, TestMachineName = machine,
                StoreCostCentres = new Dictionary<string, string> { [store] = "Synthetic Centre" }
            },
            "Synthetic company for 1.12 D1");
        await database.ExecuteAsync($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic-{store}.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256',N'd1-{store}'),2)),1);
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Tender',1,'tender'),(@file,'Tender',2,'tender'),(@file,'Tender',3,'tender'),(@file,'Gst',1,'gst'),(@file,'Gst',2,'gst');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('{store}',N'INV-1',2027,'20260825'),('{store}',N'INV-2',2027,'20260825');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_tax_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,'1',N'SAREE1','INV',1,1180,1000,180,'INR',s.source_lineage_id
            FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales' AND s.source_row_number=CASE i.document_number WHEN N'INV-1' THEN 1 ELSE 2 END
            WHERE i.store_code='{store}';
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,t.code,t.amount,'INR',s.source_lineage_id
            FROM (VALUES(N'INV-1',N'CASH',1180,1),(N'INV-2',N'CASH',500,2),(N'INV-2',N'PHONEPE',680,3)) t(doc,code,amount,seq)
            JOIN dbo.sales_invoices i ON i.store_code='{store}' AND i.document_number=t.doc
            JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Tender' AND s.source_row_number=t.seq;
            INSERT dbo.[etp_r018](import_file_id,source_lineage_id,content_key,[store_code],[transaction_type],[doc_invoice_no],[doc_invoice_date],[invoice_year],[item_number],
              [cgst_rate],[cgst_amount],[sgst_utgst_rate],[sgst_utgst_amount],[igst_rate],[igst_amount],[cess_rate],[cess_amount])
            SELECT @file,s.source_lineage_id,CONCAT('{store}-',s.source_row_number),N'{store}',N'INV',CASE s.source_row_number WHEN 1 THEN N'INV-1' ELSE N'INV-2' END,'20260825',2027,N'SAREE1',9,90,9,90,0,0,0,0
            FROM dbo.source_lineage s WHERE s.import_file_id=@file AND s.sheet_name='Gst';
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('{store}','20260825',1,REPLICATE('a',64),N'{"{}"}',SUSER_SNAME(),1);
            """);
        if (cancelSecondSameDay)
            await database.ExecuteAsync($"""
                DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
                INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,store_code,report_code,business_date,period_start,period_end,data_truth_version)
                VALUES(@batch,'cancel-{store}.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256',N'd1-r025-{store}'),2)),1,'{store}','R025','20260825','20260825','20260825',1);
                SET @file=SCOPE_IDENTITY();
                INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sales',1,'sale');
                INSERT dbo.etp_r025(import_file_id,source_lineage_id,content_key,source_transaction_type,store_code,invoice_number,transaction_date,reference_invoice_number,reference_invoice_date)
                SELECT @file,s.source_lineage_id,'bc1',N'BC',N'{store}',N'BC-2','20260825',N'INV-2','20260825'
                FROM dbo.source_lineage s WHERE s.import_file_id=@file;
                """);
        await Mapping(store, "TENDER_CASH", $"Cash - {store}");
        await Mapping(store, "SALES_REVENUE", "Sales - GST Retail");
        await Mapping(store, "OUTPUT_CGST_9", "Output CGST 9%");
        await Mapping(store, "OUTPUT_SGST_9", "Output SGST 9%");
        return profile;
    }

    private Task Mapping(string store, string businessEvent, string ledger) =>
        new SqlServerAccountingService(database.ConnectionString).ApproveMappingAsync(
            new ApproveAccountingMapping(new(store, Day), businessEvent, ledger, ledger, "{reference}", "Synthetic mapping for 1.12 D1"));

    /// <summary>What Tally's Day Book export of an imported file looks like: the same vouchers without ETP's request header.</summary>
    private static byte[] AsDayBook(byte[] payload) => Encoding.UTF8.GetBytes(
        Regex.Replace(Encoding.UTF8.GetString(payload), "<REQUESTDESC>.*?</REQUESTDESC>", "", RegexOptions.Singleline | RegexOptions.CultureInvariant));

    private static byte[] Fixture(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            foreach (var candidate in new[]
                     {
                         Path.Combine(directory.FullName, "fixtures", "tally-golden", "responses", name),
                         Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "tally-golden", "responses", name)
                     })
                if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
        throw new FileNotFoundException($"Tally golden fixture {name} was not found.", name);
    }

    /// <summary>A Tally in memory: the companies it has open, its Day Book, and what it answers to an import. An accepted
    /// import puts the sent vouchers into its Day Book.</summary>
    private sealed class FakeTally : ITallyReadbackGateway, ITallyImportGateway
    {
        public List<string> OpenCompanies { get; } = [TallyTestCompany.Name];
        public byte[] DayBook { get; set; } = Fixture("daybook-empty.xml");
        public byte[] ImportAnswer { get; set; } = Fixture("import-created.xml");
        public bool ImportPutsVouchersInTally { get; set; } = true;
        public byte[] Educational { get; set; } = Fixture("licence-educational.xml");
        public int Calls { get; private set; }
        public int Imports { get; private set; }

        public Task<TallyGatewayResponse> FetchVouchersAsync(TallyReadbackRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TallyGatewayResponse(TallyXml.DayBookRequest(request), DayBook, 200, null, null));
        }

        public Task<TallyOpenCompanies> FetchOpenCompaniesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TallyOpenCompanies(OpenCompanies.ToArray(),
                new TallyGatewayResponse(TallyXml.OpenCompaniesRequest(), Fixture("open-companies.xml"), 200, null, null)));
        }

        public Task<TallyGatewayResponse> ImportAsync(string companyName, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            Calls++;
            Imports++;
            if (ImportPutsVouchersInTally) DayBook = AsDayBook(payload.ToArray());
            return Task.FromResult(new TallyGatewayResponse(payload.ToArray(), ImportAnswer, 200, null, null));
        }

        public Task<TallyGatewayResponse> FetchEducationalModeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new TallyGatewayResponse(TallyXml.EducationalModeRequest(), Educational, 200, null, null));
        }
    }

    private sealed class SilentTally : ITallyReadbackGateway
    {
        public Task<TallyGatewayResponse> FetchVouchersAsync(TallyReadbackRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TallyGatewayResponse(TallyXml.DayBookRequest(request), null, null, TallyGatewayFailure.NotAnswering, "Tally is not answering on this PC."));

        public Task<TallyOpenCompanies> FetchOpenCompaniesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TallyOpenCompanies([], new TallyGatewayResponse(TallyXml.OpenCompaniesRequest(), null, null, TallyGatewayFailure.NotAnswering, "Tally is not answering on this PC.")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
