using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record RetailEnrichmentImportOutcome(
    Guid BatchId,
    long ImportFileId,
    string ReportCode,
    int PersistedRows,
    int MatchedRows,
    int MissingMatches,
    int AmbiguousMatches);

public sealed class RetailEnrichmentSqlImportOrchestrator(string connectionString)
{
    public Task<RetailEnrichmentImportOutcome> PersistAsync(
        WorkbookSnapshot workbook,
        string reportCode,
        DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null,
        string? importedBy = null,
        CancellationToken cancellationToken = default,
        ImportRestatementRequest? restatement = null)
    {
        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);
        if (inspection.AcceptedImport is null) throw new SalesImportBlockedException(inspection.Diagnostics);
        var accepted = inspection.AcceptedImport;
        if (!string.Equals(accepted.ProfileIdentity.ReportCode, reportCode, StringComparison.Ordinal))
            throw new InvalidOperationException("The accepted import profile does not match the requested enrichment report.");
        return PersistAsync(accepted, expectedBusinessDate, expectedStoreCode, importedBy, cancellationToken, restatement);
    }

    public async Task<RetailEnrichmentImportOutcome> PersistAsync(
        MatchedImportEnvelope accepted,
        DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null,
        string? importedBy = null,
        CancellationToken cancellationToken = default,
        ImportRestatementRequest? restatement = null)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        _ = ApprovedImportProfileRegistry.Resolve(accepted.ProfileIdentity);
        var reportCode = accepted.ProfileIdentity.ReportCode;
        if (reportCode is not ("R003" or "R013"))
            throw new ArgumentException("Only R003 and R013 are enrichment profiles.", nameof(accepted));
        if (!accepted.Staging.CanPersist) throw new SalesImportBlockedException(accepted.Diagnostics);

        var stores = accepted.Staging.Rows.Select(x => Required<string>(x.Values, "store_code"))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (stores.Length > 1) throw new InvalidOperationException("A source workbook cannot contain more than one store.");
        var dates = accepted.Staging.Rows.Select(x => Required<DateOnly>(x.Values, "transaction_date")).ToArray();
        var scope = R025SqlImportOrchestrator.ValidateScope(stores.SingleOrDefault(), dates.Length == 0 ? null : dates.Max(),
            expectedStoreCode, expectedBusinessDate);
        if (scope.StoreCode is null || scope.BusinessDate is null)
            throw new InvalidOperationException("A header-only enrichment report requires a selected store and business date.");

        var batchId = Guid.NewGuid();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var command = Command(connection, transaction, "INSERT dbo.import_batches(import_batch_id,status,period_start,period_end,started_utc) VALUES(@batch,'Processing',@start,@end,SYSUTCDATETIME())"))
            {
                command.Parameters.AddWithValue("@batch", batchId);
                command.Parameters.AddWithValue("@start", (object?)(dates.Length == 0 ? scope.BusinessDate : dates.Min()) ?? DBNull.Value);
                command.Parameters.AddWithValue("@end", scope.BusinessDate);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            long fileId;
            var profileId = await SqlServerImportProfileResolver.ResolveOrRegisterAsync(
                connection, transaction, accepted.ProfileIdentity, cancellationToken);
            await using (var command = Command(connection, transaction, "INSERT dbo.import_files(import_batch_id,import_profile_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,source_report_date,imported_by) VALUES(@batch,@profile,@name,@hash,@size,@report,@store,@date,@date,@user); SELECT CONVERT(bigint,SCOPE_IDENTITY());"))
            {
                command.Parameters.AddWithValue("@batch", batchId);
                command.Parameters.AddWithValue("@profile", profileId);
                command.Parameters.AddWithValue("@name", accepted.Workbook.FileName);
                command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(accepted.Workbook.Sha256));
                command.Parameters.AddWithValue("@size", accepted.Workbook.FileSizeBytes);
                command.Parameters.AddWithValue("@report", reportCode);
                command.Parameters.AddWithValue("@store", scope.StoreCode);
                command.Parameters.AddWithValue("@date", scope.BusinessDate);
                command.Parameters.AddWithValue("@user", importedBy ?? Environment.UserName);
                fileId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            }

            if (restatement is not null)
            {
                if (restatement.PreviousImportFileId <= 0 || string.IsNullOrWhiteSpace(restatement.RequestedBy) || string.IsNullOrWhiteSpace(restatement.Reason))
                    throw new ArgumentException("A restatement requires the previous file, requesting user and reason.", nameof(restatement));
                await using var command = Command(connection, transaction,
                    "EXEC dbo.prepare_import_restatement @previous,@replacement,@user,@reason");
                command.Parameters.AddWithValue("@previous", restatement.PreviousImportFileId);
                command.Parameters.AddWithValue("@replacement", fileId);
                command.Parameters.AddWithValue("@user", restatement.RequestedBy.Trim());
                command.Parameters.AddWithValue("@reason", restatement.Reason.Trim());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var matched = 0;
            var missing = 0;
            var ambiguous = 0;
            foreach (var row in accepted.Staging.Rows)
            {
                var result = await InsertRowAsync(connection, transaction, fileId, reportCode, accepted.MatchedSheet.Name, row, cancellationToken);
                if (result == "Matched") matched++;
                else if (result == "Missing") missing++;
                else ambiguous++;
            }

            await using (var command = Command(connection, transaction,
                "UPDATE dbo.import_batches SET status='Completed',source_row_count=@rows,completed_utc=SYSUTCDATETIME() WHERE import_batch_id=@batch AND status='Processing'"))
            {
                command.Parameters.AddWithValue("@rows", accepted.Staging.Rows.Count);
                command.Parameters.AddWithValue("@batch", batchId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The enrichment batch could not be completed.");
            }
            await transaction.CommitAsync(cancellationToken);
            return new(batchId, fileId, reportCode, accepted.Staging.Rows.Count, matched, missing, ambiguous);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<string> InsertRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long fileId,
        string reportCode,
        string sheetName,
        StagedImportRow row,
        CancellationToken token)
    {
        long lineageId;
        await using (var lineage = Command(connection, transaction,
            "INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) OUTPUT INSERTED.source_lineage_id VALUES(@file,@sheet,@row,@type)"))
        {
            lineage.Parameters.AddWithValue("@file", fileId);
            lineage.Parameters.AddWithValue("@sheet", sheetName);
            lineage.Parameters.AddWithValue("@row", row.SourceRowNumber);
            lineage.Parameters.AddWithValue("@type", $"{reportCode}_ENRICHMENT");
            lineageId = Convert.ToInt64(await lineage.ExecuteScalarAsync(token));
        }

        const string sql = """
            DECLARE @matchCount int,@salesLineId bigint;
            SELECT @matchCount=COUNT(*),@salesLineId=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
            FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
            WHERE i.store_code=@store AND i.transaction_date=@date AND i.document_number=@document AND l.product_code=@product;
            DECLARE @status varchar(20)=CASE @matchCount WHEN 0 THEN 'Missing' WHEN 1 THEN 'Matched' ELSE 'Ambiguous' END;
            INSERT dbo.sales_line_enrichments
              (enrichment_type,store_code,transaction_date,document_number,product_code,source_transaction_type,source_quantity,source_net_value,
               source_cro_number,scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,
               matched_sales_line_id,match_status,source_lineage_id)
            VALUES(@report,@store,@date,@document,@product,@type,@quantity,@net,@cro,@scheme,@userDiscount,@pre,@other,@activation,@discountDetails,
                   @salesLineId,@status,@lineage);
            SELECT @status;
            """;
        await using var command = Command(connection, transaction, sql);
        var values = row.Values;
        command.Parameters.AddWithValue("@report", reportCode);
        command.Parameters.AddWithValue("@store", Required<string>(values, "store_code"));
        command.Parameters.AddWithValue("@date", Required<DateOnly>(values, "transaction_date"));
        command.Parameters.AddWithValue("@document", Required<string>(values, "invoice_number"));
        command.Parameters.AddWithValue("@product", Required<string>(values, "product_code"));
        command.Parameters.AddWithValue("@type", Required<string>(values, "source_transaction_type"));
        command.Parameters.AddWithValue("@quantity", Required<decimal>(values, "source_quantity"));
        command.Parameters.AddWithValue("@net", Required<decimal>(values, "source_net_value"));
        Add(command, "@cro", OptionalString(values, "cro_number"));
        Add(command, "@scheme", OptionalDecimal(values, "scheme_discount"));
        Add(command, "@userDiscount", OptionalDecimal(values, "user_discount"));
        Add(command, "@pre", OptionalDecimal(values, "pre_discount"));
        Add(command, "@other", OptionalDecimal(values, "other_charges"));
        Add(command, "@activation", OptionalString(values, "activation_details"));
        Add(command, "@discountDetails", OptionalString(values, "user_discount_details"));
        command.Parameters.AddWithValue("@lineage", lineageId);
        return (string)(await command.ExecuteScalarAsync(token) ?? throw new InvalidOperationException("Enrichment match status was not returned."));
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql) =>
        new(sql, connection, transaction) { CommandTimeout = 0 };

    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static T Required<T>(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed : throw new InvalidOperationException($"Required staged field '{key}' is missing.");

    private static decimal? OptionalDecimal(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is decimal typed ? typed : null;

    private static string? OptionalString(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? value as string : null;
}
