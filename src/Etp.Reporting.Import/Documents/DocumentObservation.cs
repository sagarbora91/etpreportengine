using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Documents;

/// <summary>What became of a landed row (<c>etp_import_content.disposition</c>, spec 5.2).</summary>
public enum RowDisposition
{
    /// <summary>K: part of the authoritative observation.</summary>
    Kept,
    /// <summary>C: a stale copy collapsed by the row rule.</summary>
    Collapsed,
    /// <summary>R: a row of an older block of the same source.</summary>
    OlderBlock,
    /// <summary>H: a held row, e.g. one without a usable date.</summary>
    Held
}

/// <summary>One row of an observation after the family row rule (spec 7.2).</summary>
public sealed record FactRow(RowLocator Source, CanonicalRow Canonical)
{
    public RowDisposition Disposition { get; init; } = RowDisposition.Kept;
    /// <summary>StockUnitChain and SnapshotItems ordinal; 1 for every other rule.</summary>
    public int LineSeq { get; init; } = 1;
    /// <summary>SnapshotItems: canonical text of the RowKey fields, pairing rows between two readings.</summary>
    public string? RowKey { get; init; }
    /// <summary>The planner-1 label of the typed fact the row becomes (<c>line_identifier</c>, enrichment <c>content_key</c>), over kept rows.</summary>
    public string? LineLabel { get; init; }
}

/// <summary>
/// One block's content for one document (spec 3): the kept rows after the row rule, with their hashes.
/// </summary>
/// <param name="DocumentDate">The document date, business day, snapshot date or period start.</param>
/// <param name="FactSha256"><c>fact_sha256</c>: multiset hash of the kept rows' <c>fact_row_hash</c>.</param>
/// <param name="AttributeSha256">Multiset hash of the kept rows' attribute hashes.</param>
public sealed record DocumentObservation(
    DocumentKey Key,
    int BlockNo,
    ExportTime ExportTime,
    DateOnly? DocumentDate,
    IReadOnlyList<FactRow> Rows,
    string FactSha256,
    string AttributeSha256)
{
    /// <summary>The last day of a Period document.</summary>
    public DateOnly? PeriodTo { get; init; }
    /// <summary>Typed families: multiset hash of the canonical projection rows the fact tables store.</summary>
    public string? CanonicalSha256 { get; init; }
    /// <summary>Collapsed, older-block and held rows, kept for lineage only.</summary>
    public IReadOnlyList<FactRow> SetAside { get; init; } = [];
    /// <summary>A legacy merge of several blocks (spec 6.6); its export time is always unknown.</summary>
    public bool IsLegacyMerge { get; init; }
    /// <summary><c>IN_SOURCE_CONFLICT</c> or <c>LEGACY_BLOCKS_DIFFER</c> when the source itself holds the document (spec 6.6-6.7).</summary>
    public string? HoldCode { get; init; }

    public int RowCount => Rows.Count;
}

/// <summary>One block of a source to project: its physical and virtual rows (spec 6.5), with the family's rules.</summary>
public sealed record ProjectionRequest(EtpReportFamily Family, string StoreCode, SourceBlock Block, IReadOnlyList<SourceRow> Rows);

/// <summary>The documents one block observes, and the rows that belong to none (held, <c>ROW_DATE_MISSING</c>).</summary>
public sealed record BlockProjection(
    int BlockNo,
    IReadOnlyList<DocumentObservation> Documents,
    IReadOnlyList<FactRow> HeldRows,
    IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>
/// Turns a block's rows into documents and observations per family (spec 7): scope and key, the row rule,
/// planner-1-compatible labels, and <c>fact_sha256</c> from Key and Fact fields only.
/// </summary>
public interface IDocumentProjector
{
    BlockProjection Project(ProjectionRequest request);
}

/// <summary>What an earlier block of the same source says about a document (spec 6.7).</summary>
public sealed record InSourceDecision(DocumentKey Key, int BlockNo, DocumentDecision Decision);

/// <summary>
/// The authoritative observation per document, from the block with the greatest export time; earlier blocks
/// are attested or restated in source; same-time differences and differing legacy blocks are held.
/// </summary>
public sealed record SourceResolution(
    IReadOnlyList<DocumentObservation> Authoritative,
    IReadOnlyList<InSourceDecision> EarlierBlocks,
    IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>Resolution inside one source, with legacy merge and holds (spec 6.6-6.7). Pure; runs before the decision engine.</summary>
public interface IInSourceResolver
{
    SourceResolution Resolve(SourceDescription source, IReadOnlyList<BlockProjection> blocks);
}
