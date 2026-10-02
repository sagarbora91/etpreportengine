using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Tally;

public sealed record TallyArtifact(long Id, long BatchId, string Kind, string RelativePath, string Sha256, long ByteLength, DateTime CreatedUtc);

public enum TallyEvidenceState { Ok, Changed, Missing }

public sealed record TallyEvidenceCheck(TallyArtifact Artifact, TallyEvidenceState State);

/// <summary>Writes and checks files below the evidence root without the database (plan tasks 4 and 21).
/// A file is written once: an existing path is never overwritten, an empty file is never created,
/// and no folder on the way may be a link or junction.</summary>
public static class TallyEvidenceFiles
{
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "SOURCE_SNAPSHOT", "EXPECTED_POSTINGS", "VALIDATION", "PAYLOAD_XML", "HTTP_REQUEST", "HTTP_RESPONSE",
        "READBACK_XML", "RECONCILIATION", "RECOVERY_PLAN", "APPROVAL", "MANIFEST"
    ];

    public static async Task<(string FullPath, string Sha256, long Length)> WriteOnceAsync(
        string evidenceRoot, string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty) throw new ArgumentException("Empty evidence files are never written.", nameof(content));
        var full = Resolve(evidenceRoot, relativePath);
        RejectLinks(evidenceRoot, full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        RejectLinks(evidenceRoot, full);
        var created = false;
        try
        {
            await using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                await stream.WriteAsync(content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            return (full, await HashAsync(full, cancellationToken), content.Length);
        }
        catch when (created)
        {
            File.Delete(full);
            throw;
        }
        catch (IOException exception) when (File.Exists(full))
        {
            throw new InvalidOperationException("This evidence file already exists. Evidence is written once and never replaced.", exception);
        }
    }

    public static async Task<TallyEvidenceState> CheckAsync(string evidenceRoot, string relativePath, string sha256, CancellationToken cancellationToken = default)
    {
        var full = Resolve(evidenceRoot, relativePath);
        if (!File.Exists(full)) return TallyEvidenceState.Missing;
        RejectLinks(evidenceRoot, full);
        return string.Equals(await HashAsync(full, cancellationToken), sha256, StringComparison.Ordinal) ? TallyEvidenceState.Ok : TallyEvidenceState.Changed;
    }

    /// <summary>Reads a registered file once, through the same path and link checks as writing, and returns its bytes
    /// only when those bytes hash to <paramref name="sha256"/>. The caller hashes and parses the same bytes, so a file
    /// swapped after the check is never read as evidence.</summary>
    public static async Task<byte[]?> ReadVerifiedAsync(string evidenceRoot, string relativePath, string sha256, CancellationToken cancellationToken = default)
    {
        var full = Resolve(evidenceRoot, relativePath);
        if (!File.Exists(full)) return null;
        RejectLinks(evidenceRoot, full);
        var content = await File.ReadAllBytesAsync(full, cancellationToken);
        return string.Equals(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), sha256, StringComparison.Ordinal) ? content : null;
    }

    /// <summary>Renames a file that is on disk but was never registered (a crash between writing and registering),
    /// so its name can be used again. Such a file is not evidence; it is kept beside the name for inspection.</summary>
    public static string? SetAsideUnregistered(string evidenceRoot, string relativePath)
    {
        var full = Resolve(evidenceRoot, relativePath);
        if (!File.Exists(full)) return null;
        RejectLinks(evidenceRoot, full);
        var aside = full + ".unregistered-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        File.Move(full, aside);
        return aside;
    }

    public static async Task<string> HashAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Resolve(string evidenceRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(evidenceRoot) || !Path.IsPathFullyQualified(evidenceRoot))
            throw new ArgumentException("The Tally evidence folder must be a full path.", nameof(evidenceRoot));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(evidenceRoot));
        var relative = TallyEvidencePaths.Validate(relativePath).Replace('\\', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Evidence files must stay inside the Tally evidence folder.", nameof(relativePath));
        return full;
    }

    private static void RejectLinks(string evidenceRoot, string fullPath)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(evidenceRoot));
        for (var current = fullPath; !string.IsNullOrEmpty(current) && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Tally evidence cannot be kept in a linked folder or file.");
    }
}

/// <summary>Writes an evidence file and registers it in <c>dbo.tally_artifacts</c> in the same call;
/// re-checks every registered file of a batch (OK / CHANGED / MISSING). Owner only, like the tables.</summary>
public sealed class TallyEvidenceStore(string connectionString, string? evidenceRootOverride = null)
{
    public async Task<TallyArtifact> WriteAsync(long batchId, string kind, string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (!TallyEvidenceFiles.Kinds.Contains(kind, StringComparer.Ordinal)) throw new ArgumentException("Unknown evidence kind.", nameof(kind));
        var relative = TallyEvidencePaths.Validate(relativePath);
        if (!relative.Contains($"\\batch-{batchId}\\", StringComparison.Ordinal))
            throw new ArgumentException("An evidence file must be written in its own batch folder.", nameof(relativePath));
        await RequireOwnerAsync(cancellationToken);
        var root = await LoadRootAsync(cancellationToken);
        if (await FindAsync(batchId, relative, cancellationToken) is not null)
            throw new InvalidOperationException("This evidence file already exists. Evidence is written once and never replaced.");
        // On disk but not registered: left by a crash before registration, so it is not evidence and must not block the name.
        TallyEvidenceFiles.SetAsideUnregistered(root, relative);
        var (full, sha256, length) = await TallyEvidenceFiles.WriteOnceAsync(root, relative, content, cancellationToken);
        try
        {
            const string sql = """
                INSERT dbo.tally_artifacts(accounting_batch_id,artifact_kind,relative_path,sha256,byte_length)
                OUTPUT inserted.tally_artifact_id,inserted.created_utc
                VALUES(@batch,@kind,@path,@sha,@length);
                """;
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@batch", batchId);
            command.Parameters.AddWithValue("@kind", kind);
            command.Parameters.AddWithValue("@path", relative);
            command.Parameters.AddWithValue("@sha", sha256);
            command.Parameters.AddWithValue("@length", length);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new(reader.GetInt64(0), batchId, kind, relative, sha256, length, reader.GetDateTime(1));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The INSERT may have committed although its answer was lost (a dropped connection). Delete the file only when
            // the row is certainly absent; a file kept without a row is set aside by the next write of the same name.
            TallyArtifact? registered = null;
            var known = false;
            try
            {
                registered = await FindAsync(batchId, relative, CancellationToken.None);
                known = true;
            }
            catch (Exception lookupFailure) when (lookupFailure is SqlException or InvalidOperationException)
            {
                // Still unknown: keep the file.
            }
            if (registered is not null && string.Equals(registered.Sha256, sha256, StringComparison.Ordinal)) return registered;
            if (known && registered is null) File.Delete(full);
            throw;
        }
    }

    private async Task<TallyArtifact?> FindAsync(long batchId, string relative, CancellationToken token)
    {
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT tally_artifact_id,artifact_kind,sha256,byte_length,created_utc FROM dbo.tally_artifacts WHERE accounting_batch_id=@batch AND relative_path=@path", connection);
        command.Parameters.AddWithValue("@batch", batchId);
        command.Parameters.AddWithValue("@path", relative);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token)
            ? new(reader.GetInt64(0), batchId, reader.GetString(1), relative, reader.GetString(2), reader.GetInt64(3), reader.GetDateTime(4))
            : null;
    }

    /// <summary>The registered file's bytes, read once and checked against its recorded SHA-256; null when it is changed or missing.</summary>
    public async Task<byte[]?> ReadVerifiedAsync(TallyArtifact artifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        await RequireOwnerAsync(cancellationToken);
        return await TallyEvidenceFiles.ReadVerifiedAsync(await LoadRootAsync(cancellationToken), artifact.RelativePath, artifact.Sha256, cancellationToken);
    }

    public async Task<IReadOnlyList<TallyEvidenceCheck>> VerifyAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken);
        var root = await LoadRootAsync(cancellationToken);
        var artifacts = new List<TallyArtifact>();
        await using (var connection = await OpenAsync(cancellationToken))
        await using (var command = new SqlCommand("SELECT tally_artifact_id,artifact_kind,relative_path,sha256,byte_length,created_utc FROM dbo.tally_artifacts WHERE accounting_batch_id=@batch ORDER BY tally_artifact_id", connection))
        {
            command.Parameters.AddWithValue("@batch", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                artifacts.Add(new(reader.GetInt64(0), batchId, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.GetDateTime(5)));
        }
        var result = new List<TallyEvidenceCheck>();
        foreach (var artifact in artifacts)
            result.Add(new(artifact, await TallyEvidenceFiles.CheckAsync(root, artifact.RelativePath, artifact.Sha256, cancellationToken)));
        return result;
    }

    private async Task<string> LoadRootAsync(CancellationToken token)
    {
        if (evidenceRootOverride is not null) return evidenceRootOverride;
        await using var connection = await OpenAsync(token);
        await using var command = new SqlCommand("SELECT TOP(1) tally_evidence_root FROM dbo.product_settings ORDER BY product_setting_id", connection);
        return Convert.ToString(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The Tally evidence folder is not configured.");
    }

    private async Task RequireOwnerAsync(CancellationToken token)
    {
        if (!(await new Phase2OperationsRepository(connectionString).LoadCurrentAccessAsync(token)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken token)
    {
        var connection = new SqlConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString)));
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
