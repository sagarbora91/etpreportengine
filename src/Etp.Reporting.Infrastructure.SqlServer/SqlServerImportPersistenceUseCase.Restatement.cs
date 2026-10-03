using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerImportPersistenceUseCase
{
    public async Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return await completion.FindRestatementCandidatesAsync(reportCode, storeCode, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
    }

    // The same test planner 1 applies under its lock (PhaseOneImportPersistence): an import the replacement does not
    // replace is taken over only when every content key it holds is among the replacement's.
    public async Task<IReadOnlyList<long>> FindImportsChangedByAsync(MatchedImportEnvelope accepted, IReadOnlyList<long> importFileIds,
        DateOnly? businessDate = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(importFileIds);
        if (importFileIds.Count == 0) return [];
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        // Keyed as planner 1 keys them: an undated snapshot's rows carry the date the import is persisted with.
        var incoming = SqlServerTransactionalImportStore.ContentKeys(accepted, businessDate ?? accepted.Scope.PeriodEnd).Values.ToHashSet(StringComparer.Ordinal);
        var changed = new List<long>();
        await using var connection = LocalConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        foreach (var id in importFileIds.Distinct())
        {
            await using var command = new SqlCommand("""
                SELECT k.content_key FROM dbo.import_files f
                JOIN dbo.etp_import_content k ON k.import_file_id=f.import_file_id
                WHERE f.import_file_id=@id AND f.is_superseded=0 AND k.content_key IS NOT NULL;
                """, connection);
            command.Parameters.AddWithValue("@id", id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                if (!incoming.Contains(reader.GetString(0)))
                {
                    changed.Add(id);
                    break;
                }
        }
        return changed;
    }

    // A missing approval is the importer's own refusal, not an access failure. As UnauthorizedAccessException
    // it was reported as "The workbook could not be accessed." (spec 11.1).
    internal static ImportSourceException RestatementApprovalRequired() => new(ImportCodes.RestatementApprovalRequired,
        "This exact replacement and reason require unused Owner approval before import. Current facts are unchanged.");

    // Requesting approval never writes import facts. Each caller must still pass
    // the independent check in PersistAsync and SQL's atomic approval consumption.
    public async Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AcceptedImport);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        var restatement = Map(request.Restatement);
        if (restatement is null) return;
        var accepted = request.AcceptedImport;
        _ = ApprovedImportProfileRegistry.Resolve(accepted.ProfileIdentity);
        var scope = R025SqlImportOrchestrator.ValidateScope(accepted.Scope.StoreCode, accepted.Scope.PeriodEnd,
            request.ExpectedStoreCode, request.ExpectedBusinessDate);
        var start = accepted.Scope.PeriodStart ?? scope.BusinessDate!.Value;
        var end = scope.BusinessDate!.Value;
        await SqlServerTransactionalImportStore.RequestRestatementApprovalAsync(connectionString, restatement,
            accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode, scope.StoreCode!, start, end,
            cancellationToken).ConfigureAwait(false);
        await RequireApprovedRestatementAsync(restatement, accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
            scope.StoreCode!, start, end, cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireApprovedRestatementAsync(ImportRestatementRequest restatement, string sourceSha256,
        string reportCode, string storeCode, DateOnly periodStart, DateOnly periodEnd, CancellationToken token)
    {
        await using var connection = LocalConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new SqlCommand("""
            SELECT TOP (1) 1
            FROM dbo.import_restatement_approvals r
            JOIN dbo.approval_requests a ON a.approval_request_id=r.approval_request_id
            JOIN dbo.import_files old ON old.import_file_id=r.previous_import_file_id
            WHERE a.status='APPROVED' AND r.applied_import_file_id IS NULL AND old.is_superseded=0
              AND r.previous_import_file_id=@previous AND r.replacement_sha256=@hash
              AND r.store_code=@store AND r.report_code=@report
              AND r.period_start=@start AND r.period_end=@end
              AND r.request_reason=@reason COLLATE Latin1_General_100_BIN2;
            """, connection);
        command.Parameters.AddWithValue("@previous", restatement.PreviousImportFileId);
        command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(sourceSha256));
        command.Parameters.AddWithValue("@store", storeCode);
        command.Parameters.AddWithValue("@report", reportCode);
        command.Parameters.AddWithValue("@start", periodStart);
        command.Parameters.AddWithValue("@end", periodEnd);
        command.Parameters.AddWithValue("@reason", restatement.Reason.Trim());
        if (await command.ExecuteScalarAsync(token).ConfigureAwait(false) is null)
            throw RestatementApprovalRequired();
    }
}
