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
    IReadOnlyList<WorkbookSheet> Sheets,
    string? SourcePath = null)
{
    /// <summary>
    /// The bytes the reader parsed, from its single in-memory snapshot, so the import transaction can keep
    /// them as evidence (IF-023, spec 11.2). Null when the snapshot was not read from a file.
    /// </summary>
    public WorkbookContent? Content { get; init; }

    /// <summary>The source bytes, but only while <see cref="Sha256"/> is still their hash; empty otherwise.</summary>
    public ReadOnlyMemory<byte> EvidenceBytes =>
        Content is { } content && string.Equals(content.Sha256, Sha256, StringComparison.OrdinalIgnoreCase)
            ? content.Bytes : ReadOnlyMemory<byte>.Empty;
}

/// <summary>A workbook's file bytes and their SHA-256 (lowercase hex).</summary>
public sealed record WorkbookContent(ReadOnlyMemory<byte> Bytes, string Sha256);

public interface IWorkbookReader
{
    Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}
