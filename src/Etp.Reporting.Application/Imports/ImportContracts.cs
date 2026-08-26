namespace Etp.Reporting.Application.Imports;

public enum ImportIssueSeverity
{
    Information,
    Warning,
    Blocker
}

public sealed record ImportIssue(
    ImportIssueSeverity Severity,
    string Code,
    string Message,
    int? SourceRow = null,
    string? SourceColumn = null);

public sealed record ImportFileDescriptor(
    string FileName,
    long SizeBytes,
    string Sha256);

public sealed record ImportOutcome(
    Guid BatchId,
    bool Committed,
    int ImportedRows,
    IReadOnlyList<ImportIssue> Issues);
