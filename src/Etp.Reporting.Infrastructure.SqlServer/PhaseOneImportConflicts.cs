using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerTransactionalImportStore
{
    private const int DocumentRefLength = 200;

    // Read inside the import transaction, because the rollback that follows removes these rows (IF-017).
    // A sample keeps the business identity, date, report and the difference the procedure wrote.
    private static async Task<ImportConflictException> ConflictAsync(SqlConnection c, SqlTransaction t, long file, int count,
        CancellationToken token)
    {
        await using var q = Cmd(c, t, $"""
            SELECT TOP({ImportConflictException.MaximumSamples}) x.report_code,x.business_identity,x.business_date,x.safe_difference,l.source_row_number
            FROM dbo.import_conflicts x LEFT JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id
            WHERE x.import_file_id=@file ORDER BY x.import_conflict_id;
            """);
        q.Parameters.AddWithValue("@file", file);
        var samples = new List<ImportIssue>();
        await using (var reader = await q.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
            {
                var date = reader.IsDBNull(2) ? null : $" {reader.GetFieldValue<DateOnly>(2):yyyy-MM-dd}";
                var document = $"{(reader.IsDBNull(0) ? "" : reader.GetString(0) + " ")}{reader.GetString(1)}{date}";
                samples.Add(new(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict,
                    ImportDiagnosticCatalogue.SafeMessage(ImportCodes.ImportConflict, reader.GetString(3)),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    DocumentRef: document.Length > DocumentRefLength ? document[..DocumentRefLength] : document));
            }
        return new ImportConflictException(count, samples);
    }
}
