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
/// </summary>
public sealed record EarlierImportEvidenceResult(
    int FilesHashed,
    int Matched,
    int Retained,
    int AlreadyHeld,
    int Skipped,
    long BytesRetained);

/// <summary>Settings → Database: the evidence size and the Owner's "Keep source files for earlier imports…".</summary>
public interface IImportEvidenceService
{
    Task<ImportEvidenceSummary> LoadSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hashes every .xlsx and .csv file under <paramref name="folders"/>, and every .xlsx and .csv entry of
    /// their .zip archives, and stores the bytes of those whose SHA-256 matches an imported file. Owner only.
    /// </summary>
    Task<EarlierImportEvidenceResult> RetainEarlierImportsAsync(IReadOnlyList<string> folders,
        IProgress<int>? filesHashed = null, CancellationToken cancellationToken = default);
}
