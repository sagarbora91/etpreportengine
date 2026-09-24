using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerTransactionalImportStore
{
    // Request commits independently so a pending decision survives without writing import facts.
    private async Task RequireRestatementApprovalAsync(ImportPersistencePackage package, CancellationToken token)
    {
        var restatement = package.Restatement!;
        if (restatement.Reason.Trim().Length > 500)
            throw new ImportSourceException("RESTATEMENT_REASON_TOO_LONG", "Keep the restatement reason within 500 characters.");
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(token);
        await using var command = new SqlCommand("EXEC dbo.request_import_restatement @previous,@hash,@report,@store,@start,@end,@reason", connection);
        command.Parameters.AddWithValue("@previous", restatement.PreviousImportFileId);
        command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(package.File.SourceSha256));
        command.Parameters.AddWithValue("@report", PersistenceValidation.ResolveReportCode(package.File));
        command.Parameters.AddWithValue("@store", package.File.StoreCode ?? string.Empty);
        command.Parameters.AddWithValue("@start", package.File.PeriodStart ?? package.File.BusinessDate);
        command.Parameters.AddWithValue("@end", package.File.PeriodEnd ?? package.File.BusinessDate);
        command.Parameters.AddWithValue("@reason", restatement.Reason.Trim());
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("The restatement request could not be read.");
        var id = reader.GetInt64(0);
        var status = reader.GetString(1);
        if (status == "APPROVED") return;
        throw new ImportSourceException(status == "PENDING" ? "RESTATEMENT_APPROVAL_PENDING" : "RESTATEMENT_APPROVAL_REJECTED",
            status == "PENDING"
                ? $"Restatement request {id} is awaiting Owner approval. Current facts are unchanged. Retry this source with the same reason after approval."
                : $"Restatement request {id} was {status.ToLowerInvariant()}. Current facts are unchanged. Correct the source or provide a new reason before requesting again.");
    }
}
