namespace Etp.Reporting.Application.Imports;

public enum ImportIssueSeverity
{
    Information,
    Warning,
    Blocker
}

/// <summary>
/// One diagnostic of an import attempt (<c>dbo.import_attempt_issues</c> from migration 0038). Messages are
/// written by the code; <see cref="DocumentRef"/> holds only a document number, date and product code.
/// </summary>
public sealed record ImportIssue(
    ImportIssueSeverity Severity,
    string Code,
    string Message,
    int? SourceRow = null,
    string? SourceColumn = null,
    int? BlockNo = null,
    string? DocumentRef = null,
    int Occurrences = 1);

public sealed record ImportFileDescriptor(
    string FileName,
    long SizeBytes,
    string Sha256);

public sealed record ImportOutcome(
    Guid BatchId,
    bool Committed,
    int ImportedRows,
    IReadOnlyList<ImportIssue> Issues);
