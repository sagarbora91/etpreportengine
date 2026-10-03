using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Documents;

/// <summary>
/// Resolution inside one source (spec 6.6-6.7), pure and before the decision engine. Per document:
/// <list type="number">
/// <item>For a transactional document (Document or Date scope), the legacy blocks that hold it are merged into one
/// observation (<see cref="LegacyMerge"/>), held with <c>LEGACY_BLOCKS_DIFFER</c> when a later block holds a fact row no
/// earlier block holds. A snapshot or period document's legacy blocks are not merged: each is one reading, ranked below.</item>
/// <item>The authoritative observation is the one whose export time is greatest by <see cref="ExportOrder"/>; an
/// unknown time ranks below every known one. A legacy merge's own time is unknown, but it ranks by the known export times
/// of the blocks it merged: a timed block outranks it only when that block is newer than every one of them. Otherwise
/// the merge stands, with its hold, and an older export never wins over newer legacy blocks.</item>
/// <item>When the top observations cannot be ordered (the <c>SAME</c> time, or times <see cref="ExportOrder"/> cannot
/// order) and their facts differ, the document is held with <c>IN_SOURCE_CONFLICT</c>; with equal facts the highest
/// block number stands for them.</item>
/// <item>Every other block is <c>ATTESTED_IN_SOURCE</c> (equal facts) or <c>RESTATED_IN_SOURCE</c>, and its rows are
/// set aside as <c>R</c>.</item>
/// </list>
/// A held document is never a file blocker: its diagnostics are warnings, and the decision engine turns the hold into
/// a review item. Absence inside the source is the decision engine's (spec 8.4), from <see cref="CoverageBlocks"/>.
/// </summary>
public sealed class InSourceResolver(IFactCanonicalizer canonicalizer) : IInSourceResolver
{
    private readonly LegacyMerge legacyMerge = new(canonicalizer);

    public SourceResolution Resolve(SourceDescription source, IReadOnlyList<BlockProjection> blocks)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        var blockInfo = source.Blocks.ToDictionary(block => block.BlockNo);
        var observations = blocks.SelectMany(projection => projection.Documents).ToArray();
        var authoritative = new List<DocumentObservation>();
        var earlier = new List<InSourceDecision>();
        var conflicts = new List<DocumentObservation>();
        var legacyHolds = new List<DocumentObservation>();

        foreach (var group in observations.GroupBy(observation => observation.Key.Hash).OrderBy(group => group.First().Key.KeyText, StringComparer.Ordinal))
        {
            var candidates = new List<DocumentObservation>();
            var legacy = group.Where(observation => IsLegacy(blockInfo, observation.BlockNo)).ToArray();
            LegacyMergeResult? merged = null;
            // Spec 6.6, contract 3.3 and 11: only a transactional document is merged. A snapshot or period document's
            // legacy blocks are readings of one snapshot or period and are ranked like any other block.
            if (legacy.Length > 1 && IsTransactional(group.First().Key.Scope))
            {
                merged = legacyMerge.Merge(legacy);
                candidates.Add(merged.Observation);
            }
            else candidates.AddRange(legacy);
            candidates.AddRange(group.Where(observation => !IsLegacy(blockInfo, observation.BlockNo)));

            var (winner, decisions) = Choose(candidates, MergedTimes(legacy, merged));
            authoritative.Add(winner);
            if (merged is not null)
                // The merge's own decisions stand only when the merge is authoritative. When a timed block outranks it,
                // each legacy block is judged against that block instead, and the merge's hold does not apply.
                earlier.AddRange(winner.IsLegacyMerge ? merged.Decisions
                    : legacy.Where(observation => observation.BlockNo != merged.Observation.BlockNo)
                        .Select(observation => new InSourceDecision(observation.Key, observation.BlockNo,
                            SameFacts(observation, winner) ? DocumentDecision.AttestedInSource : DocumentDecision.RestatedInSource)));
            earlier.AddRange(decisions);
            if (winner.HoldCode == ImportCodes.InSourceConflict && decisions.Any(decision => decision.Decision == DocumentDecision.InSourceConflict))
                conflicts.Add(winner);
            if (winner.IsLegacyMerge && winner.HoldCode == ImportCodes.LegacyBlocksDiffer) legacyHolds.Add(winner);
        }

        var diagnostics = new List<ImportDiagnostic>();
        if (conflicts.Count > 0)
            diagnostics.Add(Held(ImportCodes.InSourceConflict, conflicts,
                "document(s) differ between blocks whose export times cannot be ordered; each is held for the Owner and not applied"));
        if (legacyHolds.Count > 0)
            diagnostics.Add(Held(ImportCodes.LegacyBlocksDiffer, legacyHolds,
                "document(s) hold, in a later legacy block, a line no earlier block holds; a grown document cannot be told from a re-stated line, so each is held for the Owner"));
        return new SourceResolution(authoritative, earlier, diagnostics);
    }

    /// <summary>
    /// The incoming blocks for the absence check (spec 8.4, <c>B′</c> in the same file).
    /// <list type="bullet">
    /// <item>Coverage: for snapshot documents (<paramref name="scope"/> Snapshot), exactly the snapshot dates the block
    /// read, its own snapshot date and every snapshot it observes, never a declared or observed period: a raw CLOSING_STOCK
    /// export in a pack folder reads its own snapshots, not every day of the pack. Otherwise the block's period, or its
    /// snapshot date.</item>
    /// <item>Observed: the documents the block's projection holds, and the documents of its held rows whose key is known
    /// (a row held for a missing fact value is still in the export).</item>
    /// <item>Held rows whose document is unknown make their days uncertain; a held row with no usable date leaves the
    /// block out of the absence check, since it could belong to any document.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<CoverageBlock> CoverageBlocks(SourceDescription source, IReadOnlyList<BlockProjection> blocks, DocumentScope scope)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(blocks);
        return source.Blocks.Select(block =>
        {
            var projections = blocks.Where(projection => projection.BlockNo == block.BlockNo).ToArray();
            var observed = projections.SelectMany(projection => projection.Documents.Select(observation => observation.Key)
                    .Concat(projection.HeldDocuments))
                .Select(key => key.Hash)
                .ToHashSet(StringComparer.Ordinal);
            var uncertain = projections.SelectMany(projection => projection.HeldDates).ToHashSet();
            if (projections.Any(projection => projection.HasUndatedHeldRows))
                return new CoverageBlock(null, block.BlockNo, block.ExportTime, block.Completeness, null, null, observed)
                {
                    CoveredDates = new HashSet<DateOnly>()
                };
            if (scope == DocumentScope.Snapshot)
            {
                // A held document is observed, so whether its date is covered never matters.
                var dates = projections.SelectMany(projection => projection.Documents.Select(observation => observation.DocumentDate))
                    .Append(block.SnapshotDate)
                    .OfType<DateOnly>()
                    .ToHashSet();
                return new CoverageBlock(null, block.BlockNo, block.ExportTime, block.Completeness,
                    dates.Count == 0 ? null : dates.Min(), dates.Count == 0 ? null : dates.Max(), observed)
                {
                    CoveredDates = dates,
                    UncertainDates = uncertain
                };
            }
            return new CoverageBlock(null, block.BlockNo, block.ExportTime, block.Completeness,
                block.PeriodFrom ?? block.SnapshotDate, block.PeriodTo ?? block.SnapshotDate, observed)
            {
                UncertainDates = uncertain
            };
        }).ToArray();
    }

    /// <summary>The known export times of the legacy blocks a merge took in; empty when there is no merge.</summary>
    private static IReadOnlyList<ExportTime> MergedTimes(IReadOnlyList<DocumentObservation> legacy, LegacyMergeResult? merged) =>
        merged is null ? [] : legacy.Select(observation => observation.ExportTime).Where(time => time.IsKnown).ToArray();

    /// <summary>Document and Date scope: the families whose consolidation rule is <c>transactional</c> (contract 6).</summary>
    private static bool IsTransactional(DocumentScope scope) => scope is DocumentScope.Document or DocumentScope.Date;

    private static bool IsLegacy(IReadOnlyDictionary<int, SourceBlock> blocks, int blockNo) =>
        blocks.TryGetValue(blockNo, out var block) && block.Completeness == BlockCompleteness.Legacy;

    private static (DocumentObservation Winner, IReadOnlyList<InSourceDecision> Decisions) Choose(
        IReadOnlyList<DocumentObservation> candidates, IReadOnlyList<ExportTime> mergedTimes)
    {
        if (candidates.Count == 1) return (candidates[0], []);
        // A legacy merge ranks by the known times of its blocks; every other observation by its own time.
        IReadOnlyList<ExportTime> Times(DocumentObservation candidate) =>
            candidate.IsLegacyMerge ? mergedTimes : candidate.ExportTime.IsKnown ? [candidate.ExportTime] : [];
        // Newer: one of a's times is newer than every time of b. For one time each this is ExportOrder's NEWER, and a
        // timed block is newer than a merge only when it is newer than every block the merge took in.
        bool Newer(DocumentObservation a, DocumentObservation b) =>
            Times(a).Any(time => Times(b).All(other => ExportOrder.IsNewer(time, other)));
        var known = candidates.Where(candidate => Times(candidate).Count > 0).ToArray();
        var pool = known.Length > 0 ? known : candidates.ToArray();
        // The top: every observation no other one is known to be newer than.
        var top = pool.Where(candidate => !pool.Any(other => !ReferenceEquals(other, candidate) && Newer(other, candidate))).ToArray();
        // A held merge that nothing outranks keeps its hold: it is the winner, never an export not newer than its blocks.
        var winner = top.FirstOrDefault(candidate => candidate.IsLegacyMerge && candidate.HoldCode is not null)
            ?? top.OrderByDescending(candidate => candidate.BlockNo).First();
        var conflict = top.Any(candidate => !SameFacts(candidate, winner));

        var decisions = new List<InSourceDecision>();
        var setAside = winner.SetAside.ToList();
        foreach (var other in candidates.Where(candidate => !ReferenceEquals(candidate, winner)).OrderBy(candidate => candidate.BlockNo))
        {
            var decision = conflict && top.Any(candidate => ReferenceEquals(candidate, other)) ? DocumentDecision.InSourceConflict
                : SameFacts(other, winner) ? DocumentDecision.AttestedInSource
                : DocumentDecision.RestatedInSource;
            decisions.Add(new InSourceDecision(other.Key, other.BlockNo, decision));
            setAside.AddRange(other.Rows.Concat(other.SetAside).Select(row => row with { Disposition = RowDisposition.OlderBlock }));
        }
        return (winner with
        {
            SetAside = setAside,
            HoldCode = winner.HoldCode ?? (conflict ? ImportCodes.InSourceConflict : null)
        }, decisions);
    }

    private static bool SameFacts(DocumentObservation left, DocumentObservation right) =>
        string.Equals(left.FactSha256, right.FactSha256, StringComparison.Ordinal);

    private static ImportDiagnostic Held(string code, IReadOnlyList<DocumentObservation> documents, string text) =>
        new(code, (ImportDiagnosticSeverity)(int)ImportCodes.DefaultSeverity(code),
            $"{documents.Count} {text} ({DiagnosticRefs(documents)}).")
        {
            DocumentRef = documents[0].Key.KeyText,
            BlockNo = documents[0].BlockNo,
            Occurrences = documents.Count
        };

    /// <summary>Report and key text only (financial year, number or date); never a customer value.</summary>
    private static string DiagnosticRefs(IReadOnlyList<DocumentObservation> documents)
    {
        var keys = documents.Select(document => document.Key.KeyText).ToArray();
        return keys.Length <= 10 ? string.Join(", ", keys) : $"{string.Join(", ", keys.Take(10))} and {keys.Length - 10} more";
    }
}

/// <summary>One document's legacy blocks merged into one observation, and what each other block says about it.</summary>
public sealed record LegacyMergeResult(DocumentObservation Observation, IReadOnlyList<InSourceDecision> Decisions)
{
    public bool IsHeld => Observation.HoldCode is not null;
}

/// <summary>
/// The legacy merge of spec 6.6 (<c>LEGACY_MERGE</c>) for one document held by several <c>LEGACY</c> blocks, whose
/// builder left out exact cross-export repeats without a map:
/// <list type="number">
/// <item>Each block's observation is already after the row rule's descriptive collapse (spec 7.2, the projector).</item>
/// <item>The blocks are ordered by export time, unknown last, then by block number.</item>
/// <item>Consistent: every later block holds only fact rows some earlier block holds. Each distinct fact row is taken
/// with the <b>largest</b> count it has in any one block, never the sum; the copies kept are those of the latest block
/// with that count. Other copies are set aside: <c>R</c> in an older block, <c>C</c> in a later one.</item>
/// <item>Inconsistent: some later block holds a fact row no earlier block holds. The merged observation is still
/// proposed, but held with <c>LEGACY_BLOCKS_DIFFER</c>; nothing is applied automatically.</item>
/// </list>
/// The merged observation's export time is unknown, so any difference from stored facts goes to review (spec 8); inside
/// the source it ranks by its blocks' known times (<see cref="InSourceResolver"/>). Its rows come from every block that
/// supplied a kept copy (<see cref="DocumentObservation.RowsBlockNos"/>), and its canonical hash is over their fact-table
/// rows (<see cref="FactRow.FactTableRows"/>). The merge
/// is for transactional documents only; <see cref="InSourceResolver"/> never merges a snapshot or period document.
/// Whether it equals the stored facts (decision PRESENT, step 5) is the decision engine's.
/// </summary>
public sealed class LegacyMerge(IFactCanonicalizer canonicalizer)
{
    public LegacyMergeResult Merge(IReadOnlyList<DocumentObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (observations.Count == 0) throw new ArgumentException("A merge needs at least one observation.", nameof(observations));
        if (observations.Select(observation => observation.Key.Hash).Distinct(StringComparer.Ordinal).Count() > 1)
            throw new ArgumentException("A merge takes the observations of one document.", nameof(observations));

        var ordered = observations
            .OrderBy(observation => observation.ExportTime.IsKnown ? 0 : 1)
            .ThenBy(observation => observation.ExportTime.Instant ?? DateTime.MaxValue)
            .ThenBy(observation => observation.BlockNo)
            .ToArray();

        var consistent = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Length; index++)
        {
            var hashes = ordered[index].Rows.Select(row => row.Canonical.FactRowHash).ToHashSet(StringComparer.Ordinal);
            if (index > 0 && !hashes.IsSubsetOf(seen)) consistent = false;
            seen.UnionWith(hashes);
        }

        var kept = new List<FactRow>();
        var setAside = ordered.SelectMany(observation => observation.SetAside).ToList();
        var positions = ordered.Select((observation, index) => (observation.BlockNo, index)).ToDictionary(pair => pair.BlockNo, pair => pair.index);
        foreach (var hash in ordered.SelectMany(observation => observation.Rows).Select(row => row.Canonical.FactRowHash).Distinct(StringComparer.Ordinal))
        {
            var counts = ordered.Select(observation => observation.Rows.Count(row => row.Canonical.FactRowHash == hash)).ToArray();
            var largest = counts.Max();
            var source = Array.LastIndexOf(counts, largest);
            for (var index = 0; index < ordered.Length; index++)
            {
                var rows = ordered[index].Rows.Where(row => row.Canonical.FactRowHash == hash);
                if (index == source) kept.AddRange(rows.Select(row => row with { Disposition = RowDisposition.Kept }));
                else setAside.AddRange(rows.Select(row => row with
                {
                    Disposition = index < source ? RowDisposition.OlderBlock : RowDisposition.Collapsed
                }));
            }
        }
        kept = kept.OrderBy(row => positions.GetValueOrDefault(row.Source.BlockNo)).ThenBy(row => row.Source.SourceRowNumber).ToList();

        var latest = ordered[^1];
        var contributors = kept.Select(row => row.Source.BlockNo).Distinct().Order().ToArray();
        var merged = latest with
        {
            ExportTime = ExportTime.Unknown,
            Rows = kept,
            SetAside = setAside,
            // The kept copies may come from several blocks; the document's rows are its K rows in each of them.
            RowsBlockNos = contributors.Length == 0 ? [latest.BlockNo] : contributors,
            FactSha256 = canonicalizer.MultisetHash(kept.Select(row => row.Canonical.FactRowHash)),
            AttributeSha256 = canonicalizer.MultisetHash(kept.Select(row => row.Canonical.AttributeHash)),
            // A typed family's canonical hash is over the kept rows' fact-table rows, wherever each kept row came from.
            CanonicalSha256 = ordered.All(observation => observation.CanonicalSha256 is not null)
                ? canonicalizer.MultisetHash(kept.SelectMany(row => row.FactTableRows).Select(row => row.Hash))
                : null,
            DocumentDate = latest.DocumentDate ?? ordered.Select(observation => observation.DocumentDate).LastOrDefault(date => date is not null),
            IsLegacyMerge = true,
            HoldCode = consistent ? latest.HoldCode ?? ordered.Select(observation => observation.HoldCode).FirstOrDefault(code => code is not null)
                : ImportCodes.LegacyBlocksDiffer
        };
        var decision = consistent ? DocumentDecision.AttestedInSource : DocumentDecision.LegacyBlocksDiffer;
        var decisions = ordered.Where(observation => observation.BlockNo != latest.BlockNo)
            .Select(observation => new InSourceDecision(observation.Key, observation.BlockNo, decision))
            .ToArray();
        return new LegacyMergeResult(merged, decisions);
    }
}
