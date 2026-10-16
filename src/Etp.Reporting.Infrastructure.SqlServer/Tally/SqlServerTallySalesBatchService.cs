using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Plan task 6: reads one day's invoices, composes one Sales voucher per invoice and saves them as a
/// <c>SALES_VOUCHERS</c> batch with its vouchers, entries, invoice reservations and the plan task 7 validation findings.
/// Plan task 8 (1.12 D1): writes an approved batch as the Tally file. Owner only. Nothing is sent to Tally.</summary>
public sealed class SqlServerTallySalesBatchService(string connectionString, Func<DateOnly>? today = null, string? evidenceRootOverride = null)
    : ITallySalesBatchService, ITallySalesVoucherExportService
{
    private DateOnly Today => today?.Invoke() ?? DateOnly.FromDateTime(DateTime.Now);

    public async Task<SalesVoucherPreview> PreviewAsync(int tallyProfileId, string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var store = (storeCode ?? "").Trim().ToUpperInvariant();
        var profile = (await new SqlServerTallyProfileService(connectionString).LoadAsync(cancellationToken)).FirstOrDefault(item => item.Id == tallyProfileId)
            ?? throw new InvalidOperationException("This Tally company no longer exists. Refresh the list.");
        if (!profile.IsEnabled) throw new InvalidOperationException("This Tally company is switched off in Settings → Tally companies.");
        if (profile.Environment != "TEST") throw new InvalidOperationException("Only test books can be prepared now. Live books are enabled later, after the test company has been checked.");
        if (!profile.StoreCodes.Contains(store, StringComparer.Ordinal))
            throw new InvalidOperationException($"Store {store} is not linked to {profile.CompanyName}. Add it in Settings → Tally companies.");
        if (TallySalesVoucherComposer.Unsupported(profile) is { } unsupported) throw new InvalidOperationException(unsupported);

        var (generation, invoices) = await LoadSourceAsync(store, businessDate, cancellationToken);
        var mappings = await LoadMappingsAsync(store, businessDate, cancellationToken);
        var composed = TallySalesVoucherComposer.Compose(new(profile, profile.CostCentreFor(store), profile.StoreCodes.Count > 1, mappings), invoices);
        var (plan, findings) = TallySalesVoucherValidation.Apply(composed, invoices, profile, Today);
        return new(generation, profile, store, businessDate, plan, findings);
    }

    /// <summary>Saves the preview after composing it again from the database: if any invoice, mapping or setting changed
    /// in between, nothing is saved. Returns the new batch id.</summary>
    public async Task<long> SaveAsync(SalesVoucherPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var fresh = await PreviewAsync(preview.Profile.Id ?? 0, preview.StoreCode, preview.BusinessDate, cancellationToken);
        static string Fingerprint(SalesVoucherPreview item) => string.Join("|", item.Plan.Vouchers.Select(voucher =>
            $"{voucher.Sequence}:{voucher.CorrespondenceKey}:{voucher.Status}:{voucher.BlockedReason}:{voucher.SourceSha256}:{voucher.PlanSha256}")) + $"#{item.ReportGenerationId}";
        if (Fingerprint(fresh) != Fingerprint(preview))
            throw new InvalidOperationException("The invoices, ledger mappings or Tally settings changed since the preview. Prepare the vouchers again.");

        var plan = fresh.Plan;
        var line = 0;
        var entries = plan.Vouchers.Where(voucher => voucher.Status == TallyVoucherStatus.Planned).SelectMany(voucher => voucher.Entries.Select(entry => new
        {
            voucher_sequence = voucher.Sequence, line_number = ++line, business_event = entry.BusinessEvent, ledger_name = entry.LedgerName,
            debit_amount = entry.Debit, credit_amount = entry.Credit, narration = voucher.Narration, cost_centre = entry.CostCentre,
            source_reference = $"{voucher.StoreCode}/{voucher.InvoiceYear}/{voucher.DocumentNumber}", tax_rate = entry.TaxRate
        })).ToArray();
        // Property names are the column names, so the OPENJSON clause below needs no reserved words.
        var vouchers = plan.Vouchers.Select(voucher => new
        {
            voucher_sequence = voucher.Sequence, component_role = voucher.ComponentRole, voucher_type = voucher.VoucherType, store_code = voucher.StoreCode,
            invoice_year = voucher.InvoiceYear, document_number = voucher.DocumentNumber, revision = voucher.Revision, sales_invoice_id = voucher.SalesInvoiceId,
            voucher_date = voucher.VoucherDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), expected_total = voucher.ExpectedTotal,
            correspondence_key = voucher.CorrespondenceKey, source_sha256 = voucher.SourceSha256, plan_sha256 = voucher.PlanSha256,
            voucher_status = voucher.Status, blocked_reason = voucher.BlockedReason
        }).ToArray();
        var mappingSet = plan.MappingsUsed.Select(mapping => new { @event = mapping.BusinessEvent, id = mapping.MappingId, version = mapping.Version }).ToArray();

        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.tally_profile_stores WHERE tally_profile_id=@profile AND store_code=@store)
              THROW 51579,'This store is no longer linked to the chosen Tally company. Prepare the vouchers again.',1;
            IF ISNULL((SELECT TOP(1) daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code=@store AND business_date=@date AND is_final=1 ORDER BY generation_number DESC),0)<>@report
              THROW 51220,'The final report changed. Prepare the vouchers again.',1;
            DECLARE @previous bigint,@previousStatus varchar(40),@previousKind varchar(20),@message nvarchar(2048);
            SELECT TOP(1) @previous=accounting_batch_id,@previousStatus=status,@previousKind=batch_kind FROM dbo.accounting_batches WITH(UPDLOCK,HOLDLOCK)
              WHERE store_code=@store AND business_date=@date AND status<>'REJECTED' ORDER BY accounting_batch_id;
            IF @previous IS NOT NULL AND @previousKind='DAY_JOURNAL' BEGIN
              SET @message=CONCAT('This day is already in day-journal batch ',@previous,'. A day goes to Tally either as a day journal or as invoice vouchers; reject batch ',@previous,' first if it has not been exported.');
              THROW 51571,@message,1;
            END;
            IF @previous IS NOT NULL BEGIN
              SET @message=CONCAT('This day is already in batch ',@previous,'. Reject that batch before preparing the vouchers again.');
              THROW 51452,@message,1;
            END;
            DECLARE @number int=ISNULL((SELECT MAX(accounting_generation) FROM dbo.accounting_batches WHERE store_code=@store AND business_date=@date),0)+1;
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,blocking_reason,created_by,
              batch_kind,tally_profile_id,selection_json,mapping_version_set_json)
            VALUES(@store,@date,@report,@number,@debit,@credit,CASE WHEN @blocking IS NULL THEN 'DRAFT' ELSE 'BLOCKED' END,@blocking,SUSER_SNAME(),
              'SALES_VOUCHERS',@profile,N'{"keys":null}',@mappings);
            DECLARE @id bigint=SCOPE_IDENTITY();
            INSERT dbo.accounting_vouchers(accounting_batch_id,voucher_sequence,component_role,voucher_type,store_code,invoice_year,document_number,revision,sales_invoice_id,
              voucher_date,expected_total,correspondence_key,source_sha256,plan_sha256,voucher_status,blocked_reason)
            SELECT @id,voucher_sequence,component_role,voucher_type,store_code,invoice_year,document_number,revision,sales_invoice_id,
              voucher_date,expected_total,correspondence_key,source_sha256,plan_sha256,voucher_status,blocked_reason
            FROM OPENJSON(@vouchers) WITH(voucher_sequence int,component_role varchar(20),voucher_type nvarchar(50),store_code varchar(30),invoice_year int,
              document_number nvarchar(80),revision int,sales_invoice_id bigint,voucher_date date,expected_total decimal(19,4),correspondence_key nvarchar(200),
              source_sha256 char(64),plan_sha256 char(64),voucher_status varchar(30),blocked_reason nvarchar(1000));
            INSERT dbo.accounting_entries(accounting_batch_id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,cost_centre,source_reference,accounting_voucher_id,tax_rate)
            SELECT @id,e.line_number,e.business_event,e.ledger_name,e.debit_amount,e.credit_amount,e.narration,e.cost_centre,e.source_reference,v.accounting_voucher_id,e.tax_rate
            FROM OPENJSON(@entries) WITH(voucher_sequence int,line_number int,business_event varchar(50),ledger_name nvarchar(200),debit_amount decimal(19,4),credit_amount decimal(19,4),
              narration nvarchar(500),cost_centre nvarchar(200),source_reference nvarchar(200),tax_rate decimal(5,2)) e
            JOIN dbo.accounting_vouchers v ON v.accounting_batch_id=@id AND v.voucher_sequence=e.voucher_sequence;
            INSERT dbo.accounting_voucher_reservations(tally_profile_id,store_code,invoice_year,document_number,component_role,revision,accounting_voucher_id)
            SELECT @profile,store_code,invoice_year,document_number,component_role,revision,accounting_voucher_id
            FROM dbo.accounting_vouchers WHERE accounting_batch_id=@id AND voucher_status='PLANNED';
            INSERT dbo.accounting_batch_invoices(accounting_batch_id,store_code,invoice_year,document_number,is_active)
            SELECT @id,store_code,invoice_year,document_number,1 FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date;
            INSERT dbo.accounting_validation_findings(accounting_batch_id,accounting_voucher_id,rule_id,rule_version,severity,subject,observed,expected,explanation,corrective_action)
            SELECT @id,v.accounting_voucher_id,f.rule_id,f.rule_version,f.severity,f.subject,f.observed,f.expected,f.explanation,f.corrective_action
            FROM OPENJSON(@findings) WITH(sequence int '$.VoucherSequence',rule_id varchar(30) '$.RuleId',rule_version int '$.RuleVersion',severity varchar(5) '$.Severity',
              subject nvarchar(200) '$.Subject',observed nvarchar(500) '$.Observed',expected nvarchar(500) '$.Expected',explanation nvarchar(1000) '$.Explanation',
              corrective_action nvarchar(500) '$.CorrectiveAction') f
            LEFT JOIN dbo.accounting_vouchers v ON v.accounting_batch_id=@id AND v.voucher_sequence=f.sequence;
            EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Tally sales vouchers prepared; nothing sent to Tally',N'database';
            COMMIT TRANSACTION; SELECT @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@profile", fresh.Profile.Id!.Value);
        command.Parameters.AddWithValue("@store", fresh.StoreCode);
        command.Parameters.Add("@date", SqlDbType.Date).Value = fresh.BusinessDate.ToDateTime(TimeOnly.MinValue);
        command.Parameters.AddWithValue("@report", fresh.ReportGenerationId);
        command.Parameters.AddWithValue("@debit", plan.DebitTotal);
        command.Parameters.AddWithValue("@credit", plan.CreditTotal);
        command.Parameters.Add("@blocking", SqlDbType.NVarChar, 1000).Value = (object?)Cut(plan.BlockingReason, 1000) ?? DBNull.Value;
        command.Parameters.Add("@mappings", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(mappingSet);
        command.Parameters.Add("@vouchers", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(vouchers);
        command.Parameters.Add("@entries", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(entries);
        command.Parameters.Add("@findings", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(fresh.Findings);
        try
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 && exception.Message.Contains("UX_accounting_voucher_reservations_active", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An invoice of this day is already reserved for this Tally company by another batch, so it could be posted twice. Check the earlier batch before preparing again.", exception);
        }
    }

    /// <summary>Plan task 8 (1.12 D1): writes the approved batch's planned vouchers as <c>payload.xml</c> in the batch's evidence
    /// folder, reads that file back (RULE-PAY-001: same company, count, keys and amounts) and only then marks the batch
    /// EXPORTED_AWAITING_IMPORT and its vouchers EXPORTED. Refused for a live company, for a test company whose name could be
    /// mistaken for the real books, and, for TallyPrime in Educational mode, for a date it would refuse. Never written twice.</summary>
    public async Task<TallyVoucherFileReceipt> ExportAsync(long batchId, CancellationToken cancellationToken = default)
    {
        if (batchId <= 0) throw new ArgumentOutOfRangeException(nameof(batchId));
        await RequireOwnerAsync(cancellationToken);
        var head = await LoadExportHeadAsync(batchId, cancellationToken);
        if (head.Kind != "SALES_VOUCHERS" || head.ProfileId is null)
            throw new InvalidOperationException("This batch is a day journal. Only invoice vouchers are written as a Tally voucher file.");
        if (head.Status != TallyBatchStatus.ApprovedReady)
            throw new InvalidOperationException(head.Status == TallyBatchStatus.ExportedAwaitingImport
                ? "This batch's Tally file is already written. A file is never written twice: import that file into Tally, then read back."
                : "Approve the batch before writing its Tally file.");
        var profile = (await new SqlServerTallyProfileService(connectionString).LoadAsync(cancellationToken)).FirstOrDefault(item => item.Id == head.ProfileId)
            ?? throw new InvalidOperationException("This batch's Tally company no longer exists.");
        if (ExportRefusal(profile) is { } refusal) throw new InvalidOperationException(refusal);

        var drafts = await LoadDraftsAsync(batchId, cancellationToken);
        if (drafts.Count == 0) throw new InvalidOperationException("This batch has no voucher ready to send; every invoice is left out.");
        if (TallyEducationalCalendar.AppliesTo(profile) && TallyEducationalCalendar.Default.Refusal(drafts.Select(draft => draft.VoucherDate)) is { } dates)
            throw new InvalidOperationException(dates);

        var content = TallySalesVoucherXmlWriter.Write(profile, drafts);
        if (TallySalesVoucherXmlWriter.Verify(content, profile.CompanyName, drafts) is { } problem)
            throw new InvalidOperationException($"RULE-PAY-001: {problem} Nothing was written.");
        var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var evidence = new TallyEvidenceStore(connectionString, evidenceRootOverride);
        var relative = $@"{TallyEvidencePaths.BatchFolder(profile.ProfileCode, head.StoreCode, head.BusinessDate, batchId)}\payload.xml";
        var earlier = (await evidence.VerifyAsync(batchId, cancellationToken)).Where(check => check.Artifact.Kind == "PAYLOAD_XML").ToArray();
        TallyArtifact artifact;
        if (earlier.Length == 0)
            artifact = await evidence.WriteAsync(batchId, "PAYLOAD_XML", relative, content, cancellationToken);
        else if (earlier.Length == 1 && earlier[0].State == TallyEvidenceState.Ok && earlier[0].Artifact.Sha256 == sha)
            // Written by an earlier run that stopped before the batch was marked: the same approved vouchers give the same bytes.
            artifact = earlier[0].Artifact;
        else
            throw new InvalidOperationException("A different Tally file is already recorded for this batch. Nothing was marked; check the batch's evidence folder.");

        // RULE-PAY-001 on the file as it lies on disk, read once and hashed.
        var onDisk = await evidence.ReadVerifiedAsync(artifact, cancellationToken)
            ?? throw new InvalidOperationException("The Tally file changed or vanished right after it was written. Nothing was marked.");
        if (TallySalesVoucherXmlWriter.Verify(onDisk, profile.CompanyName, drafts) is { } diskProblem)
            throw new InvalidOperationException($"RULE-PAY-001: {diskProblem} Nothing was marked.");

        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.accounting_batches WITH(UPDLOCK,HOLDLOCK)
                WHERE accounting_batch_id=@batch AND status='APPROVED_READY' AND batch_kind='SALES_VOUCHERS' AND tally_profile_id=@profile)
              THROW 51940,'This batch is no longer approved and ready. Refresh the list; nothing was marked.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.tally_profiles WITH(UPDLOCK,HOLDLOCK) WHERE tally_profile_id=@profile AND is_enabled=1 AND environment='TEST'
                AND production_enabled_utc IS NULL AND CONVERT(varbinary(400),company_name)=CONVERT(varbinary(400),@company))
              THROW 51941,'The Tally company changed while the file was written. Nothing was marked; write the file again.',1;
            IF (SELECT COUNT(*) FROM dbo.accounting_vouchers WHERE accounting_batch_id=@batch AND voucher_status='PLANNED')<>@count
              THROW 51942,'The batch''s vouchers changed while the file was written. Nothing was marked.',1;
            INSERT dbo.tally_attempts(accounting_batch_id,tally_profile_id,payload_artifact_id,delivery_mode,payload_format,attempt_status,completed_utc)
            VALUES(@batch,@profile,@artifact,'FILE','XML','FILE_WRITTEN',SYSUTCDATETIME());
            INSERT dbo.accounting_export_receipts(accounting_batch_id,output_path,sha256,tally_company_name,environment_label,exported_by)
            VALUES(@batch,@path,@sha,@company,'TEST',SUSER_SNAME());
            EXEC sys.sp_set_session_context @key=N'etp.status_reason',@value=N'Tally voucher file written; Tally has not imported it yet';
            UPDATE dbo.accounting_batches SET status='EXPORTED_AWAITING_IMPORT',exported_utc=SYSUTCDATETIME(),export_sha256=@sha WHERE accounting_batch_id=@batch;
            UPDATE dbo.accounting_vouchers SET voucher_status='EXPORTED' WHERE accounting_batch_id=@batch AND voucher_status='PLANNED';
            EXEC sys.sp_set_session_context @key=N'etp.status_reason',@value=NULL;
            EXEC dbo.record_operational_audit 'AccountingExport','Succeeded',N'Tally voucher file written; Tally result has not been checked',N'database';
            COMMIT TRANSACTION;
            SELECT exported_utc FROM dbo.accounting_batches WHERE accounting_batch_id=@batch;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.AddWithValue("@profile", profile.Id!.Value);
        command.Parameters.Add("@company", SqlDbType.NVarChar, 200).Value = profile.CompanyName;
        command.Parameters.AddWithValue("@count", drafts.Count);
        command.Parameters.AddWithValue("@artifact", artifact.Id);
        command.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = artifact.RelativePath;
        command.Parameters.Add("@sha", SqlDbType.Char, 64).Value = artifact.Sha256;
        var exported = (DateTime)(await command.ExecuteScalarAsync(cancellationToken))!;
        return new(batchId, profile.CompanyName, artifact.RelativePath, artifact.Sha256, drafts.Count, drafts.Sum(draft => draft.Total), exported);
    }

    /// <summary>Null when vouchers may be written for this company now; otherwise the sentence to show.</summary>
    public static string? ExportRefusal(TallyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsEnabled) return "This Tally company is switched off in Settings → Tally companies.";
        if (profile.Environment != "TEST" || profile.ProductionEnabledUtc is not null)
            return "Live books are not enabled in this version. Only the test company can be written (decision 48).";
        if (!TallyTestCompany.IsTheTestCompany(profile.CompanyName) && !profile.CompanyName.Contains("TEST", StringComparison.Ordinal))
            return $"A test company must be '{TallyTestCompany.Name}' or have TEST in its name, so its file is never mistaken for the real books.";
        return TallySalesVoucherComposer.Unsupported(profile);
    }

    private sealed record ExportHead(string Kind, string Status, string StoreCode, DateOnly BusinessDate, int? ProfileId);

    private async Task<ExportHead> LoadExportHeadAsync(long batchId, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(
            "SELECT batch_kind,status,store_code,business_date,tally_profile_id FROM dbo.accounting_batches WHERE accounting_batch_id=@batch", connection);
        command.Parameters.AddWithValue("@batch", batchId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("The accounting batch was not found.");
        return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)),
            reader.IsDBNull(4) ? null : reader.GetInt32(4));
    }

    /// <summary>The PLANNED vouchers with their saved entries. Each voucher's plan hash is computed again from the saved
    /// entries; a voucher changed after it was prepared is refused, never written.</summary>
    private async Task<IReadOnlyList<AccountingVoucherDraft>> LoadDraftsAsync(long batchId, CancellationToken token)
    {
        const string sql = """
            SELECT v.voucher_sequence,v.correspondence_key,v.voucher_type,v.voucher_date,v.document_number,v.plan_sha256,
                   e.business_event,e.ledger_name,e.debit_amount,e.credit_amount,e.cost_centre,e.narration,e.tax_rate
            FROM dbo.accounting_vouchers v JOIN dbo.accounting_entries e ON e.accounting_voucher_id=v.accounting_voucher_id
            WHERE v.accounting_batch_id=@batch AND v.voucher_status='PLANNED'
            ORDER BY v.voucher_sequence,e.line_number;
            """;
        var heads = new List<(int Sequence, string Key, string Type, DateOnly Date, string Document, string PlanSha, string Narration)>();
        var entries = new Dictionary<int, List<PlannedEntry>>();
        await using (var connection = await OpenAsync(token))
        await using (var command = new SqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var sequence = reader.GetInt32(0);
                if (!entries.TryGetValue(sequence, out var list))
                {
                    entries[sequence] = list = [];
                    heads.Add((sequence, reader.GetString(1), reader.GetString(2), DateOnly.FromDateTime(reader.GetDateTime(3)), reader.GetString(4),
                        reader.GetString(5), reader.GetString(11)));
                }
                list.Add(new(list.Count + 1, reader.GetString(6), reader.GetString(7), reader.GetDecimal(8), reader.GetDecimal(9), Text(reader, 10),
                    reader.IsDBNull(12) ? null : reader.GetDecimal(12)));
            }
        }
        var drafts = new List<AccountingVoucherDraft>();
        foreach (var head in heads)
        {
            var lines = entries[head.Sequence];
            if (!string.Equals(TallySalesVoucherComposer.PlanHash(head.Key, head.Date, lines), head.PlanSha, StringComparison.Ordinal))
                throw new InvalidOperationException($"Invoice {head.Document}: the saved voucher no longer matches what was prepared and approved. Nothing was written; reject the batch and prepare the day again.");
            drafts.Add(new(head.Sequence, head.Key, head.Type, head.Date, head.Document, head.Narration,
                lines.Select(line => new AccountingVoucherLine(line.LedgerName, line.Debit, line.Credit, line.CostCentre)).ToArray()));
        }
        return drafts;
    }

    public async Task<IReadOnlyList<SavedValidationFinding>> LoadFindingsAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            SELECT f.finding_id,v.voucher_sequence,f.rule_id,f.severity,f.subject,f.explanation,f.corrective_action,f.waived_by,f.waiver_reason
            FROM dbo.accounting_validation_findings f LEFT JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=f.accounting_voucher_id
            WHERE f.accounting_batch_id=@batch ORDER BY CASE f.severity WHEN 'FAIL' THEN 0 ELSE 1 END,v.voucher_sequence,f.finding_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@batch", batchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<SavedValidationFinding>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetString(2), reader.GetString(3), Text(reader, 4),
                reader.GetString(5), Text(reader, 6), Text(reader, 7), Text(reader, 8)));
        return result;
    }

    public Task AcceptWarningAsync(long findingId, string reason, CancellationToken cancellationToken = default) =>
        new SqlServerTallyReconciliationService(connectionString).AcceptWarningAsync(findingId, reason, cancellationToken);

    /// <summary>The day's invoices as task 6 reads them, with the latest final report generation. Tax rows come from one
    /// current R018 import per invoice, so a second import of the same day never doubles the tax. An R018 row belongs to
    /// the invoice keyed by the financial year of its own invoice date (OD-1, IF-019, migration 0041): R018's
    /// INVOICEYEAR is only a label and can differ from that year.</summary>
    public async Task<(long ReportGenerationId, IReadOnlyList<InvoiceAccountingSource> Invoices)> LoadSourceAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            DECLARE @generation bigint=(SELECT TOP(1) daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code=@store AND business_date=@date AND is_final=1 ORDER BY generation_number DESC);
            IF @generation IS NULL THROW 51220,'Finalise the report generation before preparing accounting.',1;
            SELECT @generation;
            -- Decision 41: a bill cancelled the same day (0060's view) is marked, and the composer leaves it out. The view is
            -- read only when it exists, so this still runs on a database from before 1.11.2.
            IF OBJECT_ID(N'dbo.v_sales_cancelled_same_day',N'V') IS NOT NULL
              SELECT i.sales_invoice_id,i.store_code,i.invoice_year,i.document_number,i.transaction_date,
                     CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.v_sales_cancelled_same_day c
                       WHERE c.store_code=i.store_code AND c.document_number=i.document_number AND c.transaction_date=i.transaction_date) THEN 1 ELSE 0 END)
              FROM dbo.sales_invoices i WHERE i.store_code=@store AND i.transaction_date=@date;
            ELSE
              SELECT sales_invoice_id,store_code,invoice_year,document_number,transaction_date,CONVERT(bit,0)
              FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date;
            SELECT l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code=@store AND i.transaction_date=@date;
            WITH invoice_tax AS (
              SELECT i.sales_invoice_id,CAST(r.item_number AS nvarchar(80)) COLLATE DATABASE_DEFAULT AS item_number,
                     r.cgst_rate,r.cgst_amount,r.sgst_utgst_rate,r.sgst_utgst_amount,r.igst_rate,r.igst_amount,r.cess_amount,r.import_file_id,
                     MAX(r.import_file_id) OVER(PARTITION BY i.sales_invoice_id) AS latest_file
              FROM dbo.[etp_r018] r
              JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.is_superseded=0
              JOIN dbo.sales_invoices i ON CAST(r.store_code AS nvarchar(80)) COLLATE DATABASE_DEFAULT=i.store_code
                AND YEAR(r.doc_invoice_date)+CASE WHEN MONTH(r.doc_invoice_date)>=4 THEN 1 ELSE 0 END=i.invoice_year
                AND CAST(r.doc_invoice_no AS nvarchar(80)) COLLATE DATABASE_DEFAULT=i.document_number
              WHERE i.store_code=@store AND i.transaction_date=@date AND CAST(r.transaction_type AS nvarchar(80)) COLLATE DATABASE_DEFAULT='INV')
            SELECT sales_invoice_id,item_number,cgst_rate,cgst_amount,sgst_utgst_rate,sgst_utgst_amount,igst_rate,igst_amount,cess_amount
            FROM invoice_tax WHERE import_file_id=latest_file AND item_number IS NOT NULL;
            SELECT t.sales_invoice_id,t.tender_type,m.mode,t.source_amount
            FROM dbo.reporting_sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
            LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
            WHERE i.store_code=@store AND i.transaction_date=@date;
            -- Decision 74: the same-day BC documents of the day's bills (current R025 rows naming the bill), for netting a
            -- partly cancelled bill. A BC document that names several bills gives each only its own lines (by lineage).
            SELECT DISTINCT i.sales_invoice_id bill_id,b.sales_invoice_id bc_id
            INTO #bc_ref
            FROM dbo.etp_r025 r
            JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.report_code='R025' AND f.is_superseded=0 AND f.data_truth_version=1
            JOIN dbo.sales_invoices i ON i.store_code=@store AND i.transaction_date=@date AND i.store_code=CONVERT(varchar(30),LTRIM(RTRIM(r.store_code)))
              AND i.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.reference_invoice_number))) AND i.transaction_date=r.transaction_date
            JOIN dbo.sales_invoices b ON b.store_code=i.store_code AND b.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.invoice_number)))
              AND b.transaction_date=r.transaction_date AND b.sales_invoice_id<>i.sales_invoice_id
            WHERE UPPER(LTRIM(RTRIM(r.source_transaction_type)))=N'BC' AND NULLIF(LTRIM(RTRIM(r.reference_invoice_number)),N'') IS NOT NULL
              AND (r.reference_invoice_date IS NULL OR r.reference_invoice_date=r.transaction_date);
            SELECT x.bill_id,x.bc_id,b.document_number,CONVERT(bit,CASE WHEN (SELECT COUNT(*) FROM #bc_ref y WHERE y.bc_id=x.bc_id)>1 THEN 1 ELSE 0 END) shared,
                   l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount
            FROM #bc_ref x JOIN dbo.sales_invoices b ON b.sales_invoice_id=x.bc_id JOIN dbo.sales_invoices bill ON bill.sales_invoice_id=x.bill_id
            JOIN dbo.sales_lines l ON l.sales_invoice_id=x.bc_id AND UPPER(LTRIM(RTRIM(COALESCE(l.source_transaction_type,N''))))=N'BC'
            WHERE (SELECT COUNT(*) FROM #bc_ref y WHERE y.bc_id=x.bc_id)=1
               OR EXISTS(SELECT 1 FROM dbo.etp_r025 r JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.report_code='R025' AND f.is_superseded=0
                         WHERE r.source_lineage_id=l.source_lineage_id AND CONVERT(nvarchar(80),LTRIM(RTRIM(r.reference_invoice_number)))=bill.document_number);
            WITH bc_tax AS (
              SELECT b.sales_invoice_id bc_id,CAST(r.item_number AS nvarchar(80)) COLLATE DATABASE_DEFAULT AS item_number,
                     r.cgst_rate,r.cgst_amount,r.sgst_utgst_rate,r.sgst_utgst_amount,r.igst_rate,r.igst_amount,r.cess_amount,r.import_file_id,
                     MAX(r.import_file_id) OVER(PARTITION BY b.sales_invoice_id) AS latest_file
              FROM dbo.[etp_r018] r
              JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.is_superseded=0
              JOIN dbo.sales_invoices b ON CAST(r.store_code AS nvarchar(80)) COLLATE DATABASE_DEFAULT=b.store_code
                AND YEAR(r.doc_invoice_date)+CASE WHEN MONTH(r.doc_invoice_date)>=4 THEN 1 ELSE 0 END=b.invoice_year
                AND CAST(r.doc_invoice_no AS nvarchar(80)) COLLATE DATABASE_DEFAULT=b.document_number
              WHERE b.sales_invoice_id IN(SELECT bc_id FROM #bc_ref) AND CAST(r.transaction_type AS nvarchar(80)) COLLATE DATABASE_DEFAULT='BC')
            SELECT bc_id,item_number,cgst_rate,cgst_amount,sgst_utgst_rate,sgst_utgst_amount,igst_rate,igst_amount,cess_amount
            FROM bc_tax WHERE import_file_id=latest_file AND item_number IS NOT NULL;
            SELECT t.sales_invoice_id,t.tender_type,m.mode,t.source_amount
            FROM dbo.reporting_sales_tenders t LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
            WHERE t.sales_invoice_id IN(SELECT bc_id FROM #bc_ref);
            DROP TABLE #bc_ref;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", (storeCode ?? "").Trim().ToUpperInvariant());
        command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var generation = reader.GetInt64(0);

        var heads = new List<(long Id, string Store, int Year, string Document, DateOnly Date, bool Cancelled)>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            heads.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4)), reader.GetBoolean(5)));

        var lines = heads.ToDictionary(head => head.Id, _ => new List<InvoiceSourceLine>());
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            lines[reader.GetInt64(0)].Add(new(reader.GetString(1), reader.GetString(2), Text(reader, 3), reader.GetDecimal(4), Number(reader, 5), Number(reader, 6), Number(reader, 7)));

        var tax = heads.ToDictionary(head => head.Id, _ => new List<InvoiceTaxRow>());
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tax[reader.GetInt64(0)].Add(new(reader.GetString(1), Number(reader, 2), Number(reader, 3), Number(reader, 4), Number(reader, 5), Number(reader, 6), Number(reader, 7), Number(reader, 8)));

        var tenders = heads.ToDictionary(head => head.Id, _ => new List<InvoiceSourceTender>());
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            tenders[reader.GetInt64(0)].Add(new(reader.GetString(1), Text(reader, 2), reader.GetDecimal(3)));

        // Decision 74: a partly cancelled bill goes to Tally net of its same-day BC lines.
        var bcLines = new List<(long Bill, long Bc, string Document, bool Shared, InvoiceSourceLine Line)>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            bcLines.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetBoolean(3),
                new(reader.GetString(4), reader.GetString(5), Text(reader, 6), reader.GetDecimal(7), Number(reader, 8), Number(reader, 9), Number(reader, 10))));
        var bcTax = new Dictionary<long, List<InvoiceTaxRow>>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!bcTax.TryGetValue(reader.GetInt64(0), out var rows)) bcTax[reader.GetInt64(0)] = rows = [];
            rows.Add(new(reader.GetString(1), Number(reader, 2), Number(reader, 3), Number(reader, 4), Number(reader, 5), Number(reader, 6), Number(reader, 7), Number(reader, 8)));
        }
        var bcTenders = new Dictionary<long, List<InvoiceSourceTender>>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!bcTenders.TryGetValue(reader.GetInt64(0), out var rows)) bcTenders[reader.GetInt64(0)] = rows = [];
            rows.Add(new(reader.GetString(1), Text(reader, 2), reader.GetDecimal(3)));
        }
        return (generation, Assemble(heads.Select(head => new InvoiceAccountingSource(head.Id, head.Store, head.Year, head.Document, head.Date,
            lines[head.Id], tax[head.Id], tenders[head.Id], head.Cancelled)).ToArray(), bcLines, bcTax, bcTenders));
    }

    /// <summary>Decision 74: nets each partly cancelled bill (not in <c>v_sales_cancelled_same_day</c>) by its BC parts and marks
    /// those BC documents as taken off it. A bill cancelled whole, and its BC document, stay as before.</summary>
    internal static IReadOnlyList<InvoiceAccountingSource> Assemble(IReadOnlyList<InvoiceAccountingSource> invoices,
        IReadOnlyList<(long Bill, long Bc, string Document, bool Shared, InvoiceSourceLine Line)> bcLines,
        IReadOnlyDictionary<long, List<InvoiceTaxRow>> bcTax, IReadOnlyDictionary<long, List<InvoiceSourceTender>> bcTenders)
    {
        var netted = new Dictionary<long, InvoiceAccountingSource>();
        var cancels = new Dictionary<long, SortedSet<string>>();
        foreach (var invoice in invoices)
        {
            if (invoice.CancelledSameDay) continue;
            var parts = bcLines.Where(row => row.Bill == invoice.SalesInvoiceId).GroupBy(row => (row.Bc, row.Document, row.Shared))
                .Select(group => new SameDayCancellationPart(group.Key.Document, group.Select(row => row.Line).ToArray(),
                    bcTax.TryGetValue(group.Key.Bc, out var rows) ? rows : [], bcTenders.TryGetValue(group.Key.Bc, out var paid) ? paid : [], group.Key.Shared))
                .ToArray();
            if (parts.Length == 0) continue;
            var net = netted[invoice.SalesInvoiceId] = TallySameDayCancellation.Net(invoice, parts);
            // Every item taken back: the bill and its BC document stay out as a whole-bill cancellation does.
            if (net.CancelledSameDay) continue;
            foreach (var bc in bcLines.Where(row => row.Bill == invoice.SalesInvoiceId).Select(row => row.Bc).Distinct())
            {
                if (!cancels.TryGetValue(bc, out var bills)) cancels[bc] = bills = new(StringComparer.Ordinal);
                bills.Add(invoice.DocumentNumber);
            }
        }
        return invoices.Select(invoice => netted.TryGetValue(invoice.SalesInvoiceId, out var nettedBill) ? nettedBill
            : cancels.TryGetValue(invoice.SalesInvoiceId, out var bills) ? invoice with { CancelsPartOf = string.Join(", ", bills) }
            : invoice).ToArray();
    }

    /// <summary>Approved, active mappings on the date; a store-specific mapping wins over the all-stores one.</summary>
    private async Task<IReadOnlyList<TallyLedgerMapping>> LoadMappingsAsync(string storeCode, DateOnly date, CancellationToken token)
    {
        const string sql = """
            SELECT m.business_event,m.accounting_mapping_id,m.version,m.debit_ledger,m.credit_ledger
            FROM dbo.accounting_mappings m JOIN dbo.approval_requests a ON a.approval_request_id=m.approval_request_id AND a.status='APPROVED'
            WHERE m.is_active=1 AND (m.store_code IS NULL OR m.store_code=@store) AND m.effective_from<=@date AND (m.effective_to IS NULL OR m.effective_to>=@date)
            ORDER BY CASE WHEN m.store_code=@store THEN 0 ELSE 1 END,m.version DESC;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode);
        command.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
        await using var reader = await command.ExecuteReaderAsync(token);
        var result = new List<TallyLedgerMapping>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
            if (seen.Add(reader.GetString(0)))
                result.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
        return result;
    }

    private async Task RequireOwnerAsync(CancellationToken token)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static string? Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static decimal? Number(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    private static string? Cut(string? value, int length) => value is null ? null : value.Length <= length ? value : value[..length];
}
