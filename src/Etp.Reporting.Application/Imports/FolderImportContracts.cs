namespace Etp.Reporting.Application.Imports;

public sealed record FolderImportOptions(
    string ImportedBy,
    bool RestatementEnabled = false,
    string RestatementReason = "",
    string? OverrideStoreCode = null,
    DateOnly? OverrideBusinessDate = null);

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
    public string? SourceSha256 { get; init; }
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
