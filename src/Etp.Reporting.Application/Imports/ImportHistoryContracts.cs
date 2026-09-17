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
