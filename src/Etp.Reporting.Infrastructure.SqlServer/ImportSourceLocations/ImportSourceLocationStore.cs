using System.Data;
using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;

/// <summary>Where a run left a source (0069 <c>import_source_locations.route</c>).</summary>
public enum ImportSourceRoute { Processed, Duplicate, Failed, Hand }

/// <summary>One file a run routed: the file inside the source, its own hash, and the path of the routed source.</summary>
public sealed record NewImportSourceLocation(string FileName, string? SourceSha256, string RoutedPath, ImportSourceRoute Route)
{
    /// <summary>The 12-character diagnostics Ref of the failure, when it wrote one.</summary>
    public string? AttemptRef { get; init; }
}

/// <summary>A saved location (newest first when several match a file).</summary>
public sealed record ImportSourceLocation(long Id, long? ImportFileId, string? AttemptRef, string FileName, string? SourceSha256,
    string RoutedPath, ImportSourceRoute Route, DateTime RoutedUtc);

/// <summary>
/// The 0069 table (wave plan C-c). The unattended run and a hand import write it; the Problems and History Fix buttons read
/// it. On a database older than 0069 a write does nothing and a read finds nothing (the table is looked up first, in a
/// nested IF), so the import itself never depends on it.
/// </summary>
public interface IImportSourceLocationStore
{
    Task RecordAsync(IReadOnlyList<NewImportSourceLocation> locations, CancellationToken cancellationToken = default);

    /// <summary>The newest location of the file: by hash when known, otherwise by file name. Null when none is saved.</summary>
    Task<ImportSourceLocation?> FindLatestAsync(string fileName, string? sourceSha256, CancellationToken cancellationToken = default);
}

public sealed partial class SqlServerImportSourceLocationStore(IEtpConnectionFactory connections) : IImportSourceLocationStore
{
    public const int PathLength = 400;
    public const int FileNameLength = 260;

    [GeneratedRegex("^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[0-9A-Fa-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex RefPattern();

    /// <summary>
    /// One INSERT per file, in one transaction. A hash that matches a committed import file (the newest with that hash) fills
    /// import_file_id. A path longer than the column, or an empty one, is not recorded (the import does not depend on it).
    /// </summary>
    public async Task RecordAsync(IReadOnlyList<NewImportSourceLocation> locations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locations);
        var rows = locations.Where(Recordable).ToArray();
        if (rows.Length == 0) return;
        const string sql = """
            IF OBJECT_ID(N'dbo.import_source_locations',N'U') IS NOT NULL
            BEGIN
              INSERT dbo.import_source_locations(import_file_id,attempt_ref,file_name,source_sha256,routed_path,route)
              SELECT (SELECT TOP(1) f.import_file_id FROM dbo.import_files f WHERE @sha IS NOT NULL AND f.source_sha256=@sha ORDER BY f.import_file_id DESC),
                     @ref,@name,@sha,@path,@route;
            END
            """;
        await using var connection = await connections.OpenAsync(ct: cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var row in rows)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.Add("@ref", SqlDbType.VarChar, 12).Value = RefPattern().IsMatch(row.AttemptRef ?? "") ? row.AttemptRef!.ToUpperInvariant() : (object)DBNull.Value;
            command.Parameters.Add("@name", SqlDbType.NVarChar, FileNameLength).Value = Path.GetFileName(row.FileName.Trim());
            command.Parameters.Add("@sha", SqlDbType.Char, 64).Value = NormalisedSha(row.SourceSha256) is { } sha ? sha : (object)DBNull.Value;
            command.Parameters.Add("@path", SqlDbType.NVarChar, PathLength).Value = row.RoutedPath.Trim();
            command.Parameters.Add("@route", SqlDbType.VarChar, 10).Value = row.Route.ToString();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImportSourceLocation?> FindLatestAsync(string fileName, string? sourceSha256, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName) && NormalisedSha(sourceSha256) is null) return null;
        // Nested IF (never "IF x AND (SELECT ...)"): an older database has no table, so the SELECT is not even compiled there.
        const string sql = """
            IF OBJECT_ID(N'dbo.import_source_locations',N'U') IS NOT NULL
            BEGIN
              IF @sha IS NOT NULL
                SELECT TOP(1) import_source_location_id,import_file_id,attempt_ref,file_name,source_sha256,routed_path,route,routed_utc
                FROM dbo.import_source_locations WHERE source_sha256=@sha
                ORDER BY routed_utc DESC,import_source_location_id DESC;
              ELSE
                SELECT TOP(1) import_source_location_id,import_file_id,attempt_ref,file_name,source_sha256,routed_path,route,routed_utc
                FROM dbo.import_source_locations WHERE file_name=@name
                ORDER BY routed_utc DESC,import_source_location_id DESC;
            END
            """;
        await using var connection = await connections.OpenAsync(ct: cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@sha", SqlDbType.Char, 64).Value = NormalisedSha(sourceSha256) is { } sha ? sha : (object)DBNull.Value;
        command.Parameters.Add("@name", SqlDbType.NVarChar, FileNameLength).Value = Path.GetFileName((fileName ?? "").Trim());
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5),
                Enum.Parse<ImportSourceRoute>(reader.GetString(6)), DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc));
        }
        catch (SqlException exception) when (exception.Number == 229)
        {
            // A Viewer is denied the table (0069): it holds local folder paths. A Viewer has no Fix action that needs one.
            return null;
        }
    }

    internal static bool Recordable(NewImportSourceLocation location) =>
        location is not null && !string.IsNullOrWhiteSpace(location.FileName) && !string.IsNullOrWhiteSpace(location.RoutedPath)
        && location.RoutedPath.Trim().Length <= PathLength && Path.GetFileName(location.FileName.Trim()).Length <= FileNameLength;

    internal static string? NormalisedSha(string? value) =>
        value is not null && Sha256Pattern().IsMatch(value.Trim()) ? value.Trim().ToUpperInvariant() : null;
}
