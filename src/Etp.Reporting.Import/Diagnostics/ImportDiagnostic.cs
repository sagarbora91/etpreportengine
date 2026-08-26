namespace Etp.Reporting.Import.Diagnostics;

public enum ImportDiagnosticSeverity { Information, Warning, Blocker }

public sealed record ImportDiagnostic(
    string Code,
    ImportDiagnosticSeverity Severity,
    string Message,
    string? SheetName = null,
    int? RowNumber = null,
    string? ColumnName = null);
