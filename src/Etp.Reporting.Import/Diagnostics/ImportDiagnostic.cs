using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Import.Diagnostics;

public enum ImportDiagnosticSeverity { Information, Warning, Blocker }

public sealed record ImportDiagnostic(
    string Code,
    ImportDiagnosticSeverity Severity,
    string Message,
    string? SheetName = null,
    int? RowNumber = null,
    string? ColumnName = null)
{
    /// <summary>The block of a consolidated source the diagnostic belongs to, when it belongs to one.</summary>
    public int? BlockNo { get; init; }
    /// <summary>Document number, date and product code only; never a customer value.</summary>
    public string? DocumentRef { get; init; }
    /// <summary>How many rows or documents the diagnostic stands for.</summary>
    public int Occurrences { get; init; } = 1;

    /// <summary>The attempt issue that records this diagnostic; the sheet name is not stored.</summary>
    public ImportIssue ToImportIssue() => new((ImportIssueSeverity)(int)Severity, Code, Message, RowNumber, ColumnName,
        BlockNo, DocumentRef, Occurrences);
}
