namespace Etp.Reporting.Import.Workbooks;

public sealed record WorkbookCell(object? Value, string? DisplayText = null);

public sealed record WorkbookRow(int RowNumber, IReadOnlyList<WorkbookCell> Cells);

public sealed record WorkbookSheet(
    string Name,
    int HeaderRowNumber,
    IReadOnlyList<string> Headers,
    IReadOnlyList<WorkbookRow> Rows);

public sealed record WorkbookSnapshot(
    string FileName,
    long FileSizeBytes,
    string Sha256,
    IReadOnlyList<WorkbookSheet> Sheets);

public interface IWorkbookReader
{
    Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
