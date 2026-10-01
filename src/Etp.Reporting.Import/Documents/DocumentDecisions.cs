using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Documents;

/// <summary>The decision values of <c>import_document_decisions.decision</c> (spec Appendix B).</summary>
public enum DocumentDecision
{
    New, Present, Refreshed, AttributeUpdated, Filled, Grown, ProvisionalUpdated, ReadingUpdated,
    Stale, NotAdded, PendingChange, PendingExists, RejectedBefore, HeldLocked, HeldHeaderDate,
    InSourceConflict, LegacyBlocksDiffer, AttestedInSource, RestatedInSource, Reinterpreted, MissingFromLater
}

/// <summary><c>import_change_items.reason_code</c>.</summary>
public enum ChangeReason
{
    NewOnLockedDay, LaterExport, UnknownProvenance, SameExportDiffers, SnapshotShrink, MissingFromLater, Reappeared,
    Reinterpretation, InSourceConflict, LegacyBlocksDiffer, HeaderDateMismatch, MigrationReview, OwnerRestatement,
    Provisional, Reading, Attribute, Fill, Grow
}

/// <summary><c>import_change_items.action</c>.</summary>
public enum ChangeAction { None, Replace, Insert, Retire, Update, Trim }

/// <summary><c>import_change_sets.mode</c>: applied in the import (archived, audited) or waiting for the Owner.</summary>
public enum ChangeMode { Auto, Review }

/// <summary><c>import_change_items.status</c>.</summary>
public enum ChangeItemStatus { Pending, Applied, Rejected, Kept, Obsolete, Held }

/// <summary><c>fact_document_versions.change_kind</c>.</summary>
public enum VersionChangeKind { Backfill, Resync, New, Replace, Fill, Grow, Attribute, Provisional, Reading, Trim }

/// <summary>Who runs the import (spec 8.1 <c>Owner</c>). Automation is a Store Manager that never approves or restates.</summary>
public enum ImporterRole { Owner, StoreManager, Automation }

/// <summary>
/// When a provisional-day update or a same-date snapshot reading of a typed family applies without review
/// (spec 8.3, rules 13-14). <see cref="OwnerImportOnly"/> is OD-7's recommendation and the default; the others are
/// kept as a setting. Landing-only families apply automatically under every policy (spec 10.1).
/// </summary>
public enum ProvisionalPolicy
{
    /// <summary>Automatic only when the Owner runs the import; a one-click review item for anyone else (recommended).</summary>
    OwnerImportOnly,
    /// <summary>Always a review item.</summary>
    AlwaysReview,
    /// <summary>Automatic for any importer.</summary>
    Anyone
}

/// <summary>Settings of the decision engine that the Owner may change.</summary>
public sealed record DecisionPolicy(ProvisionalPolicy Provisional = ProvisionalPolicy.OwnerImportOnly)
{
    public static DecisionPolicy Default { get; } = new();
}

/// <summary>
/// Everything the decision engine needs for one (store, report) of one source (spec 8). Pure data: the engine
/// reads no database.
/// </summary>
/// <param name="Incoming">The authoritative observation per document (spec 6.7), holds included.</param>
/// <param name="Stored">Stored documents by <see cref="DocumentKey.Hash"/>: the incoming documents, the CURRENT
/// documents within the incoming blocks' coverage (for MISSING_FROM_LATER retire items) and the
/// <see cref="EarlierReadingDocuments"/>.</param>
/// <param name="Blocks">Registered blocks of the same store and report, and the incoming source's blocks (spec 8.4).</param>
/// <param name="LockedDays">The store's LOCKED days within the documents' dates.</param>
public sealed record DecisionRequest(
    string StoreCode,
    string ReportCode,
    EtpFamilyIdentity Identity,
    IReadOnlyList<DocumentObservation> Incoming,
    IReadOnlyDictionary<string, StoredDocument> Stored,
    IReadOnlyList<CoverageBlock> Blocks,
    IReadOnlySet<DateOnly> LockedDays,
    ImporterRole Role)
{
    /// <summary>Sales and revenue families: existing invoice headers by <see cref="DocumentKey.Hash"/> (rule 4).</summary>
    public IReadOnlyDictionary<string, InvoiceHeaderState> Headers { get; init; } = new Dictionary<string, InvoiceHeaderState>();
    /// <summary>Spec 8.5: documents a current planner-1 file with the same SHA-256 but another declared scope introduced.</summary>
    public IReadOnlyList<DocumentKey> EarlierReadingDocuments { get; init; } = [];
    public DecisionPolicy Policy { get; init; } = DecisionPolicy.Default;
}

/// <summary>
/// A change the decision proposes: applied now (AUTO) or a review item. A held change (a locked day, or a source to
/// fix) is always a review item and keeps its reason.
/// </summary>
/// <param name="NewVersionKind">The version the change creates; null for RETIRE and NONE.</param>
public sealed record ChangeProposal(
    ChangeMode Mode,
    ChangeReason Reason,
    ChangeAction Action,
    VersionChangeKind? NewVersionKind,
    bool Held = false)
{
    public ChangeItemStatus ItemStatus => Held ? ChangeItemStatus.Held : Mode == ChangeMode.Auto ? ChangeItemStatus.Applied : ChangeItemStatus.Pending;
}

/// <summary>The engine's decision for one document (spec 8.2), with the side effects it asks for.</summary>
public sealed record DocumentDecisionResult(DocumentKey Key, DocumentDecision Decision)
{
    /// <summary>
    /// The incoming block decided on. For a <c>MISSING_FROM_LATER</c> retire item, the incoming block the document is
    /// missing from; null for a <c>REINTERPRETATION</c> retire item, which is about the source as a whole.
    /// </summary>
    public int? BlockNo { get; init; }
    public ChangeProposal? Change { get; init; }
    /// <summary>An information code that goes with the decision, e.g. <c>ATTRIBUTE_NOT_APPLIED</c>.</summary>
    public string? DetailCode { get; init; }
    /// <summary>Attest the current or pending version and raise <c>last_attested_time</c>.</summary>
    public bool Attest { get; init; }
    /// <summary>Move the landing-rows pointer to the incoming block (rule 9b).</summary>
    public bool MoveRowsPointer { get; init; }
    /// <summary>Clear the provisional flag (rule 9c).</summary>
    public bool SettleProvisional { get; init; }
    /// <summary>Pending INSERT items of the document become OBSOLETE (rule 8).</summary>
    public bool ObsoletePendingInsert { get; init; }
    /// <summary>
    /// The version this decision creates or proposes is provisional (spec 8.3): every export that attests it has a
    /// known date on or before the document's business day.
    /// </summary>
    public bool NewVersionProvisional { get; init; }
}

public sealed record DecisionOutcome(IReadOnlyList<DocumentDecisionResult> Decisions, IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>
/// The document decision engine (spec 8): rules 1-15 in order, first match wins; absence (8.4); reinterpretation
/// (8.5); locked days hold changes (8.6, OD-5). The result does not depend on import order (8.7).
/// </summary>
public interface IDocumentDecisionEngine
{
    DecisionOutcome Decide(DecisionRequest request);
}
