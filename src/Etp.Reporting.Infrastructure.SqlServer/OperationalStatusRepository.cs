using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record OperationalSummary(
    int ImportedFiles,
    int CompletedBatches,
    long SourceRows,
    DateTime? LatestImportUtc,
    IReadOnlyList<ImportHistoryRow> RecentImports);

public sealed record ImportHistoryRow(
    string FileName,
    string ReportCode,
    string Status,
    int SourceRows,
    DateTime StartedUtc,
    DateTime? CompletedUtc);

public sealed class OperationalStatusRepository(string connectionString)
{
    private const string SummarySql = """
        SELECT COUNT_BIG(*), (SELECT COUNT_BIG(*) FROM dbo.source_lineage),
               MAX(b.completed_utc), COUNT_BIG(DISTINCT CASE WHEN b.status='Completed' THEN b.import_batch_id END)
        FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id;
        """;

    private const string HistorySql = """
        SELECT TOP (50) f.original_file_name,COALESCE(p.report_code,'UNKNOWN'),b.status,
               COALESCE(f.source_row_count,0),b.started_utc,b.completed_utc
        FROM dbo.import_files f
        JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
        LEFT JOIN dbo.import_profiles p ON p.import_profile_id=f.import_profile_id
        ORDER BY b.started_utc DESC,f.import_file_id DESC;
        """;

    public async Task<OperationalSummary> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A SQL Server connection string is required.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        int files;
        int batches;
        long rows;
        DateTime? latest;
        await using (var command = new SqlCommand(SummarySql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            files = checked((int)reader.GetInt64(0));
            rows = reader.GetInt64(1);
            latest = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            batches = checked((int)reader.GetInt64(3));
        }

        var history = new List<ImportHistoryRow>();
        await using (var command = new SqlCommand(HistorySql, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                history.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.GetDateTime(4), reader.IsDBNull(5) ? null : reader.GetDateTime(5)));

        return new(files, batches, rows, latest, history);
    }
}
