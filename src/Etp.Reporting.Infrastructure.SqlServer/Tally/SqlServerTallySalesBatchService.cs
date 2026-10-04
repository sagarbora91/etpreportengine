using System.Data;
using System.Globalization;
using System.Text.Json;
using Etp.Reporting.Application.Accounting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

/// <summary>Plan task 6: reads one day's invoices, composes one Sales voucher per invoice and saves them as a
/// <c>SALES_VOUCHERS</c> batch with its vouchers, entries, invoice reservations and the plan task 7 validation findings.
/// Owner only. Nothing is sent to Tally.</summary>
public sealed class SqlServerTallySalesBatchService(string connectionString, Func<DateOnly>? today = null) : ITallySalesBatchService
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
    /// current R018 import per invoice, so a second import of the same day never doubles the tax.</summary>
    public async Task<(long ReportGenerationId, IReadOnlyList<InvoiceAccountingSource> Invoices)> LoadSourceAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        const string sql = """
            DECLARE @generation bigint=(SELECT TOP(1) daily_report_generation_id FROM dbo.daily_report_generations WHERE store_code=@store AND business_date=@date AND is_final=1 ORDER BY generation_number DESC);
            IF @generation IS NULL THROW 51220,'Finalise the report generation before preparing accounting.',1;
            SELECT @generation;
            SELECT sales_invoice_id,store_code,invoice_year,document_number,transaction_date FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date;
            SELECT l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code=@store AND i.transaction_date=@date;
            WITH invoice_tax AS (
              SELECT i.sales_invoice_id,CAST(r.item_number AS nvarchar(80)) COLLATE DATABASE_DEFAULT AS item_number,
                     r.cgst_rate,r.cgst_amount,r.sgst_utgst_rate,r.sgst_utgst_amount,r.igst_rate,r.igst_amount,r.cess_amount,r.import_file_id,
                     MAX(r.import_file_id) OVER(PARTITION BY i.sales_invoice_id) AS latest_file
              FROM dbo.[etp_r018] r
              JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.is_superseded=0
              JOIN dbo.sales_invoices i ON CAST(r.store_code AS nvarchar(80)) COLLATE DATABASE_DEFAULT=i.store_code AND r.invoice_year=i.invoice_year
                AND CAST(r.doc_invoice_no AS nvarchar(80)) COLLATE DATABASE_DEFAULT=i.document_number
              WHERE i.store_code=@store AND i.transaction_date=@date AND CAST(r.transaction_type AS nvarchar(80)) COLLATE DATABASE_DEFAULT='INV')
            SELECT sales_invoice_id,item_number,cgst_rate,cgst_amount,sgst_utgst_rate,sgst_utgst_amount,igst_rate,igst_amount,cess_amount
            FROM invoice_tax WHERE import_file_id=latest_file AND item_number IS NOT NULL;
            SELECT t.sales_invoice_id,t.tender_type,m.mode,t.source_amount
            FROM dbo.reporting_sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
            LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
            WHERE i.store_code=@store AND i.transaction_date=@date;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", (storeCode ?? "").Trim().ToUpperInvariant());
        command.Parameters.Add("@date", SqlDbType.Date).Value = businessDate.ToDateTime(TimeOnly.MinValue);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var generation = reader.GetInt64(0);

        var heads = new List<(long Id, string Store, int Year, string Document, DateOnly Date)>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            heads.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), DateOnly.FromDateTime(reader.GetDateTime(4))));

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

        return (generation, heads.Select(head => new InvoiceAccountingSource(head.Id, head.Store, head.Year, head.Document, head.Date,
            lines[head.Id], tax[head.Id], tenders[head.Id])).ToArray());
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
