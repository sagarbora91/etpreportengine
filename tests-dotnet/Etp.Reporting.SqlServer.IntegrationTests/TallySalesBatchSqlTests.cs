using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Tally;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Plan task 6 end to end on the fixture database: a synthetic day with one G01 cash sale and one split-payment
// sale, GST rows from an R018 import, approved ledger mappings and a final report generation. Each test uses its
// own store codes so the shared fixture stays independent.
public sealed class TallySalesBatchSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    [Fact]
    public async Task A_day_is_prepared_as_one_voucher_per_invoice_and_saved_with_its_reservations()
    {
        var (profile, _) = await SeedAsync("TSVA", "TSVB");
        var service = new SqlServerTallySalesBatchService(database.ConnectionString);

        var preview = await service.PreviewAsync(profile, "TSVA", Day);

        Assert.Null(preview.Plan.BlockingReason);
        Assert.Equal(new[] { "INV-1:PLANNED", "INV-2:BLOCKED" }, preview.Plan.Vouchers.Select(voucher => $"{voucher.DocumentNumber}:{voucher.Status}"));
        var sale = preview.Plan.Vouchers[0];
        Assert.Equal(new[] { "TENDER_CASH|Cash|1180", "SALES_REVENUE|Sales|1000", "OUTPUT_CGST_9|Output CGST 9%|90", "OUTPUT_SGST_9|Output SGST 9%|90" },
            sale.Entries.Select(entry => $"{entry.BusinessEvent}|{entry.LedgerName}|{entry.Debit + entry.Credit:0.##}"));
        Assert.All(sale.Entries, entry => Assert.Equal("Titan World", entry.CostCentre));
        Assert.StartsWith("NOT_IN_SCOPE_7A: split payment", preview.Plan.Vouchers[1].BlockedReason, StringComparison.Ordinal);

        var batch = await service.SaveAsync(preview);

        Assert.Equal("SALES_VOUCHERS|DRAFT|1180.0000|1180.0000", await database.ExecuteAsync(
            $"SELECT CONCAT(batch_kind,'|',status,'|',debit_total,'|',credit_total) FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        Assert.Equal(profile, Convert.ToInt32(await database.ExecuteAsync($"SELECT tally_profile_id FROM dbo.accounting_batches WHERE accounting_batch_id={batch}")));
        Assert.Equal("INV-1:PLANNED|INV-2:BLOCKED", await database.ExecuteAsync(
            $"SELECT STRING_AGG(CONCAT(document_number,':',voucher_status),'|') WITHIN GROUP(ORDER BY voucher_sequence) FROM dbo.accounting_vouchers WHERE accounting_batch_id={batch}"));
        Assert.Equal(sale.PlanSha256, await database.ExecuteAsync($"SELECT plan_sha256 FROM dbo.accounting_vouchers WHERE accounting_batch_id={batch} AND voucher_sequence=1"));
        Assert.Equal(4, await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM dbo.accounting_entries e JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=e.accounting_voucher_id
            WHERE e.accounting_batch_id={batch} AND v.document_number=N'INV-1' AND e.cost_centre=N'Titan World'
            """));
        Assert.Equal(9m, await database.ExecuteAsync($"SELECT MAX(tax_rate) FROM dbo.accounting_entries WHERE accounting_batch_id={batch}"));
        Assert.Equal("INV-1", await database.ExecuteAsync($"""
            SELECT STRING_AGG(r.document_number,'|') FROM dbo.accounting_voucher_reservations r JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=r.accounting_voucher_id
            WHERE v.accounting_batch_id={batch} AND r.released_utc IS NULL
            """));
        Assert.Equal(2, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.accounting_batch_invoices WHERE accounting_batch_id={batch} AND is_active=1"));
        var mappings = (string)(await database.ExecuteAsync($"SELECT mapping_version_set_json FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"))!;
        foreach (var code in new[] { "TENDER_CASH", "SALES_REVENUE", "OUTPUT_CGST_9", "OUTPUT_SGST_9" })
            Assert.Contains($"\"{code}\"", mappings, StringComparison.Ordinal);
        Assert.Equal("|DRAFT|", await database.ExecuteAsync(
            $"SELECT CONCAT(from_status,'|',to_status,'|',reason) FROM dbo.accounting_status_history WHERE subject_type='BATCH' AND subject_id={batch}"));

        var again = await Assert.ThrowsAsync<SqlException>(() => service.SaveAsync(preview));
        Assert.Equal(51452, again.Number);
    }

    // OD-1 (IF-019, migration 0041): an invoice is keyed by the financial year of its date, and R018's INVOICEYEAR is only
    // a label. A GST row whose label differs from that year (on 10 Oct 2026 only 3 return rows on live) still belongs
    // to its invoice; joining on the label would leave the invoice blocked as TAX_ROW_MISSING.
    [Fact]
    public async Task Gst_rows_are_found_by_the_financial_year_of_their_date_not_by_the_year_label()
    {
        var (profile, _) = await SeedAsync("TSVM", "TSVN", r018YearLabel: 2026);
        var service = new SqlServerTallySalesBatchService(database.ConnectionString);

        var preview = await service.PreviewAsync(profile, "TSVM", Day);

        Assert.Null(preview.Plan.BlockingReason);
        var sale = preview.Plan.Vouchers[0];
        Assert.Equal(("INV-1", 2027, "PLANNED"), (sale.DocumentNumber, sale.InvoiceYear, sale.Status));
        Assert.Equal(new[] { "TENDER_CASH|1180", "SALES_REVENUE|1000", "OUTPUT_CGST_9|90", "OUTPUT_SGST_9|90" },
            sale.Entries.Select(entry => $"{entry.BusinessEvent}|{entry.Debit + entry.Credit:0.##}"));
    }

    [Fact]
    public async Task A_preview_that_no_longer_matches_the_mappings_is_never_saved()
    {
        var (profile, _) = await SeedAsync("TSVC", "TSVD");
        var service = new SqlServerTallySalesBatchService(database.ConnectionString);
        var preview = await service.PreviewAsync(profile, "TSVC", Day);

        await Mapping("TSVC", "TENDER_CASH", "Cash in hand");

        var stale = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(preview));
        Assert.Contains("changed since the preview", stale.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batches WHERE store_code='TSVC'"));

        var fresh = await service.PreviewAsync(profile, "TSVC", Day);
        Assert.Equal("Cash in hand", fresh.Plan.Vouchers[0].Entries[0].LedgerName);
        Assert.True(await service.SaveAsync(fresh) > 0);
    }

    [Fact]
    public async Task A_missing_ledger_mapping_saves_the_day_as_blocked_with_the_reason()
    {
        var (profile, _) = await SeedAsync("TSVE", "TSVF", mapTax: false);
        var service = new SqlServerTallySalesBatchService(database.ConnectionString);

        var preview = await service.PreviewAsync(profile, "TSVE", Day);
        Assert.StartsWith("MAPPING_MISSING: no approved ledger for OUTPUT_CGST_9, OUTPUT_SGST_9", preview.Plan.Vouchers[0].BlockedReason, StringComparison.Ordinal);

        var batch = await service.SaveAsync(preview);
        Assert.Equal("BLOCKED", await database.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        Assert.StartsWith("1 invoice(s) need fixing", (string)(await database.ExecuteAsync($"SELECT blocking_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"))!, StringComparison.Ordinal);
        Assert.Equal(0, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.accounting_voucher_reservations r JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=r.accounting_voucher_id WHERE v.accounting_batch_id={batch}"));
    }

    [Fact]
    public async Task Stores_outside_the_company_and_days_without_a_final_report_are_refused()
    {
        var (profile, _) = await SeedAsync("TSVG", "TSVH");
        var service = new SqlServerTallySalesBatchService(database.ConnectionString);

        var outside = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PreviewAsync(profile, "TSVA", Day));
        Assert.Contains("is not linked to", outside.Message, StringComparison.Ordinal);
        var notFinal = await Assert.ThrowsAsync<SqlException>(() => service.PreviewAsync(profile, "TSVG", Day.AddDays(1)));
        Assert.Equal(51220, notFinal.Number);
    }

    [Fact]
    public async Task Warnings_are_saved_with_the_batch_and_must_be_accepted_before_approval()
    {
        var (profile, _) = await SeedAsync("TSVI", "TSVJ");
        var service = new SqlServerTallySalesBatchService(database.ConnectionString, () => Day.AddDays(40));

        var preview = await service.PreviewAsync(profile, "TSVI", Day);
        Assert.Equal("RULE-DAT-002", Assert.Single(preview.Findings).RuleId);
        var batch = await service.SaveAsync(preview);
        var saved = Assert.Single(await service.LoadFindingsAsync(batch));
        Assert.Equal(("RULE-DAT-002", "WARN", (int?)1), (saved.RuleId, saved.Severity, saved.VoucherSequence));
        Assert.Null(saved.WaivedBy);

        var accounting = new SqlServerAccountingService(database.ConnectionString);
        var refused = await Assert.ThrowsAsync<SqlException>(() => accounting.ApproveAsync(new ApproveAccountingBatch(batch, "Checked synthetic vouchers")));
        Assert.Equal(51221, refused.Number);

        await service.AcceptWarningAsync(saved.Id, "Owner confirmed the late day");
        Assert.Equal("Owner confirmed the late day", Assert.Single(await service.LoadFindingsAsync(batch)).WaiverReason);
        await accounting.ApproveAsync(new ApproveAccountingBatch(batch, "Checked synthetic vouchers"));
        Assert.Equal("APPROVED_READY", await database.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));

        // The Phase 5 day-journal export must never write invoice vouchers as one journal.
        var path = Path.Combine(Path.GetTempPath(), "EtpTallyVouchers_" + Guid.NewGuid().ToString("N") + ".xml");
        var export = await Assert.ThrowsAsync<SqlException>(() => new ProductisationRepository(database.ConnectionString).ExportAccountingBatchAsync(
            batch, path, new AccountingDestination("TEST - ETP TSVI", "TEST"), _ => Task.FromResult(new string('a', 64))));
        Assert.Equal(51571, export.Number);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Ledger_mappings_list_versions_and_name_what_a_store_still_needs()
    {
        await SeedAsync("TSVK", "TSVL", mapTax: false);
        var ledgers = new SqlServerTallyLedgerMappingService(database.ConnectionString);

        var needed = await ledgers.LoadNeededEventsAsync("TSVK");
        foreach (var code in new[] { "TENDER_CASH", "TENDER_UPI", "ROUND_OFF", "SALES_REVENUE", "OUTPUT_CGST_9", "OUTPUT_SGST_9" })
            Assert.Contains(code, needed);

        await ledgers.SaveAsync("TSVK", "OUTPUT_CGST_9", "Output CGST 9%", Day, "Accountant named the CGST ledger");
        await ledgers.SaveAsync("TSVK", "OUTPUT_CGST_9", "Output CGST @ 9%", Day.AddDays(1), "Renamed in Tally");
        var versions = (await ledgers.LoadAsync()).Where(row => row.StoreCode == "TSVK" && row.BusinessEvent == "OUTPUT_CGST_9").ToArray();
        Assert.Equal(new[] { "2:Output CGST @ 9%:True", "1:Output CGST 9%:False" }, versions.Select(row => $"{row.Version}:{row.DebitLedger}:{row.IsActive}"));

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => ledgers.SaveAsync("TSVK", "SOMETHING_ELSE", "Ledger", Day, "Wrong event"));
        Assert.Contains("Choose a business event", refused.Message, StringComparison.Ordinal);
    }

    // A TEST company holding both stores (D12 = one company, store as cost centre), one G01 cash sale INV-1 and one
    // split-payment sale INV-2 on the first store, their R018 GST rows, approved mappings and a final generation.
    // r018YearLabel is R018's INVOICEYEAR column, a label only (OD-1): the invoices are keyed by the financial year of
    // 25 Aug 2026, which is 2027.
    private async Task<(int Profile, long Generation)> SeedAsync(string store, string otherStore, bool mapTax = true, int r018YearLabel = 2027)
    {
        await database.ExecuteAsync($"INSERT dbo.stores(store_code,store_name) VALUES('{store}',N'Synthetic {store}'),('{otherStore}',N'Synthetic {otherStore}')");
        var profile = await new SqlServerTallyProfileService(database.ConnectionString).SaveAsync(
            TallyProfile.NewTest("G" + store, $"TEST - ETP {store}", new[] { store, otherStore }) with
                { StoreCostCentres = new Dictionary<string, string> { [store] = "Titan World", [otherStore] = "Helios" } },
            "Synthetic company for task 6");
        await database.ExecuteAsync($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic-{store}.xlsx',LOWER(CONVERT(char(64),HASHBYTES('SHA2_256',N'synthetic-{store}'),2)),1);
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
            SELECT @file,s.source_lineage_id,CONCAT('{store}-',s.source_row_number),N'{store}',N'INV',CASE s.source_row_number WHEN 1 THEN N'INV-1' ELSE N'INV-2' END,'20260825',{r018YearLabel},N'SAREE1',9,90,9,90,0,0,0,0
            FROM dbo.source_lineage s WHERE s.import_file_id=@file AND s.sheet_name='Gst';
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
            VALUES('{store}','20260825',1,REPLICATE('a',64),N'{"{}"}',SUSER_SNAME(),1);
            """);
        await Mapping(store, "TENDER_CASH", "Cash");
        await Mapping(store, "TENDER_UPI", "UPI receivable");
        await Mapping(store, "SALES_REVENUE", "Sales");
        if (mapTax)
        {
            await Mapping(store, "OUTPUT_CGST_9", "Output CGST 9%");
            await Mapping(store, "OUTPUT_SGST_9", "Output SGST 9%");
        }
        var generation = Convert.ToInt64(await database.ExecuteAsync($"SELECT daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code='{store}'"));
        return (profile, generation);
    }

    private Task Mapping(string store, string businessEvent, string ledger) =>
        new SqlServerAccountingService(database.ConnectionString).ApproveMappingAsync(
            new ApproveAccountingMapping(new(store, Day), businessEvent, ledger, ledger, "{reference}", "Synthetic mapping for task 6"));
}
