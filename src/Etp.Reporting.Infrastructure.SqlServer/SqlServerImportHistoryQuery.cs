using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Profiles;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerImportHistoryQuery(string connectionString) : IImportHistoryQuery, IImportAttemptRecorder
{
    private static readonly HashSet<string> SafeColumns = ApprovedImportProfileRegistry.All
        .SelectMany(profile => profile.ExpectedSourceHeaders).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static ImportIssue SafeIssue(ImportIssue issue) => issue with
    {
        Message = "Review the indicated source column or row and correct the workbook before retrying.",
        SourceColumn = issue.SourceColumn is { } column && SafeColumns.Contains(column) ? column : null
    };

    public async Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default)
    {
        var access = await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(cancellationToken);
        if (!access.CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
        // Retain location/code/severity, never source values, paths, SQL, or exception text.
        var diagnostics = (result.Diagnostics ?? []).Select(SafeIssue).ToArray();
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            EXEC dbo.record_import_attempt @name=@name,@hash=@hash,@report=@report,@store=@store,
              @start=@start,@end=@end,@outcome=@outcome,@rows=@rows,@new=@new,@present=@present,
              @conflicts=@conflicts,@diagnostics=@diagnostics;
            """, connection);
        Add("@name", Path.GetFileName(result.FileName)); Add("@hash", result.SourceSha256);
        Add("@report", result.ReportCode); Add("@store", result.StoreCode);
        Add("@start", result.PeriodStart); Add("@end", result.PeriodEnd); Add("@outcome", result.Status);
        Add("@rows", result.RowsProcessed); Add("@new", result.NewRows); Add("@present", result.AlreadyPresentRows);
        Add("@conflicts", result.ConflictRows); Add("@diagnostics", JsonSerializer.Serialize(diagnostics));
        await command.ExecuteNonQueryAsync(cancellationToken);
        void Add(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public async Task<IReadOnlyList<ImportHistoryEntry>> LoadAsync(ImportHistoryScope scope, CancellationToken cancellationToken = default)
    {
        if (scope.From > scope.To) throw new ArgumentException("Start date must not follow end date.", nameof(scope));
        var access = await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(cancellationToken);
        if (!access.CanView) throw new UnauthorizedAccessException("Application access is required.");
        const string sql = """
            WITH canonical AS (
              SELECT f.import_file_id,f.original_file_name,f.report_code,f.store_code,
                COALESCE(f.period_start,f.business_date,b.period_start) period_start,
                COALESCE(f.period_end,f.business_date,b.period_end) period_end,
                COALESCE(b.completed_utc,b.started_utc) recorded_utc,b.status,
                COALESCE(b.source_row_count,0) rows_processed,
                COALESCE(o.new_rows,0) new_rows,COALESCE(o.present_rows,0) present_rows,COALESCE(o.conflicts,0) conflicts
              FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
              OUTER APPLY (
                SELECT SUM(CASE WHEN x.conflict_row=1 THEN 0 ELSE x.new_row END) new_rows,
                  SUM(CASE WHEN x.conflict_row=1 OR x.new_row=1 THEN 0 ELSE x.present_row END) present_rows,
                  SUM(x.conflict_row) conflicts
                FROM (
                  SELECT MAX(CASE WHEN r.outcome='NEW' THEN 1 ELSE 0 END) new_row,
                    MAX(CASE WHEN r.outcome='ALREADY_PRESENT' THEN 1 ELSE 0 END) present_row,
                    MAX(CASE WHEN r.outcome='CONFLICT' THEN 1 ELSE 0 END) conflict_row
                  FROM dbo.import_row_outcomes r LEFT JOIN dbo.source_lineage l ON l.source_lineage_id=r.source_lineage_id
                  WHERE r.import_file_id=f.import_file_id
                  GROUP BY l.sheet_name,l.source_row_number,CASE WHEN r.source_lineage_id IS NULL THEN r.business_identity END
                ) x
              ) o
            ), classified AS (
              SELECT f.*,
                CASE WHEN f.conflicts>0 THEN 'Failed' WHEN f.status='Completed' AND f.new_rows=0 AND f.present_rows>0 THEN 'Duplicate content'
                  WHEN f.status='Completed' AND f.rows_processed=0 THEN 'empty export'
                  WHEN f.status='Completed' THEN 'Imported' ELSE f.status END outcome
              FROM canonical f
            ), represented AS (
              SELECT f.*,a.import_attempt_id,COALESCE(a.diagnostics_json,N'[]') diagnostics_json
              FROM classified f OUTER APPLY (
                SELECT TOP(1) import_attempt_id,diagnostics_json FROM dbo.import_attempts a WHERE a.import_file_id=f.import_file_id
                  AND a.outcome=f.outcome AND a.rows_processed=f.rows_processed AND a.new_rows=f.new_rows
                  AND a.already_present_rows=f.present_rows AND a.conflict_rows=f.conflicts ORDER BY a.import_attempt_id
              ) a
            ), history AS (
              SELECT CONCAT('file:',f.import_file_id) entry_key,f.recorded_utc,f.import_file_id,
                f.original_file_name file_name,f.report_code,f.store_code,f.period_start,f.period_end,f.outcome,
                f.rows_processed,f.new_rows,f.present_rows,f.conflicts,f.diagnostics_json
              FROM represented f
              UNION ALL
              SELECT CONCAT('attempt:',a.import_attempt_id),a.recorded_utc,a.import_file_id,a.file_name,a.report_code,a.store_code,
                a.period_start,a.period_end,a.outcome,a.rows_processed,a.new_rows,a.already_present_rows,a.conflict_rows,a.diagnostics_json
              FROM dbo.import_attempts a
              WHERE NOT EXISTS(SELECT 1 FROM represented f WHERE f.import_attempt_id=a.import_attempt_id)
            )
            SELECT * FROM history
            WHERE COALESCE(period_start,CONVERT(date,recorded_utc))<=@to
              AND COALESCE(period_end,CONVERT(date,recorded_utc))>=@from
              AND (@store IS NULL OR store_code=@store OR store_code IS NULL)
            ORDER BY recorded_utc DESC,entry_key DESC;
            """;
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@from", scope.From); command.Parameters.AddWithValue("@to", scope.To);
        command.Parameters.AddWithValue("@store", string.IsNullOrWhiteSpace(scope.StoreCode) ? DBNull.Value : scope.StoreCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ImportHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(8);
            var diagnostics = (JsonSerializer.Deserialize<ImportIssue[]>(reader.GetString(13)) ?? []).Select(SafeIssue).ToArray();
            var conflicts = reader.GetInt32(12);
            var message = conflicts > 0 ? $"{conflicts:N0} source rows conflict with existing data. Review the source before retrying."
                : status == "Duplicate" ? "This source was already imported. No facts were added by this attempt."
                : status == "Failed" ? "Import failed. Review the selected diagnostics and correct the source before retrying."
                : "Persisted import outcome. Counts describe source rows; a source row can produce multiple database facts.";
            if (diagnostics.Length == 0 && (conflicts > 0 || status == "Failed"))
                diagnostics = [new(ImportIssueSeverity.Blocker, conflicts > 0 ? "ROW_CONFLICT" : "IMPORT_FAILED", message)];
            entries.Add(new(reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetInt64(2),
                new(Path.GetFileName(reader.GetString(3)), Text(4), Text(5), Date(6), Date(7), status,
                    reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), conflicts, message, diagnostics)));
        }
        return entries;
        string? Text(int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
        DateOnly? Date(int column) => reader.IsDBNull(column) ? null : DateOnly.FromDateTime(reader.GetDateTime(column));
    }
}
