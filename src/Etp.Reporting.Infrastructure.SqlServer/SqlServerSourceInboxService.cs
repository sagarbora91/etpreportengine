using Etp.Reporting.Application.SourceInbox;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerSourceInboxService : ISourceInboxService
{
    private readonly Func<string?, int, CancellationToken, Task<IReadOnlyList<SourceDocumentRow>>> loadDocuments;
    private readonly Func<long, CancellationToken, Task<IReadOnlyList<DocumentExtractionRow>>> loadExtractions;
    private readonly Func<long, bool, string, CancellationToken, Task> reviewExtraction;
    private readonly Func<string, string?, DateOnly?, string?, CancellationToken, Task<DocumentIntakeOutcome>> intake;
    private readonly Func<string, string, CancellationToken, Task<bool>> verifyIntegrity;

    public SqlServerSourceInboxService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        var repository = new ProductisationRepository(validated);
        var operations = new ProductisationOperationsService(validated);

        loadDocuments = repository.LoadSourceInboxAsync;
        loadExtractions = repository.LoadDocumentExtractionsAsync;
        reviewExtraction = repository.ReviewDocumentExtractionAsync;
        intake = operations.IntakeDocumentAsync;
        verifyIntegrity = ManagedDocumentRepository.VerifyIntegrityAsync;
    }

    internal SqlServerSourceInboxService(
        Func<string?, int, CancellationToken, Task<IReadOnlyList<SourceDocumentRow>>> loadDocuments,
        Func<long, CancellationToken, Task<IReadOnlyList<DocumentExtractionRow>>> loadExtractions,
        Func<long, bool, string, CancellationToken, Task> reviewExtraction,
        Func<string, string?, DateOnly?, string?, CancellationToken, Task<DocumentIntakeOutcome>> intake,
        Func<string, string, CancellationToken, Task<bool>> verifyIntegrity)
    {
        this.loadDocuments = loadDocuments ?? throw new ArgumentNullException(nameof(loadDocuments));
        this.loadExtractions = loadExtractions ?? throw new ArgumentNullException(nameof(loadExtractions));
        this.reviewExtraction = reviewExtraction ?? throw new ArgumentNullException(nameof(reviewExtraction));
        this.intake = intake ?? throw new ArgumentNullException(nameof(intake));
        this.verifyIntegrity = verifyIntegrity ?? throw new ArgumentNullException(nameof(verifyIntegrity));
    }

    public async Task<IReadOnlyList<SourceInboxDocument>> LoadDocumentsAsync(
        string? lifecycleStatus = null,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        (await loadDocuments(lifecycleStatus, limit, cancellationToken).ConfigureAwait(false))
        .Select(Map)
        .ToArray();

    public async Task<IReadOnlyList<SourceDocumentExtraction>> LoadExtractionsAsync(
        long sourceDocumentId,
        CancellationToken cancellationToken = default) =>
        (await loadExtractions(sourceDocumentId, cancellationToken).ConfigureAwait(false))
        .Select(Map)
        .ToArray();

    public Task ReviewExtractionAsync(
        long extractionId,
        bool verified,
        string reason,
        CancellationToken cancellationToken = default) =>
        reviewExtraction(extractionId, verified, reason, cancellationToken);

    public async Task<SourceDocumentIntakeOutcome> IntakeAsync(
        SourceDocumentIntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await intake(
            request.SourcePath,
            request.StoreCode,
            request.BusinessDate,
            request.DocumentType,
            cancellationToken).ConfigureAwait(false);
        return new(
            Map(outcome.Document),
            outcome.Extraction is null ? null : Map(outcome.Extraction),
            outcome.Duplicate);
    }

    public Task<bool> VerifyIntegrityAsync(
        SourceInboxDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return verifyIntegrity(document.ManagedFilePath, document.Sha256, cancellationToken);
    }

    internal static SourceInboxDocument Map(SourceDocumentRow row) =>
        new(
            row.Id,
            row.OriginalFileName,
            row.ManagedFilePath,
            row.Sha256,
            row.SizeBytes,
            row.SourceType,
            row.DocumentType,
            row.StoreCode,
            row.BusinessDate,
            row.LifecycleStatus,
            row.ReportCode,
            row.ImportFileId,
            row.ReportGenerationId,
            row.ReceivedBy,
            row.ReceivedUtc,
            row.SafeMessage);

    internal static SourceDocumentExtraction Map(DocumentExtractionRow row) =>
        new(
            row.Id,
            row.SourceDocumentId,
            row.Method,
            row.Version,
            row.Text,
            row.Confidence,
            row.ReviewStatus,
            row.ReviewedBy,
            row.ReviewedUtc,
            row.ReviewReason,
            row.CreatedUtc);

    internal static SourceDocumentExtractionResult Map(DocumentExtractionResult result) =>
        new(
            result.Method,
            result.Version,
            result.Text,
            result.Confidence,
            result.PageNumber,
            result.BoundingBoxJson,
            result.StructuredFieldsJson,
            result.ReviewStatus);
}
