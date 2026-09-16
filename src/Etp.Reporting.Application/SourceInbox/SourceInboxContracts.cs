namespace Etp.Reporting.Application.SourceInbox;

public sealed record SourceInboxDocument(
    long Id,
    string OriginalFileName,
    string ManagedFilePath,
    string Sha256,
    long SizeBytes,
    string SourceType,
    string? DocumentType,
    string? StoreCode,
    DateOnly? BusinessDate,
    string LifecycleStatus,
    string? ReportCode,
    long? ImportFileId,
    long? ReportGenerationId,
    string ReceivedBy,
    DateTime ReceivedUtc,
    string? SafeMessage);

public sealed record SourceDocumentIntakeRequest(
    string SourcePath,
    string? StoreCode,
    DateOnly? BusinessDate,
    string? DocumentType);

public sealed record SourceDocumentIntakeOutcome(
    SourceInboxDocument Document,
    bool Duplicate);

public interface ISourceInboxService
{
    Task<IReadOnlyList<SourceInboxDocument>> LoadDocumentsAsync(
        string? lifecycleStatus = null,
        int limit = 500,
        CancellationToken cancellationToken = default);

    Task<SourceDocumentIntakeOutcome> IntakeAsync(
        SourceDocumentIntakeRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyIntegrityAsync(
        SourceInboxDocument document,
        CancellationToken cancellationToken = default);
}
