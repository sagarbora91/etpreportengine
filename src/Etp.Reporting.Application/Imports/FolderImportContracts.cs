namespace Etp.Reporting.Application.Imports;

public sealed record FolderImportOptions(
    string ImportedBy,
    bool RestatementEnabled = false,
    string RestatementReason = "",
    string? OverrideStoreCode = null,
    DateOnly? OverrideBusinessDate = null)
{
    /// <summary>
    /// Asks which current import a restatement replaces when its period overlaps several (IF-016); returns
    /// one of the candidates, or null when none was chosen. Automation sets none, so it never picks.
    /// </summary>
    public Func<RestatementTargetChoice, CancellationToken, Task<RestatementCandidate?>>? ChooseRestatementTarget { get; init; }
}

public sealed record FolderImportFileResult(
    string FileName,
    string? ReportCode,
    string? StoreCode,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string Status,
    int RowsProcessed = 0,
    int NewRows = 0,
    int AlreadyPresentRows = 0,
    int ConflictRows = 0,
    string Message = "",
    IReadOnlyList<ImportIssue>? Diagnostics = null)
{
    public string? SourcePath { get; init; }
    public string? SourceSha256 { get; init; }
    /// <summary>Code, stage, safe message and issues of a failed attempt (IF-017); null when it did not fail.</summary>
    public ImportFailure? Failure { get; init; }
    /// <summary>Whether the import transaction committed, rolled back or could not be confirmed (IF-014).</summary>
    public CommitState? CommitState { get; init; }
    /// <summary>Whether the source bytes are held inside the database (IF-023); null when not determined.</summary>
    public EvidenceState? Evidence { get; init; }
    public Guid? BatchId { get; init; }
    public string Period => PeriodStart is null ? "—" : PeriodStart == PeriodEnd
        ? PeriodStart.Value.ToString("dd MMM yyyy")
        : $"{PeriodStart:dd MMM yyyy} – {PeriodEnd:dd MMM yyyy}";
    public bool Failed => Status == "Failed";
}

public sealed record FolderImportProgress(
    int Completed, int Total, string CurrentFile, string Stage,
    IReadOnlyList<FolderImportFileResult> Files);

public sealed record FolderImportSummary(IReadOnlyList<FolderImportFileResult> Files)
{
    public int NewRows => Files.Sum(file => file.NewRows);
    public int AlreadyPresentRows => Files.Sum(file => file.AlreadyPresentRows);
    public int Conflicts => Files.Sum(file => file.ConflictRows);
    public int Failed => Files.Count(file => file.Failed);
    public int Duplicates => Files.Count(file => file.Status is "Duplicate" or "Duplicate content" or "Already present");
    public int Imported => Files.Count(file => file.Status is "Imported" or "empty export");
    public int UnknownLayouts => Files.Count(file => file.Status == "Unknown layout");
}

public interface IFolderImportService
{
    Task<FolderImportSummary> RunAsync(
        string sourcePath, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
