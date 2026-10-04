namespace Etp.Reporting.Application.Imports;

public sealed record ImportHistoryScope(DateOnly From, DateOnly To, string? StoreCode = null);
public sealed record ImportHistoryEntry(string Key, DateTime RecordedUtc, long? ImportFileId,
    FolderImportFileResult Result);

public interface IImportHistoryQuery
{
    Task<IReadOnlyList<ImportHistoryEntry>> LoadAsync(ImportHistoryScope scope, CancellationToken cancellationToken = default);
}

public interface IImportAttemptRecorder
{
    Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default);
}

/// <summary>
/// The guidance Imports &gt; History shows for a saved outcome. The query and the view both use it, so a
/// repeat file reads "already imported" whether its outcome was saved as "Duplicate content" (the planner,
/// and the history classification) or as "Duplicate" (rows written before 0041).
/// </summary>
public static class ImportHistoryMessages
{
    public const string AlreadyImported = "This source was already imported. No facts were added by this attempt.";
    public const string Failed = "Import failed. Review the selected diagnostics and correct the source before retrying.";
    public const string Persisted = "Persisted import outcome. Counts describe source rows; a source row can produce multiple database facts.";

    public static bool IsDuplicate(string? status) =>
        string.Equals(status, ImportAttemptOutcomes.DuplicateContent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, ImportAttemptOutcomes.Duplicate, StringComparison.OrdinalIgnoreCase);

    public static string Conflicts(int conflicts) =>
        $"{conflicts:N0} source rows conflict with existing data. Review the source before retrying.";

    /// <summary>A recorded failure's own message wins, then conflicts, then the outcome's guidance.</summary>
    public static string ForSavedOutcome(string status, int conflicts, ImportFailure? failure) =>
        failure is not null ? failure.SafeMessage
        : conflicts > 0 ? Conflicts(conflicts)
        : IsDuplicate(status) ? AlreadyImported
        : string.Equals(status, ImportAttemptOutcomes.Failed, StringComparison.Ordinal) ? Failed
        : Persisted;
}
