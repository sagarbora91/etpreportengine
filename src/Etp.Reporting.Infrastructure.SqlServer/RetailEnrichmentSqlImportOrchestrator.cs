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

        var scope = R025SqlImportOrchestrator.ValidateScope(accepted.Scope.StoreCode, accepted.Scope.PeriodEnd,
            expectedStoreCode, expectedBusinessDate);
        var keys = EtpInvoiceIdentity.LineKeys(accepted.Staging.Rows);
        var rows = accepted.Staging.Rows.Select(row =>
        {
            var v = row.Values;
            var type = Required<string>(v, "source_transaction_type");
            var sign = type is "SR" or "BC" ? -1m : 1m;
            return new EnrichmentPersistence(reportCode, Required<string>(v,"store_code"), Required<DateOnly>(v,"transaction_date"),
                Required<string>(v,"invoice_number"), Required<string>(v,"product_code"), type,
                sign * Math.Abs(Required<decimal>(v,"source_quantity")), sign * Math.Abs(Required<decimal>(v,"source_net_value")),
                sign * Math.Abs(Required<decimal>(v,"source_net_amount")), OptionalString(v,"cro_number"), OptionalString(v,"cro_name") ?? OptionalString(v,"staff_name"),
                OptionalDecimal(v,"scheme_discount"), OptionalDecimal(v,"user_discount"), OptionalDecimal(v,"pre_discount"), OptionalDecimal(v,"other_charges"),
                OptionalString(v,"activation_details"), OptionalString(v,"user_discount_details"), keys[row.SourceRowNumber],
                new(accepted.MatchedSheet.Name,row.SourceRowNumber,$"{reportCode}_ENRICHMENT"));
        }).ToArray();
        var batchId = Guid.NewGuid();
        var package = new ImportPersistencePackage(
            new(batchId,null,accepted.Scope.PeriodStart ?? scope.BusinessDate,scope.BusinessDate,DateTimeOffset.UtcNow),
            new(batchId,accepted.ProfileIdentity,accepted.Workbook.FileName,accepted.Workbook.Sha256,accepted.Workbook.FileSizeBytes,
                reportCode,scope.StoreCode,scope.BusinessDate,scope.BusinessDate,importedBy ?? Environment.UserName,
                accepted.Scope.PeriodStart ?? scope.BusinessDate,scope.BusinessDate), [],[],[],[])
            { Enrichments=rows, AcceptedImport=accepted, Restatement=restatement };
        var id = await new SqlServerTransactionalImportStore(connectionString).PersistAsync(package,cancellationToken);
        await using var connection=new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        await using var query=new SqlCommand("SELECT e.match_status,COUNT(*) FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@file GROUP BY e.match_status",connection);
        query.Parameters.AddWithValue("@file",id);
        var counts=new Dictionary<string,int>();
        await using var reader=await query.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) counts[reader.GetString(0)]=reader.GetInt32(1);
        return new(batchId,id,reportCode,rows.Length,counts.GetValueOrDefault("Matched"),counts.GetValueOrDefault("Missing"),counts.GetValueOrDefault("Ambiguous"));
    }

    private static T Required<T>(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed : throw new InvalidOperationException($"Required staged field '{key}' is missing.");

    private static decimal? OptionalDecimal(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is decimal typed ? typed : null;

    private static string? OptionalString(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? value as string : null;
}
