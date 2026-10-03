namespace Etp.Reporting.Application.Imports;

/// <summary>
/// Keeps the bytes of a source file whose rows are already stored, e.g. for a <c>Duplicate</c> result whose
/// earlier import kept no bytes (IF-023, spec 11.2). It runs in a small transaction of its own; a new import
/// stores its bytes inside its own import transaction instead.
/// </summary>
public interface IImportEvidenceRetainer
{
    /// <returns><see cref="EvidenceState.Retained"/> when this call stored the bytes, <see cref="EvidenceState.AlreadyHeld"/>
    /// when the database held them already, and <see cref="EvidenceState.NotAttempted"/> when no import of
    /// <paramref name="sourceSha256"/> exists or there are no bytes. A failure throws.</returns>
    Task<EvidenceState> RetainImportedSourceAsync(string sourceSha256, ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}

/// <summary>What Settings → Database shows about the source files kept as evidence.</summary>
public sealed record ImportEvidenceSummary(
    int FilesHeld,
    long BytesHeld,
    int ImportedSources,
    int ImportedSourcesWithoutFile,
    long DatabaseDataBytes);

/// <summary>
/// The result of "Keep source files for earlier imports…": every file hashed, how many matched an import
/// that had no bytes, and how many of those were stored. Nothing is ever imported.
/// <paramref name="Skipped"/> counts files and archives that could not be read; <paramref name="Busy"/> matched
/// files an import in progress held, and <paramref name="DatabaseFailures"/> matched files the database refused
/// for another reason; <paramref name="FoldersTooDeep"/> folders below the depth limit that were not walked.
/// A walk that stopped early (<paramref name="Stop"/>) keeps the files it stored and reports its counts so far.
/// </summary>
public sealed record EarlierImportEvidenceResult(
    int FilesHashed,
    int Matched,
    int Retained,
    int AlreadyHeld,
    int Skipped,
    long BytesRetained,
    int Busy = 0,
    int DatabaseFailures = 0,
    int FoldersTooDeep = 0,
    EarlierImportStop Stop = EarlierImportStop.None);

/// <summary>Why "Keep source files for earlier imports…" ended before it had walked every folder.</summary>
public enum EarlierImportStop
{
    None,
    /// <summary>The Owner pressed Stop.</summary>
    Cancelled,
    /// <summary>The database connection broke and could not be opened again.</summary>
    ConnectionLost
}

/// <summary>
/// The evidence could not be read because an import in progress holds the rows (a short lock timeout).
/// Nothing changed; it can be tried again when the import finishes.
/// </summary>
public sealed class ImportEvidenceBusyException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

/// <summary>Settings → Database: the evidence size and the Owner's "Keep source files for earlier imports…".</summary>
public interface IImportEvidenceService
{
    /// <summary>The evidence held. Throws <see cref="ImportEvidenceBusyException"/> when an import holds the rows.</summary>
    Task<ImportEvidenceSummary> LoadSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hashes every .xlsx and .csv file under <paramref name="folders"/>, and every .xlsx and .csv entry of
    /// their .zip archives, and stores the bytes of those whose SHA-256 matches an imported file. Owner only.
    /// A cancel returns the counts so far. Throws <see cref="ImportEvidenceBusyException"/> before anything is
    /// stored when an import holds the list of imported files.
    /// </summary>
    Task<EarlierImportEvidenceResult> RetainEarlierImportsAsync(IReadOnlyList<string> folders,
        IProgress<int>? filesHashed = null, CancellationToken cancellationToken = default);
}
