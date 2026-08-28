using Etp.Reporting.Application.Archive;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// SQL Server adapter for immutable generation search, integrity-checked open,
/// and deterministic generation comparison.
/// </summary>
public sealed class SqlServerReportArchiveQuery : IReportArchiveQuery<ReportPackDocument>
{
    private readonly Phase2OperationsRepository repository;

    public SqlServerReportArchiveQuery(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        repository = new Phase2OperationsRepository(connectionString);
    }

    public async Task<IReadOnlyList<ArchivedReportGenerationSummary>> SearchAsync(
        ReportArchiveSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var rows = await repository.LoadReportGenerationsAsync(
            search.StoreCode,
            search.BusinessDate,
            search.Limit,
            cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public Task<ReportPackDocument> OpenAsync(
        long generationId,
        CancellationToken cancellationToken = default) =>
        repository.LoadArchivedReportAsync(generationId, cancellationToken);

    public async Task<IReadOnlyList<ArchivedReportComparisonSection>> CompareAsync(
        long firstGenerationId,
        long secondGenerationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await repository.CompareReportGenerationsAsync(
            firstGenerationId,
            secondGenerationId,
            cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public static ArchivedReportGenerationSummary Map(ArchivedReportGeneration row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new(
            row.Id,
            row.StoreCode,
            row.BusinessDate,
            row.GenerationNumber,
            row.ControlSha256,
            row.DocumentSha256,
            row.GeneratedUtc,
            row.GeneratedBy,
            row.IsFinal,
            row.SupersedesGenerationId,
            row.CanReExport);
    }

    public static ArchivedReportComparisonSection Map(ReportGenerationComparisonRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new(
            row.Table,
            row.FirstRows,
            row.SecondRows,
            row.FirstStatus,
            row.SecondStatus,
            row.Changed);
    }
}
