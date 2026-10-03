using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Documents;

/// <summary><c>fact_documents.status</c>.</summary>
public enum DocumentStatus { Current, Pending, Retired, NotAdded }

/// <summary><c>fact_documents.review_state</c>.</summary>
public enum ReviewState { None, Pending, Held }

/// <summary><c>fact_document_versions.state</c>.</summary>
public enum VersionState { Current, Superseded, Pending, Rejected, Obsolete }

/// <summary>How a version's facts were verified (<c>fact_document_versions.basis</c>).</summary>
public enum VersionBasis { SourceRows, CanonicalOnly, Unverified }

/// <summary>A stored version of a document as <c>read_document_state</c> returns it (spec 5.3).</summary>
/// <param name="LastAttested">cur.T of spec 8.1: the latest known time among the exports that attested the version.</param>
public sealed record StoredVersion(
    long VersionId,
    int VersionNo,
    VersionBasis Basis,
    string? FactSha256,
    string? CanonicalSha256,
    string? AttributeSha256,
    int RowCount,
    ExportTime LastAttested,
    bool Provisional)
{
    /// <summary>The export that introduced the version.</summary>
    public ExportTime ExportTime { get; init; } = ExportTime.Unknown;
    public int RulesetVersion { get; init; } = 1;
    /// <summary>The version's rows, loaded only for documents the caller flagged as differing (rowset 3).</summary>
    public IReadOnlyList<FactRow>? Rows { get; init; }
}

/// <summary>
/// A stored document and its CURRENT version (spec 5.2 <c>fact_documents</c>). For a RETIRED document,
/// <see cref="Current"/> is the version its retirement superseded: rule 6 compares an incoming export with that
/// version's last attested time.
/// </summary>
public sealed record StoredDocument(
    DocumentKey Key,
    long DocumentId,
    DocumentStatus Status,
    DateOnly? DocumentDate,
    StoredVersion? Current)
{
    public DateOnly? PeriodTo { get; init; }
    public ReviewState ReviewState { get; init; } = ReviewState.None;
    /// <summary>The export time of the landing rows the document points at (descriptive values; landing-only facts).</summary>
    public ExportTime RowsPointerTime { get; init; } = ExportTime.Unknown;
    public IReadOnlyList<string> PendingFactSha256 { get; init; } = [];
    public IReadOnlyList<string> RejectedFactSha256 { get; init; } = [];
    /// <summary>A document with a PENDING INSERT item (rules 3, 5, 6), which a NEW decision makes OBSOLETE.</summary>
    public bool HasPendingInsert { get; init; }
}

/// <summary>An existing <c>sales_invoices</c> header (store, FY, number) shared by R025 and R022 (spec 7.3).</summary>
/// <param name="IsOrphan">No line, control or tender refers to it; it does not block a NEW document.</param>
public sealed record InvoiceHeaderState(DocumentKey Key, DateOnly TransactionDate, bool IsOrphan);

/// <summary>
/// A block that may show a document missing from a later export (spec 8.4): a registered block of the same
/// store and report, or a block of the incoming source (<see cref="ImportFileId"/> null). Its coverage is the day range
/// <see cref="CoverageFrom"/>..<see cref="CoverageTo"/>, or, for snapshot documents, exactly the snapshot dates in
/// <see cref="CoveredDates"/>; days in <see cref="UncertainDates"/> are never covered.
/// </summary>
public sealed record CoverageBlock(
    long? ImportFileId,
    int BlockNo,
    ExportTime ExportTime,
    BlockCompleteness Completeness,
    DateOnly? CoverageFrom,
    DateOnly? CoverageTo,
    IReadOnlySet<string> ObservedDocuments)
{
    /// <summary>
    /// Snapshot documents: the snapshot dates the block read (its own snapshot date and every snapshot it observes). A
    /// snapshot block covers only these, never a declared or observed period (a pack folder's dates are not readings).
    /// </summary>
    public IReadOnlySet<DateOnly>? CoveredDates { get; init; }

    /// <summary>
    /// Days on which the block holds rows it could not place in a document (spec 7.4 held rows): it cannot show that a
    /// document of such a day is missing.
    /// </summary>
    public IReadOnlySet<DateOnly> UncertainDates { get; init; } = new HashSet<DateOnly>();

    public bool IsIncoming => ImportFileId is null;

    /// <summary>The block covers at least one day, so it takes part in the absence check.</summary>
    public bool HasCoverage => CoveredDates is { Count: > 0 } || (CoveredDates is null && CoverageFrom is not null && CoverageTo is not null);

    public bool Covers(DateOnly date) => !UncertainDates.Contains(date) && (CoveredDates is { } dates
        ? dates.Contains(date)
        : CoverageFrom is { } from && CoverageTo is { } to && date >= from && date <= to);

    /// <summary>The block covers every day of <paramref name="from"/>..<paramref name="to"/> (a document's day or period).</summary>
    public bool CoversDays(DateOnly from, DateOnly to)
    {
        if (to < from || !Covers(from) || !Covers(to) || UncertainDates.Any(day => day >= from && day <= to)) return false;
        if (CoveredDates is not { } dates) return true;
        for (var day = from; day <= to; day = day.AddDays(1))
            if (!dates.Contains(day)) return false;
        return true;
    }

    /// <summary>True when the block observed the document (by <see cref="DocumentKey.Hash"/>).</summary>
    public bool Observed(DocumentKey key) => ObservedDocuments.Contains(key.Hash);
}
