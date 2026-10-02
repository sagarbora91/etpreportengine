using System.Data;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Source files kept inside the database as import evidence (IF-023, Owner decision OD-2; spec 5.1 B and 11.2).
/// <c>dbo.retain_import_source</c> stores the bytes once per SHA-256 and only for an imported file with that hash.
/// </summary>
internal static class ImportSourceEvidence
{
    /// <summary>Stores the bytes inside the caller's transaction: the import's own, or a small one.</summary>
    internal static async Task<EvidenceState> RetainAsync(SqlConnection connection, SqlTransaction transaction,
        string sourceSha256, ReadOnlyMemory<byte> content, long importFileId, CancellationToken token)
    {
        await using var command = new SqlCommand("EXEC dbo.retain_import_source @hash,@size,@content,@file,@state OUTPUT",
            connection, transaction) { CommandTimeout = 0 };
        command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = SqlServerImportFileRepository.NormalizeHash(sourceSha256);
        command.Parameters.Add("@size", SqlDbType.BigInt).Value = (long)content.Length;
        command.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = WholeArray(content);
        command.Parameters.Add("@file", SqlDbType.BigInt).Value = importFileId;
        var state = command.Parameters.Add("@state", SqlDbType.VarChar, 16);
        state.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        return ImportDatabaseCodes.ParseDatabaseCode<EvidenceState>((string)state.Value);
    }

    /// <summary>
    /// Keeps the bytes of a file that is already imported, e.g. a <c>Duplicate</c> whose earlier import kept none,
    /// in a small transaction of its own. The first import of those bytes becomes their file.
    /// </summary>
    internal static async Task<EvidenceState> RetainImportedSourceAsync(string connectionString, string sourceSha256,
        ReadOnlyMemory<byte> content, CancellationToken token)
    {
        var hash = SqlServerImportFileRepository.NormalizeHash(sourceSha256);
        if (content.IsEmpty) return EvidenceState.NotAttempted;
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(token).ConfigureAwait(false);
        long? file;
        await using (var query = new SqlCommand("""
            SELECT (SELECT MIN(import_file_id) FROM dbo.import_files WHERE source_sha256=@hash),
                   CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.v_import_source_evidence WHERE source_sha256=@hash) THEN 1 ELSE 0 END);
            """, connection))
        {
            query.Parameters.Add("@hash", SqlDbType.Char, 64).Value = hash;
            await using var reader = await query.ExecuteReaderAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            if (reader.GetBoolean(1)) return EvidenceState.AlreadyHeld;
            file = reader.IsDBNull(0) ? null : reader.GetInt64(0);
        }
        if (file is null) return EvidenceState.NotAttempted;
        return await RetainInOwnTransactionAsync(connection, hash, content, file.Value, token).ConfigureAwait(false);
    }

    internal static async Task<EvidenceState> RetainInOwnTransactionAsync(SqlConnection connection, string sourceSha256,
        ReadOnlyMemory<byte> content, long importFileId, CancellationToken token)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        try
        {
            var state = await RetainAsync(connection, transaction, sourceSha256, content, importFileId, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
            return state;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); throw; }
    }

    /// <summary>
    /// What a committed import left: <see cref="EvidenceState.Retained"/> when its own file brought the bytes,
    /// <see cref="EvidenceState.AlreadyHeld"/> when an earlier import had, otherwise not retained or not attempted.
    /// </summary>
    internal static async Task<EvidenceState> StateAfterImportAsync(string connectionString, string sourceSha256,
        string reportCode, string storeCode, DateOnly periodStart, DateOnly periodEnd, bool hadBytes, CancellationToken token)
    {
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var query = new SqlCommand("""
            SELECT TOP(1) f.import_file_id,e.first_import_file_id,CONVERT(bit,CASE WHEN e.source_sha256 IS NULL THEN 0 ELSE 1 END)
            FROM dbo.import_files f LEFT JOIN dbo.v_import_source_evidence e ON e.source_sha256=f.source_sha256
            WHERE f.source_sha256=@hash AND f.data_truth_version=1 AND f.report_code=@report AND f.store_code=@store
              AND f.period_start=@start AND f.period_end=@end
            ORDER BY f.import_file_id DESC;
            """, connection);
        query.Parameters.Add("@hash", SqlDbType.Char, 64).Value = SqlServerImportFileRepository.NormalizeHash(sourceSha256);
        query.Parameters.AddWithValue("@report", reportCode.Trim().ToUpperInvariant());
        query.Parameters.AddWithValue("@store", storeCode.Trim().ToUpperInvariant());
        query.Parameters.AddWithValue("@start", periodStart);
        query.Parameters.AddWithValue("@end", periodEnd);
        await using var reader = await query.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || !reader.GetBoolean(2))
            return hadBytes ? EvidenceState.NotRetained : EvidenceState.NotAttempted;
        return !reader.IsDBNull(1) && reader.GetInt64(1) == reader.GetInt64(0) ? EvidenceState.Retained : EvidenceState.AlreadyHeld;
    }

    // The reader's snapshot is an exact-size array, so it is sent without a copy.
    private static byte[] WholeArray(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is { } array &&
        segment.Offset == 0 && segment.Count == array.Length ? array : content.ToArray();
}

/// <summary>
/// Settings → Database: the size of the evidence held, and the Owner's "Keep source files for earlier imports…",
/// which stores the bytes of files imported before evidence was kept. It never imports anything.
/// </summary>
public sealed class SqlServerImportEvidenceService : IImportEvidenceService
{
    private readonly string connectionString;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;
    private readonly ImportPathPolicyOptions limits;

    public SqlServerImportEvidenceService(string connectionString) : this(connectionString, null)
    {
    }

    internal SqlServerImportEvidenceService(string connectionString,
        Func<CancellationToken, Task<ApplicationAccess>>? loadAccess, ImportPathPolicyOptions? limits = null)
    {
        this.connectionString = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        this.loadAccess = loadAccess ?? new Phase2OperationsRepository(this.connectionString).LoadCurrentAccessAsync;
        this.limits = limits ?? ImportPathPolicyOptions.Default;
    }

    public async Task<ImportEvidenceSummary> LoadSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("Application access is required.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var query = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.v_import_source_evidence),
                   (SELECT COALESCE(SUM(size_bytes),0) FROM dbo.v_import_source_evidence),
                   (SELECT COUNT(DISTINCT source_sha256) FROM dbo.import_files),
                   (SELECT COUNT(DISTINCT f.source_sha256) FROM dbo.import_files f
                     WHERE NOT EXISTS(SELECT 1 FROM dbo.v_import_source_evidence e WHERE e.source_sha256=f.source_sha256)),
                   (SELECT COALESCE(SUM(CONVERT(bigint,size)),0)*8192 FROM sys.database_files WHERE type=0);
            """, connection);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new(reader.GetInt32(0), Convert.ToInt64(reader.GetValue(1)), reader.GetInt32(2), reader.GetInt32(3),
            Convert.ToInt64(reader.GetValue(4)));
    }

    public async Task<EarlierImportEvidenceResult> RetainEarlierImportsAsync(IReadOnlyList<string> folders,
        IProgress<int>? filesHashed = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var roots = folders.Where(folder => !string.IsNullOrWhiteSpace(folder)).Select(folder => Path.GetFullPath(folder.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0) throw new ArgumentException("Choose at least one folder.", nameof(folders));
        if (roots.Any(root => !Directory.Exists(root))) throw new ArgumentException("A chosen folder no longer exists.", nameof(folders));
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var imported = await LoadImportedSourcesAsync(connection, cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int hashed = 0, matched = 0, retained = 0, alreadyHeld = 0, skipped = 0;
        long bytesRetained = 0;
        await foreach (var source in EarlierImportSourceScanner.ScanAsync(roots, limits, cancellationToken).ConfigureAwait(false))
        {
            if (source.Bytes is not { } bytes) { skipped++; continue; }
            hashed++;
            filesHashed?.Report(hashed);
            if (!imported.TryGetValue(source.Sha256!, out var import) || !seen.Add(source.Sha256!)) continue;
            matched++;
            if (import.Held) { alreadyHeld++; continue; }
            // One small transaction per file, so a stop or a power-off keeps the files already stored.
            EvidenceState state;
            try
            {
                state = await ImportSourceEvidence.RetainInOwnTransactionAsync(connection, source.Sha256!, bytes, import.FileId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException)
            {
                // One database error (a race with a live import, a lock timeout, a full data file) skips this file;
                // the walk keeps its counts and the files already stored.
                skipped++;
                continue;
            }
            if (state == EvidenceState.Retained) { retained++; bytesRetained += bytes.Length; }
            else alreadyHeld++;
        }
        return new(hashed, matched, retained, alreadyHeld, skipped, bytesRetained);
    }

    private static async Task<Dictionary<string, (long FileId, bool Held)>> LoadImportedSourcesAsync(SqlConnection connection,
        CancellationToken token)
    {
        await using var query = new SqlCommand("""
            SELECT LOWER(f.source_sha256),MIN(f.import_file_id),
                   CONVERT(bit,MAX(CASE WHEN e.source_sha256 IS NULL THEN 0 ELSE 1 END))
            FROM dbo.import_files f LEFT JOIN dbo.v_import_source_evidence e ON e.source_sha256=f.source_sha256
            GROUP BY LOWER(f.source_sha256);
            """, connection);
        await using var reader = await query.ExecuteReaderAsync(token).ConfigureAwait(false);
        var result = new Dictionary<string, (long, bool)>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result[reader.GetString(0)] = (reader.GetInt64(1), reader.GetBoolean(2));
        return result;
    }
}

/// <summary>
/// The files under the Owner's chosen folders, read and hashed one at a time: every .xlsx and .csv file, and
/// every .xlsx and .csv entry of a .zip archive, within the import size limits. Linked folders and files are
/// not followed. A file that cannot be read is returned without bytes and counted as skipped.
/// </summary>
internal static class EarlierImportSourceScanner
{
    internal readonly record struct ScannedSource(byte[]? Bytes, string? Sha256);

    private static readonly string[] SourceExtensions = [".xlsx", ".csv"];

    internal static async IAsyncEnumerable<ScannedSource> ScanAsync(IReadOnlyList<string> roots, ImportPathPolicyOptions limits,
        [EnumeratorCancellation] CancellationToken token)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<(string Path, int Depth)>(roots.Reverse().Select(root => (root, 0)));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (folder, depth) = pending.Pop();
            if (!visited.Add(Path.GetFullPath(folder))) continue;
            var (files, folders) = List(folder);
            if (files is null) { yield return default; continue; }
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                if (name.StartsWith("~$", StringComparison.Ordinal) || IsLink(file)) continue;
                if (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await foreach (var entry in ArchiveAsync(file, limits, token).ConfigureAwait(false)) yield return entry;
                }
                else if (SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return await ReadFileAsync(file, limits, token).ConfigureAwait(false);
            }
            if (depth >= limits.MaximumFolderDepth) continue;
            foreach (var child in folders!.Where(child => !IsLink(child)).OrderDescending(StringComparer.OrdinalIgnoreCase))
                pending.Push((child, depth + 1));
        }
    }

    private static (string[]? Files, string[]? Folders) List(string folder)
    {
        try
        {
            return (Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                Directory.EnumerateDirectories(folder).ToArray());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    private static async Task<ScannedSource> ReadFileAsync(string path, ImportPathPolicyOptions limits, CancellationToken token)
    {
        try
        {
            // Excel may hold an export open; read through a shared handle, as the workbook reader does.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > limits.MaximumEntryBytes) return default;
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            return Hashed(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return default; }
    }

    private static async IAsyncEnumerable<ScannedSource> ArchiveAsync(string path, ImportPathPolicyOptions limits,
        [EnumeratorCancellation] CancellationToken token)
    {
        var archive = OpenArchive(path, limits);
        if (archive is null) { yield return default; yield break; }
        using (archive)
        {
            foreach (var entry in archive.Entries.OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name) || entry.Name.StartsWith("~$", StringComparison.Ordinal) ||
                    !SourceExtensions.Contains(Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase)) continue;
                yield return await ReadEntryAsync(entry, token).ConfigureAwait(false);
            }
        }
    }

    // An unreadable or unsafe archive (too many entries, too large, a link, a zip bomb) is skipped whole.
    private static ZipArchive? OpenArchive(string path, ImportPathPolicyOptions limits)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920);
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            try { new ImportPathPolicy(limits).ValidateArchive(archive); }
            catch { archive.Dispose(); throw; }
            return archive;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or ImportSourceException)
        {
            stream?.Dispose();
            return null;
        }
    }

    private static async Task<ScannedSource> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken token)
    {
        if (entry.Length <= 0) return default;
        try
        {
            var bytes = new byte[entry.Length];
            await using var input = entry.Open();
            await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            return Hashed(bytes);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException) { return default; }
    }

    private static ScannedSource Hashed(byte[] bytes) => new(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return true; }
    }
}
