using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Documents;

/// <summary>
/// The document decision engine (spec 8). Pure: it reads only the request and writes nothing.
/// <list type="bullet">
/// <item>Each incoming observation goes through rules 1-15 in order; the first match wins (8.2).</item>
/// <item>A document the source holds itself (<c>IN_SOURCE_CONFLICT</c>, <c>LEGACY_BLOCKS_DIFFER</c>) is never applied (6.6-6.7).</item>
/// <item>Then CURRENT documents missing from a later complete incoming block become RETIRE items (8.4), and documents
/// an earlier reading of the same workbook introduced but this reading does not produce become RETIRE items (8.5).</item>
/// <item>Every change on a locked day is held, reason kept (8.6, OD-5). Nothing is ever deleted without review.</item>
/// </list>
/// </summary>
public sealed class DocumentDecisionEngine : IDocumentDecisionEngine
{
    public DecisionOutcome Decide(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decisions = new List<DocumentDecisionResult>(request.Incoming.Count);
        var diagnostics = new List<ImportDiagnostic>();
        var incoming = new HashSet<string>(StringComparer.Ordinal);
        foreach (var observation in request.Incoming)
        {
            CheckKey(request, observation.Key);
            if (!incoming.Add(observation.Key.Hash))
                throw new ArgumentException($"Document {observation.Key} has more than one authoritative observation.", nameof(request));
            request.Stored.TryGetValue(observation.Key.Hash, out var stored);
            var decision = DecideOne(request, observation, stored);
            decisions.Add(decision);
            if (Diagnostic(observation, decision) is { } diagnostic) diagnostics.Add(diagnostic);
        }

        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Reinterpretations(request, incoming).Concat(MissingFromLater(request)))
        {
            if (retired.Add(item.Key.Hash)) decisions.Add(item);
        }
        return new DecisionOutcome(decisions, diagnostics);
    }

    private static DocumentDecisionResult DecideOne(DecisionRequest request, DocumentObservation o, StoredDocument? stored)
    {
        var locked = IsLocked(request.LockedDays, o.DocumentDate, o.PeriodTo);
        var result = new DocumentDecisionResult(o.Key, DocumentDecision.Present) { BlockNo = o.BlockNo };

        // 6.7: two blocks of the same export time disagree. Nothing can be applied until the source is fixed.
        if (o.HoldCode == ImportCodes.InSourceConflict)
            return result with
            {
                Decision = DocumentDecision.InSourceConflict,
                Change = new ChangeProposal(ChangeMode.Review, ChangeReason.InSourceConflict, ChangeAction.None, null, Held: true)
            };

        // Rule 1: the incoming facts are already queued for the Owner.
        if (stored is not null && stored.PendingFactSha256.Contains(o.FactSha256, StringComparer.OrdinalIgnoreCase))
            return result with { Decision = DocumentDecision.PendingExists, Attest = true };
        // Rule 2: the Owner rejected these facts before; never raised again.
        if (stored is not null && stored.RejectedFactSha256.Contains(o.FactSha256, StringComparer.OrdinalIgnoreCase))
            return result with { Decision = DocumentDecision.RejectedBefore };

        var legacyDiffers = o.HoldCode == ImportCodes.LegacyBlocksDiffer;
        var cur = stored is { Status: DocumentStatus.Current, Current: { } current } ? current : null;
        if (cur is null) return DecideWithoutFacts(request, o, stored, result, locked, legacyDiffers);

        var ord = ExportOrder.Compare(o.ExportTime, cur.LastAttested);
        var newerOrFirstKnown = ord == ExportOrderResult.Newer || (!cur.LastAttested.IsKnown && o.ExportTime.IsKnown);
        var typed = request.Identity.HasTypedFacts;

        // Rule 9: the facts are present. Attest, then attributes (a), the rows pointer (b) and settling (c).
        if (FactsEqual(o, cur))
        {
            result = result with
            {
                Attest = true,
                MoveRowsPointer = RowsPointerMoves(o.ExportTime, stored!.RowsPointerTime),
                SettleProvisional = cur.Provisional && SettlesDay(o.ExportTime, BusinessDate(o))
            };
            if (!string.Equals(o.AttributeSha256, cur.AttributeSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (!newerOrFirstKnown) return result with { DetailCode = ImportCodes.AttributeNotApplied };
                result = Change(result with { Decision = DocumentDecision.AttributeUpdated },
                    new ChangeProposal(ChangeMode.Auto, ChangeReason.Attribute, ChangeAction.Update, VersionChangeKind.Attribute), locked);
                return result with { NewVersionProvisional = cur.Provisional && !result.SettleProvisional };
            }
            return result.MoveRowsPointer ? result with { Decision = DocumentDecision.Refreshed } : result;
        }

        // 6.6: a legacy merge whose blocks disagree is never applied automatically; the Owner decides.
        if (legacyDiffers)
            return Proposal(result with { Decision = DocumentDecision.LegacyBlocksDiffer },
                ChangeMode.Review, ChangeReason.LegacyBlocksDiffer, ChangeAction.Replace, VersionChangeKind.Replace, o, locked);

        // Rule 10: a v0 document gains values it never had (gross, tax). Archived, then updated in place.
        if (cur.Basis == VersionBasis.CanonicalOnly && OnlyFillsLegacyNulls(o, cur, request.Identity.LegacyNullable))
        {
            result = Change(result with { Decision = DocumentDecision.Filled },
                new ChangeProposal(ChangeMode.Auto, ChangeReason.Fill, ChangeAction.Update, VersionChangeKind.Fill), locked);
            return result with { NewVersionProvisional = cur.Provisional && Provisional(o) };
        }

        // Rule 11: an older export never changes a newer one.
        if (ord == ExportOrderResult.Older) return result with { Decision = DocumentDecision.Stale };

        // Rule 12: late rows on a landing-only day or period.
        if (request.Identity.Scope is DocumentScope.Date or DocumentScope.Period && !typed && newerOrFirstKnown
            && cur.Rows is { } stored12 && IsSuperset(o.Rows, stored12))
            return Proposal(result with { Decision = DocumentDecision.Grown, MoveRowsPointer = true },
                ChangeMode.Auto, ChangeReason.Grow, ChangeAction.Replace, VersionChangeKind.Grow, o, locked);

        // Rule 13: a day exported before it was over, changed by an export taken by the end of the next day.
        if (cur.Provisional && ord == ExportOrderResult.Newer && InProvisionalWindow(o.ExportTime, BusinessDate(o)))
        {
            var auto = AppliesAutomatically(request);
            return Proposal(result with { Decision = auto ? DocumentDecision.ProvisionalUpdated : DocumentDecision.PendingChange },
                auto ? ChangeMode.Auto : ChangeMode.Review, ChangeReason.Provisional, ChangeAction.Replace,
                VersionChangeKind.Provisional, o, locked);
        }

        // Rule 14: a newer reading of the same snapshot or period; a row removed always needs the Owner.
        if (request.Identity.ChangePolicy == ChangePolicy.LatestReadingWins && ord == ExportOrderResult.Newer)
        {
            if (Shrinks(o.Rows, cur.Rows))
                return Proposal(result with { Decision = DocumentDecision.PendingChange },
                    ChangeMode.Review, ChangeReason.SnapshotShrink, ChangeAction.Replace, VersionChangeKind.Reading, o, locked);
            var auto = AppliesAutomatically(request);
            return Proposal(result with { Decision = auto ? DocumentDecision.ReadingUpdated : DocumentDecision.PendingChange },
                auto ? ChangeMode.Auto : ChangeMode.Review, ChangeReason.Reading, ChangeAction.Replace,
                VersionChangeKind.Reading, o, locked);
        }

        // Rule 15: a genuine change to a settled document.
        var reason = ord switch
        {
            ExportOrderResult.Newer => ChangeReason.LaterExport,
            ExportOrderResult.Same => ChangeReason.SameExportDiffers,
            _ => ChangeReason.UnknownProvenance
        };
        return Proposal(result with { Decision = DocumentDecision.PendingChange },
            ChangeMode.Review, reason, ChangeAction.Replace, VersionChangeKind.Replace, o, locked);
    }

    /// <summary>Rules 3-8: no current facts (no document, NOT_ADDED, PENDING or RETIRED).</summary>
    private static DocumentDecisionResult DecideWithoutFacts(
        DecisionRequest request, DocumentObservation o, StoredDocument? stored, DocumentDecisionResult result, bool locked,
        bool legacyDiffers)
    {
        var retired = stored?.Status == DocumentStatus.Retired;

        // Rule 3: a later complete export of the same store and report lacks the document. Not added; the Owner decides.
        // A RETIRED document counts too: otherwise an export between the one that retired it and the one it is missing
        // from would raise REAPPEARED in one import order and nothing in the other (8.7).
        if (IsAbsentFromLater(request.Blocks, o))
            return Proposal(result with { Decision = DocumentDecision.NotAdded },
                ChangeMode.Review, ChangeReason.MissingFromLater, ChangeAction.Insert, VersionChangeKind.New, o, locked);

        // Rule 4: a shared invoice header already carries another date. Never merged, never re-dated.
        if (request.Identity.Route is FamilyRoute.Sales or FamilyRoute.Revenue
            && request.Headers.TryGetValue(o.Key.Hash, out var header) && !header.IsOrphan
            && o.DocumentDate is { } date && header.TransactionDate != date)
            return result with
            {
                Decision = DocumentDecision.HeldHeaderDate,
                Change = new ChangeProposal(ChangeMode.Review, ChangeReason.HeaderDateMismatch, ChangeAction.None, null, Held: true)
            };

        // Rule 5: a new document on a locked day waits for the day to be reopened.
        if (locked)
            return Proposal(result with { Decision = DocumentDecision.HeldLocked },
                ChangeMode.Review, ChangeReason.NewOnLockedDay, ChangeAction.Insert, VersionChangeKind.New, o, locked);

        if (retired)
        {
            // Rules 6-7: a retired document comes back only from an export newer than every export that attested it.
            var lastAttested = stored!.Current?.LastAttested ?? ExportTime.Unknown;
            if (ExportOrder.Compare(o.ExportTime, lastAttested) != ExportOrderResult.Newer)
                return result with { Decision = DocumentDecision.Stale };
            return Proposal(result with { Decision = DocumentDecision.PendingChange },
                ChangeMode.Review, ChangeReason.Reappeared, ChangeAction.Insert, VersionChangeKind.New, o, locked);
        }

        // 6.6: a legacy merge whose blocks disagree is proposed, never inserted.
        if (legacyDiffers)
            return Proposal(result with { Decision = DocumentDecision.LegacyBlocksDiffer },
                ChangeMode.Review, ChangeReason.LegacyBlocksDiffer, ChangeAction.Insert, VersionChangeKind.New, o, locked);

        // Rule 8: new, including a NOT_ADDED document now seen in an export newer than every export that lacked it.
        return result with
        {
            Decision = DocumentDecision.New,
            ObsoletePendingInsert = stored?.HasPendingInsert ?? false,
            NewVersionProvisional = Provisional(o)
        };
    }

    /// <summary>A change proposing the incoming observation as the document's next version.</summary>
    private static DocumentDecisionResult Proposal(
        DocumentDecisionResult result, ChangeMode mode, ChangeReason reason, ChangeAction action, VersionChangeKind kind,
        DocumentObservation o, bool locked) =>
        Change(result, new ChangeProposal(mode, reason, action, kind), locked) with { NewVersionProvisional = Provisional(o) };

    /// <summary>
    /// 8.6: on a locked day every change, automatic or reviewed, becomes a HELD item with its reason kept. Fact changes
    /// are recorded as <c>HELD_LOCKED</c>; NOT_ADDED, the source holds and retire items keep their decision.
    /// </summary>
    private static DocumentDecisionResult Change(DocumentDecisionResult result, ChangeProposal change, bool locked)
    {
        if (!locked) return result with { Change = change };
        var decision = result.Decision is DocumentDecision.NotAdded or DocumentDecision.LegacyBlocksDiffer
            or DocumentDecision.MissingFromLater or DocumentDecision.Reinterpreted
            ? result.Decision
            : DocumentDecision.HeldLocked;
        return result with
        {
            Decision = decision,
            Change = change with { Mode = ChangeMode.Review, Held = true },
            // A held GROW leaves the rows where they are; a descriptive refresh is allowed on a locked day.
            MoveRowsPointer = result.MoveRowsPointer && result.Decision != DocumentDecision.Grown
        };
    }

    /// <summary>
    /// 8.4 after the documents: a CURRENT document missing from a newer complete incoming block whose coverage holds
    /// its day, and whose day was over when that block was exported, becomes a RETIRE item for review.
    /// </summary>
    private static IEnumerable<DocumentDecisionResult> MissingFromLater(DecisionRequest request)
    {
        var blocks = request.Blocks.Where(block => block.IsIncoming && IsRebuildable(block.Completeness)
            && block.ExportTime.IsKnown && block.CoverageFrom is not null && block.CoverageTo is not null).ToArray();
        if (blocks.Length == 0) yield break;
        foreach (var stored in CurrentDocuments(request))
        {
            var cur = stored.Current!;
            var from = stored.DocumentDate!.Value;
            var to = stored.PeriodTo ?? from;
            // The newest block the document is missing from; the lowest block number among equal times.
            var missingFrom = blocks
                .Where(block => block.Covers(from) && block.Covers(to) && to < block.ExportTime.ExportDate!.Value
                    && !block.Observed(stored.Key)
                    && (!cur.LastAttested.IsKnown || ExportOrder.IsNewer(block.ExportTime, cur.LastAttested)))
                .OrderByDescending(block => block.ExportTime.Instant)
                .ThenBy(block => block.BlockNo)
                .FirstOrDefault();
            if (missingFrom is null) continue;
            yield return Retire(request, stored, DocumentDecision.MissingFromLater, ChangeReason.MissingFromLater) with
            {
                BlockNo = missingFrom.BlockNo
            };
        }
    }

    /// <summary>8.5: documents an earlier reading of the same workbook introduced that this reading does not produce.</summary>
    private static IEnumerable<DocumentDecisionResult> Reinterpretations(DecisionRequest request, IReadOnlySet<string> incoming)
    {
        foreach (var key in request.EarlierReadingDocuments.DistinctBy(key => key.Hash).OrderBy(key => key.KeyText, StringComparer.Ordinal))
        {
            CheckKey(request, key);
            if (incoming.Contains(key.Hash)) continue;
            if (!request.Stored.TryGetValue(key.Hash, out var stored) || stored is not { Status: DocumentStatus.Current, Current: not null })
                continue;
            yield return Retire(request, stored, DocumentDecision.Reinterpreted, ChangeReason.Reinterpretation);
        }
    }

    private static DocumentDecisionResult Retire(DecisionRequest request, StoredDocument stored, DocumentDecision decision, ChangeReason reason) =>
        Change(new DocumentDecisionResult(stored.Key, decision),
            new ChangeProposal(ChangeMode.Review, reason, ChangeAction.Retire, null),
            IsLocked(request.LockedDays, stored.DocumentDate, stored.PeriodTo));

    private static IEnumerable<StoredDocument> CurrentDocuments(DecisionRequest request) =>
        request.Stored.Values
            .Where(stored => stored is { Status: DocumentStatus.Current, Current: not null, DocumentDate: not null }
                && stored.Key.ReportCode == Upper(request.ReportCode) && stored.Key.StoreCode == Upper(request.StoreCode))
            .OrderBy(stored => stored.DocumentDate)
            .ThenBy(stored => stored.Key.KeyText, StringComparer.Ordinal);

    /// <summary>
    /// A(d) of 8.4: some block of the same store and report, newer than the observation's export, rebuildable, covering
    /// the document's days and taken after they were over, does not hold the document. Stored or incoming alike.
    /// </summary>
    private static bool IsAbsentFromLater(IReadOnlyList<CoverageBlock> blocks, DocumentObservation o)
    {
        if (o.DocumentDate is not { } from || !o.ExportTime.IsKnown) return false;
        var to = o.PeriodTo ?? from;
        return blocks.Any(block => IsRebuildable(block.Completeness) && block.ExportTime.IsKnown
            && ExportOrder.Compare(block.ExportTime, o.ExportTime) == ExportOrderResult.Newer
            && block.Covers(from) && block.Covers(to) && to < block.ExportTime.ExportDate!.Value
            && !block.Observed(o.Key));
    }

    private static bool IsRebuildable(BlockCompleteness completeness) =>
        completeness is BlockCompleteness.Complete or BlockCompleteness.Delta;

    /// <summary>
    /// "Facts equal" of 8.1: the same <c>fact_sha256</c>, or the same <c>canonical_sha256</c> when the current version
    /// was not verified against source rows (and both sides carry one).
    /// </summary>
    private static bool FactsEqual(DocumentObservation o, StoredVersion cur)
    {
        if (cur.Basis != VersionBasis.SourceRows && cur.CanonicalSha256 is not null)
            return o.CanonicalSha256 is not null && string.Equals(o.CanonicalSha256, cur.CanonicalSha256, StringComparison.OrdinalIgnoreCase);
        return string.Equals(o.FactSha256, cur.FactSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rule 10: the rows pair one to one, and every difference is a <c>LegacyNullable</c> field that is NULL in the
    /// stored version; at least one such field gains a value. Needs the stored rows (rowset 3).
    /// </summary>
    private static bool OnlyFillsLegacyNulls(DocumentObservation o, StoredVersion cur, IReadOnlyList<string> legacyNullable)
    {
        if (legacyNullable.Count == 0 || cur.Rows is not { } storedRows || storedRows.Count != o.Rows.Count) return false;
        var nullable = new HashSet<string>(legacyNullable, StringComparer.OrdinalIgnoreCase);
        var unmatched = o.Rows.Select(row => row.Canonical.Facts).ToList();
        var filled = false;
        // Exact pairs first, so a fill never takes a row another stored row matches exactly.
        var pending = new List<IReadOnlyDictionary<string, string>>();
        foreach (var storedRow in storedRows.Select(row => row.Canonical.Facts))
        {
            var exact = unmatched.FindIndex(candidate => Pairs(storedRow, candidate, nullable, out var fills) && !fills);
            if (exact >= 0) unmatched.RemoveAt(exact);
            else pending.Add(storedRow);
        }
        foreach (var storedRow in pending)
        {
            var index = unmatched.FindIndex(candidate => Pairs(storedRow, candidate, nullable, out _));
            if (index < 0) return false;
            unmatched.RemoveAt(index);
            filled = true;
        }
        return filled;

        static bool Pairs(IReadOnlyDictionary<string, string> stored, IReadOnlyDictionary<string, string> incoming,
            HashSet<string> nullable, out bool fills)
        {
            fills = false;
            foreach (var (field, value) in stored)
            {
                var other = incoming.TryGetValue(field, out var text) ? text : string.Empty;
                if (string.Equals(value, other, StringComparison.Ordinal)) continue;
                if (!nullable.Contains(field) || !string.IsNullOrEmpty(value)) return false;
                fills = true;
            }
            return true;
        }
    }

    /// <summary>Rule 12: the incoming rows contain every stored row, with multiplicity.</summary>
    private static bool IsSuperset(IReadOnlyList<FactRow> incoming, IReadOnlyList<FactRow> stored)
    {
        var counts = incoming.GroupBy(row => row.Canonical.FactRowHash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var row in stored)
        {
            if (!counts.TryGetValue(row.Canonical.FactRowHash, out var count) || count == 0) return false;
            counts[row.Canonical.FactRowHash] = count - 1;
        }
        return incoming.Count > stored.Count;
    }

    /// <summary>
    /// Rule 14: some row key of the stored reading is missing from the incoming one. Without the stored rows the
    /// engine cannot prove nothing was removed, so it treats the reading as a shrink (review).
    /// </summary>
    private static bool Shrinks(IReadOnlyList<FactRow> incoming, IReadOnlyList<FactRow>? stored)
    {
        if (stored is null) return true;
        var keys = incoming.Select(RowKey).ToHashSet(StringComparer.Ordinal);
        return stored.Any(row => !keys.Contains(RowKey(row)));

        static string RowKey(FactRow row) => row.RowKey ?? row.Canonical.FactRowHash.ToLowerInvariant();
    }

    /// <summary>Rule 9b: newer than the pointer, or the pointer unknown and the export known, or both unknown (import order).</summary>
    private static bool RowsPointerMoves(ExportTime incoming, ExportTime pointer) =>
        ExportOrder.IsNewer(incoming, pointer)
        || !pointer.IsKnown && incoming.IsKnown
        || !pointer.IsKnown && !incoming.IsKnown;

    /// <summary>The last day of the document: its date, or the end of its period.</summary>
    private static DateOnly? BusinessDate(DocumentObservation o) => o.PeriodTo ?? o.DocumentDate;

    /// <summary>8.3: the export was taken on or before the document's last day, so the day was not over.</summary>
    private static bool Provisional(DocumentObservation o) =>
        o.ExportTime.IsKnown && BusinessDate(o) is { } day && o.ExportTime.ExportDate!.Value <= day;

    /// <summary>Rule 9c: a known export dated after the business day settles a provisional version.</summary>
    private static bool SettlesDay(ExportTime time, DateOnly? day) =>
        time.IsKnown && day is { } value && time.ExportDate!.Value > value;

    /// <summary>Rule 13: the export was taken by the end of the next day.</summary>
    private static bool InProvisionalWindow(ExportTime time, DateOnly? day) =>
        time.IsKnown && day is { } value && time.ExportDate!.Value <= value.AddDays(1);

    /// <summary>Rules 13-14 under the provisional policy (OD-7): landing-only families need no Owner.</summary>
    private static bool AppliesAutomatically(DecisionRequest request) => request.Policy.Provisional switch
    {
        ProvisionalPolicy.Anyone => true,
        ProvisionalPolicy.AlwaysReview => false,
        _ => !request.Identity.HasTypedFacts || request.Role == ImporterRole.Owner
    };

    /// <summary>L(d) of 8.1: the document date, the snapshot date, or any day of a Period document is LOCKED.</summary>
    private static bool IsLocked(IReadOnlySet<DateOnly> lockedDays, DateOnly? date, DateOnly? periodTo)
    {
        if (lockedDays.Count == 0 || date is not { } from) return false;
        var to = periodTo ?? from;
        return lockedDays.Any(day => day >= from && day <= to);
    }

    private static ImportDiagnostic? Diagnostic(DocumentObservation o, DocumentDecisionResult decision)
    {
        var code = decision.DetailCode ?? (decision.Decision == DocumentDecision.HeldHeaderDate ? ImportCodes.HeaderDateMismatch : null);
        if (code is null) return null;
        var message = code == ImportCodes.AttributeNotApplied
            ? $"Attribute values of {o.Key.KeyText} differ, but this export is not newer than the stored one; they were not applied."
            : $"{o.Key.KeyText} is held: its invoice header already carries another date.";
        return new ImportDiagnostic(code, (ImportDiagnosticSeverity)(int)ImportCodes.DefaultSeverity(code), message)
        {
            BlockNo = o.BlockNo,
            DocumentRef = o.Key.KeyText
        };
    }

    private static void CheckKey(DecisionRequest request, DocumentKey key)
    {
        if (key.ReportCode != Upper(request.ReportCode) || key.StoreCode != Upper(request.StoreCode))
            throw new ArgumentException($"Document {key} does not belong to {request.StoreCode}/{request.ReportCode}.", nameof(request));
    }

    private static string Upper(string value) => value.Trim().ToUpperInvariant();
}
