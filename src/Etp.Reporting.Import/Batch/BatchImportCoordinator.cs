namespace Etp.Reporting.Import.Batch;

public enum BatchImportFileStatus { Succeeded, Failed, Cancelled }

public sealed record BatchImportProgress(int Completed, int Total, string Stage, string SafeFileName);

public sealed record BatchImportFileResult(
    string SafeFileName,
    BatchImportFileStatus Status,
    int Attempts,
    string? ErrorCode = null,
    string? SafeErrorMessage = null,
    int RowsProcessed = 0,
    int NewRows = 0,
    int AlreadyPresentRows = 0,
    int ConflictRows = 0,
    bool ExactDuplicate = false);

public sealed record BatchImportSummary(IReadOnlyList<BatchImportFileResult> Files)
{
    public int Succeeded => Files.Count(x => x.Status == BatchImportFileStatus.Succeeded);
    public int Failed => Files.Count(x => x.Status == BatchImportFileStatus.Failed);
    public int Cancelled => Files.Count(x => x.Status == BatchImportFileStatus.Cancelled);
    public int RowsProcessed => Files.Sum(x => x.RowsProcessed);
    public int NewRows => Files.Sum(x => x.NewRows);
    public int AlreadyPresentRows => Files.Sum(x => x.AlreadyPresentRows);
    public int Conflicts => Files.Sum(x => x.ConflictRows);
    public int ExactDuplicates => Files.Count(x => x.ExactDuplicate);
    public bool CanRetry => Failed > 0;
}

public sealed record WorkbookImportOutcome(int RowsProcessed, int NewRows, int AlreadyPresentRows, int ConflictRows, bool ExactDuplicate = false)
{
    public static WorkbookImportOutcome Imported { get; } = new(0, 0, 0, 0);
}

public interface IWorkbookImportProcessor
{
    Task ProcessAsync(string workbookPath, CancellationToken cancellationToken);
}

public interface IWorkbookImportOutcomeProcessor : IWorkbookImportProcessor
{
    Task<WorkbookImportOutcome> ProcessWithOutcomeAsync(string workbookPath, CancellationToken cancellationToken);
}

public interface IImportFailureClassifier
{
    bool IsTransient(Exception exception);
    (string Code, string SafeMessage) Describe(Exception exception);
}

public sealed class SafeImportFailureClassifier : IImportFailureClassifier
{
    public bool IsTransient(Exception exception) => exception is IOException or TimeoutException;

    public (string Code, string SafeMessage) Describe(Exception exception) => exception switch
    {
        ImportSourceException source => (source.Code, source.Message),
        UnauthorizedAccessException => ("IMPORT_ACCESS_DENIED", "The workbook could not be accessed."),
        IOException => ("IMPORT_IO_FAILURE", "The workbook could not be read. Close other applications and retry."),
        TimeoutException => ("IMPORT_TIMEOUT", "The import timed out and can be retried."),
        _ => ("IMPORT_PROCESSING_FAILED", "The workbook could not be imported. Review the support package for diagnostics.")
    };
}

public sealed class BatchImportCoordinator
{
    private readonly IWorkbookImportProcessor _processor;
    private readonly IImportFailureClassifier _classifier;
    private readonly int _maximumAttempts;

    public BatchImportCoordinator(IWorkbookImportProcessor processor, IImportFailureClassifier? classifier = null, int maximumAttempts = 2)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _classifier = classifier ?? new SafeImportFailureClassifier();
        if (maximumAttempts is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        _maximumAttempts = maximumAttempts;
    }

    public async Task<BatchImportSummary> RunAsync(
        IReadOnlyList<string> workbookPaths,
        IProgress<BatchImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbookPaths);
        var results = new List<BatchImportFileResult>(workbookPaths.Count);
        for (var index = 0; index < workbookPaths.Count; index++)
        {
            var safeName = Path.GetFileName(workbookPaths[index]);
            if (cancellationToken.IsCancellationRequested)
            {
                results.AddRange(workbookPaths.Skip(index).Select(x =>
                    new BatchImportFileResult(Path.GetFileName(x), BatchImportFileStatus.Cancelled, 0)));
                break;
            }

            var attempts = 0;
            progress?.Report(new(index, workbookPaths.Count, "Importing", safeName));
            while (true)
            {
                attempts++;
                try
                {
                    var outcome = _processor is IWorkbookImportOutcomeProcessor detailed
                        ? await detailed.ProcessWithOutcomeAsync(workbookPaths[index], cancellationToken).ConfigureAwait(false)
                        : await ProcessWithoutOutcomeAsync(_processor, workbookPaths[index], cancellationToken).ConfigureAwait(false);
                    results.Add(new(safeName, BatchImportFileStatus.Succeeded, attempts, RowsProcessed: outcome.RowsProcessed,
                        NewRows: outcome.NewRows, AlreadyPresentRows: outcome.AlreadyPresentRows, ConflictRows: outcome.ConflictRows,
                        ExactDuplicate: outcome.ExactDuplicate));
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    results.Add(new(safeName, BatchImportFileStatus.Cancelled, attempts));
                    results.AddRange(workbookPaths.Skip(index + 1).Select(x =>
                        new BatchImportFileResult(Path.GetFileName(x), BatchImportFileStatus.Cancelled, 0)));
                    index = workbookPaths.Count;
                    break;
                }
                catch (Exception ex)
                {
                    if (_classifier.IsTransient(ex) && attempts < _maximumAttempts)
                    {
                        progress?.Report(new(index, workbookPaths.Count, "Retrying", safeName));
                        continue;
                    }
                    var failure = _classifier.Describe(ex);
                    results.Add(new(safeName, BatchImportFileStatus.Failed, attempts, failure.Code, failure.SafeMessage));
                    break;
                }
            }
        }
        progress?.Report(new(results.Count, workbookPaths.Count, "Completed", string.Empty));
        return new BatchImportSummary(results);
    }

    private static async Task<WorkbookImportOutcome> ProcessWithoutOutcomeAsync(IWorkbookImportProcessor processor, string path, CancellationToken token)
    {
        await processor.ProcessAsync(path, token).ConfigureAwait(false);
        return WorkbookImportOutcome.Imported;
    }
}
