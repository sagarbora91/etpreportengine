using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using App = Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class ProductisationRepository
{
    public async Task<App.AccountingDestination> LoadAccountingDestinationAsync(CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT tally_company_name,tally_environment_label FROM dbo.product_settings WHERE product_setting_id=1", connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Product settings are not initialized.");
        return new(OptionalString(reader, 0), reader.GetString(1));
    }

    public async Task SaveAccountingDestinationAsync(App.SaveAccountingDestination value, CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        var company = Clean(value.CompanyName);
        var environment = value.EnvironmentLabel.Trim().ToUpperInvariant();
        if (company?.Length > 200) throw new ArgumentException("The company name must be at most 200 characters.");
        if (environment is not ("TEST" or "PRODUCTION")) throw new ArgumentException("Choose TEST or PRODUCTION.");
        if (string.IsNullOrWhiteSpace(value.Reason) || value.Reason.Length > 1000) throw new ArgumentException("Enter a settings change reason of at most 1000 characters.");
        if (environment == "PRODUCTION" && (company is null || !string.Equals(company, value.CompanyConfirmation, StringComparison.Ordinal)))
            throw new ArgumentException("Type the exact company name to confirm PRODUCTION. Live export remains unavailable until Phase 7 approval.");
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            UPDATE dbo.product_settings SET tally_company_name=@company,tally_environment_label=@environment,
              modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason WHERE product_setting_id=1;
            EXEC dbo.record_operational_audit 'ConfigurationChange','Succeeded',N'Tally destination changed; live transfer is not enabled',N'database';
            COMMIT TRANSACTION;
            """;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@company", company); command.Parameters.AddWithValue("@environment", environment);
        command.Parameters.AddWithValue("@reason", value.Reason.Trim());
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<long> SaveAccountingBatchAsync(string storeCode, DateOnly businessDate, long reportGenerationId,
        AccountingBatchDraft batch, CancellationToken cancellationToken = default)
    {
        await EnsureOwnerAsync(cancellationToken);
        var source = await LoadAccountingSourceAsync(storeCode, businessDate, cancellationToken);
        if (source.GenerationId != reportGenerationId) throw new InvalidOperationException("The final report changed. Prepare the accounting preview again.");
        var fresh = new AccountingBatchComposer().Compose(source.Events, await LoadApprovedAccountingMappingsAsync(storeCode, businessDate, cancellationToken));
        if (!fresh.Entries.SequenceEqual(batch.Entries) || !fresh.MissingMappings.SequenceEqual(batch.MissingMappings))
            throw new InvalidOperationException("The source or ledger mappings changed. Prepare the accounting preview again.");
        var destination = await LoadAccountingDestinationAsync(cancellationToken);
        var reasons = new List<string>();
        if (fresh.MissingMappings.Count > 0) reasons.Add("Missing approved mappings: " + string.Join(", ", fresh.MissingMappings));
        if (string.IsNullOrWhiteSpace(destination.CompanyName)) reasons.Add("Tally company not decided (D12). Set the intended TEST company in Settings.");
        if (destination.EnvironmentLabel == "PRODUCTION") reasons.Add("Live Tally export is not enabled (D18 / Phase 7). Select TEST in Settings.");
        if (source.Events.Count == 0) reasons.Add("No accounting events exist for this day.");
        var blocking = reasons.Count == 0 ? null : string.Join(" ", reasons);
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
            DECLARE @previous bigint=(SELECT TOP(1) accounting_batch_id FROM dbo.accounting_batches WITH(UPDLOCK,HOLDLOCK)
              WHERE store_code=@store AND business_date=@date AND status<>'REJECTED' ORDER BY accounting_batch_id);
            IF @previous IS NOT NULL BEGIN DECLARE @message nvarchar(2048)=CONCAT('This day is already in batch ',@previous,'. Reject that unexported batch before preparing another.'); THROW 51452,@message,1; END;
            DECLARE @number int=ISNULL((SELECT MAX(accounting_generation) FROM dbo.accounting_batches WHERE store_code=@store AND business_date=@date),0)+1;
            INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,blocking_reason,created_by)
            VALUES(@store,@date,@report,@number,@debit,@credit,CASE WHEN @blocking IS NULL THEN 'DRAFT' ELSE 'BLOCKED' END,@blocking,SUSER_SNAME());
            DECLARE @id bigint=SCOPE_IDENTITY();
            INSERT dbo.accounting_entries(accounting_batch_id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,cost_centre,source_reference)
            SELECT @id,line_number,business_event,ledger_name,debit_amount,credit_amount,narration,cost_centre,source_reference FROM OPENJSON(@entries)
            WITH(line_number int '$.LineNumber',business_event varchar(50) '$.BusinessEvent',ledger_name nvarchar(200) '$.LedgerName',debit_amount decimal(19,4) '$.DebitAmount',credit_amount decimal(19,4) '$.CreditAmount',narration nvarchar(500) '$.Narration',cost_centre nvarchar(200) '$.CostCentre',source_reference nvarchar(200) '$.SourceReference');
            INSERT dbo.accounting_batch_invoices(accounting_batch_id,store_code,invoice_year,document_number,is_active)
            SELECT @id,store_code,invoice_year,document_number,1 FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date;
            EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Accounting batch prepared; readiness recorded',N'database';
            COMMIT TRANSACTION; SELECT @id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@store", storeCode.Trim().ToUpperInvariant()); command.Parameters.AddWithValue("@date", businessDate);
        command.Parameters.AddWithValue("@report", reportGenerationId); command.Parameters.AddWithValue("@debit", fresh.DebitTotal);
        command.Parameters.AddWithValue("@credit", fresh.CreditTotal); Add(command, "@blocking", blocking);
        command.Parameters.AddWithValue("@entries", JsonSerializer.Serialize(fresh.Entries));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<AccountingBatchRow>> LoadAccountingBatchesAsync(CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        const string sql = "SELECT TOP(500) accounting_batch_id,store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,approved_by,exported_utc,tally_reference,created_utc,blocking_reason FROM dbo.accounting_batches ORDER BY business_date DESC,accounting_generation DESC";
        await using var connection = await OpenAsync(token); await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(token); var rows = new List<AccountingBatchRow>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0),reader.GetString(1),DateOnly.FromDateTime(reader.GetDateTime(2)),reader.GetInt64(3),reader.GetInt32(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetString(7),OptionalString(reader,8),reader.IsDBNull(9)?null:reader.GetDateTime(9),OptionalString(reader,10),reader.GetDateTime(11),OptionalString(reader,12)));
        return rows;
    }

    public async Task<IReadOnlyList<AccountingEntryDraft>> LoadAccountingEntriesAsync(long batchId, CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT line_number,business_event,ledger_name,debit_amount,credit_amount,narration,cost_centre,source_reference FROM dbo.accounting_entries WHERE accounting_batch_id=@id ORDER BY line_number", connection);
        command.Parameters.AddWithValue("@id",batchId);
        await using var reader = await command.ExecuteReaderAsync(token); var rows = new List<AccountingEntryDraft>();
        while(await reader.ReadAsync(token)) rows.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),reader.GetDecimal(4),reader.GetString(5),OptionalString(reader,6),reader.GetString(7)));
        return rows;
    }

    public async Task ApproveAccountingBatchAsync(long batchId, string reason, CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ArgumentException("Enter an accounting approval reason of at most 1000 characters.");
        const string sql = """
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            IF NOT EXISTS(SELECT 1 FROM dbo.product_settings WITH(UPDLOCK,HOLDLOCK) WHERE product_setting_id=1 AND NULLIF(LTRIM(RTRIM(tally_company_name)),'') IS NOT NULL AND tally_environment_label='TEST')
              THROW 51460,'Set the intended TEST Tally company in Settings before approving (D12/D18).',1;
            UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=@reason,approved_by=SUSER_SNAME(),approved_utc=SYSUTCDATETIME()
              WHERE accounting_batch_id=@id AND status='DRAFT' AND blocking_reason IS NULL AND debit_total=credit_total
              AND EXISTS(SELECT 1 FROM dbo.accounting_entries WHERE accounting_batch_id=@id);
            IF @@ROWCOUNT<>1 THROW 51221,'Only a balanced, unblocked draft can be approved. Correct the setup and reject/reprepare a blocked batch.',1;
            EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Accounting batch approved and ready for file export',N'database';
            COMMIT TRANSACTION;
            """;
        await using var connection = await OpenAsync(token); await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id",batchId); command.Parameters.AddWithValue("@reason",reason.Trim());
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task RejectAccountingBatchAsync(long batchId,string reason,CancellationToken token=default)
    {
        await EnsureOwnerAsync(token);
        if(string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Enter an accounting rejection reason.",nameof(reason));
        await using var connection=await OpenAsync(token);
        await using var command=new SqlCommand("EXEC dbo.reject_accounting_batch @id,@reason;",connection);
        command.Parameters.AddWithValue("@id",batchId);command.Parameters.AddWithValue("@reason",reason.Trim());
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<App.AccountingExportReceipt> ExportAccountingBatchAsync(long batchId, string outputPath,
        App.AccountingDestination destination, Func<CancellationToken,Task<string>> writeFile, CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        var fullPath = Path.GetFullPath(outputPath);
        if (fullPath.Length > 500) throw new ArgumentException("Choose an export path of at most 500 characters.");
        if (File.Exists(fullPath)) throw new InvalidOperationException("Choose a new file name. An existing export must not be overwritten.");
        await using var connection = await OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        await using (var check = new SqlCommand("""
            DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
            IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.product_settings WITH(UPDLOCK,HOLDLOCK) WHERE product_setting_id=1 AND tally_company_name=@company AND tally_environment_label=@environment AND tally_environment_label='TEST')
              THROW 51460,'Tally destination changed or live export is not enabled. Refresh Settings before exporting.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.accounting_batches WITH(UPDLOCK,HOLDLOCK) WHERE accounting_batch_id=@id AND status='APPROVED_READY')
              THROW 51222,'Approve the accounting batch before export. A rejected or exported batch cannot be exported again.',1;
            """, connection, transaction))
        {
            check.Parameters.AddWithValue("@id",batchId); Add(check,"@company",destination.CompanyName); check.Parameters.AddWithValue("@environment",destination.EnvironmentLabel);
            await check.ExecuteNonQueryAsync(token);
        }
        var written = false;
        var commitStarted = false;
        try
        {
            var hash = await writeFile(token); written = true;
            await using var record = new SqlCommand("""
                INSERT dbo.accounting_export_receipts(accounting_batch_id,output_path,sha256,tally_company_name,environment_label,exported_by)
                VALUES(@id,@path,@hash,@company,@environment,SUSER_SNAME());
                UPDATE dbo.accounting_batches SET status='EXPORTED_AWAITING_IMPORT',exported_utc=SYSUTCDATETIME(),export_sha256=@hash WHERE accounting_batch_id=@id;
                EXEC dbo.record_operational_audit 'AccountingExport','Succeeded',N'Accounting XML exported; Tally result has not been checked',N'database';
                SELECT exported_utc,exported_by FROM dbo.accounting_export_receipts WHERE accounting_export_receipt_id=SCOPE_IDENTITY();
                """, connection, transaction);
            record.Parameters.AddWithValue("@id",batchId); record.Parameters.AddWithValue("@path",fullPath);
            record.Parameters.AddWithValue("@hash",SqlServerImportFileRepository.NormalizeHash(hash)); Add(record,"@company",destination.CompanyName);
            record.Parameters.AddWithValue("@environment",destination.EnvironmentLabel);
            DateTime time; string actor;
            await using(var reader = await record.ExecuteReaderAsync(token)) { await reader.ReadAsync(token); time=reader.GetDateTime(0); actor=reader.GetString(1); }
            commitStarted = true;
            await transaction.CommitAsync(token);
            return new(batchId,fullPath,hash,destination.CompanyName!,destination.EnvironmentLabel,time,actor);
        }
        catch
        {
            // Rollback before commit removes this attempt. An uncertain commit must retain its evidence file.
            if (written && !commitStarted) { try { File.Delete(fullPath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            throw;
        }
    }

    public async Task<IReadOnlyList<App.AccountingExportReceipt>> LoadAccountingExportHistoryAsync(CancellationToken token = default)
    {
        await EnsureOwnerAsync(token);
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT TOP(500) accounting_batch_id,output_path,sha256,tally_company_name,environment_label,exported_utc,exported_by FROM dbo.accounting_export_receipts ORDER BY accounting_export_receipt_id DESC",connection);
        await using var reader = await command.ExecuteReaderAsync(token); var rows = new List<App.AccountingExportReceipt>();
        while(await reader.ReadAsync(token)) rows.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetDateTime(5),reader.GetString(6)));
        return rows;
    }
}
