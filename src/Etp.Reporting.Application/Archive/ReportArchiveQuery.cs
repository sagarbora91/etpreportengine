namespace Etp.Reporting.Application.Archive;

public sealed record ReportArchiveSearch(
    string? StoreCode = null,
    DateOnly? BusinessDate = null,
    int Limit = 200);

public sealed record ArchivedReportGenerationSummary(
    long Id,
    string StoreCode,
    DateOnly BusinessDate,
    int GenerationNumber,
    string ControlSha256,
    string? DocumentSha256,
    DateTime GeneratedUtc,
    string GeneratedBy,
    bool IsFinal,
    long? SupersedesGenerationId,
    bool CanReExport);

public sealed record ArchivedReportComparisonSection(
    string Table,
    int FirstRows,
    int SecondRows,
    string FirstStatus,
    string SecondStatus,
    bool Changed);

/// <summary>
/// Reads immutable report generations. The document type is supplied by the
/// outer reporting adapter so this application contract remains renderer-neutral.
/// </summary>
public interface IReportArchiveQuery<TDocument> where TDocument : notnull
{
    Task<IReadOnlyList<ArchivedReportGenerationSummary>> SearchAsync(
        ReportArchiveSearch search,
        CancellationToken cancellationToken = default);

    Task<TDocument> OpenAsync(
        long generationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchivedReportComparisonSection>> CompareAsync(
        long firstGenerationId,
        long secondGenerationId,
        CancellationToken cancellationToken = default);
}
