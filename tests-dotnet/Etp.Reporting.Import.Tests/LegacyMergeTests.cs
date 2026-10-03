using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// Legacy consolidated workbooks (spec 6.6): a document's legacy blocks merge by the largest count of each fact row,
/// never the sum, and a later block holding a fact row no earlier block holds is held for the Owner.
/// </summary>
public sealed class LegacyMergeTests
{
    private static readonly DocumentKey Invoice = DocumentKey.ForDocument("R025", "WLMHW", new DateOnly(2026, 7, 1), "100001");

    [Fact]
    public void Consistent_blocks_merge_by_maximum_never_sum()
    {
        // The builder left exact cross-export repeats out without a map: block 2 holds one copy of P1 that block 1
        // already held twice, and block 3 (the latest export) holds P1 three times.
        var first = Observation(1, Time(7, 2), ("P1", 2), ("P2", 1));
        var second = Observation(2, Time(8, 7), ("P1", 1));
        var third = Observation(3, Time(9, 6), ("P1", 3));

        var merged = new LegacyMerge(TestSources.Canonicalizer).Merge([third, first, second]);

        Assert.False(merged.IsHeld);
        Assert.Null(merged.Observation.HoldCode);
        Assert.True(merged.Observation.IsLegacyMerge);
        Assert.False(merged.Observation.ExportTime.IsKnown);
        Assert.Equal(3, merged.Observation.BlockNo);
        // P1 three times (the largest count in one block, not 2 + 1 + 3) and P2 once.
        Assert.Equal(4, merged.Observation.RowCount);
        Assert.Equal(3, merged.Observation.Rows.Count(row => row.Source.BlockNo == 3));
        Assert.Equal(1, merged.Observation.Rows.Count(row => row.Source.BlockNo == 1));
        Assert.All(merged.Observation.Rows, row => Assert.Equal(RowDisposition.Kept, row.Disposition));
        Assert.Equal(TestSources.Canonicalizer.MultisetHash(merged.Observation.Rows.Select(row => row.Canonical.FactRowHash)), merged.Observation.FactSha256);
        // The other copies are set aside for lineage: older blocks as R.
        Assert.Equal(3, merged.Observation.SetAside.Count);
        Assert.All(merged.Observation.SetAside, row => Assert.Equal(RowDisposition.OlderBlock, row.Disposition));
        Assert.Equal(
            [new InSourceDecision(Invoice, 1, DocumentDecision.AttestedInSource), new InSourceDecision(Invoice, 2, DocumentDecision.AttestedInSource)],
            merged.Decisions);
    }

    [Fact]
    public void Copies_kept_come_from_the_latest_block_with_the_largest_count()
    {
        var first = Observation(1, Time(7, 2), ("P1", 2));
        var second = Observation(2, Time(8, 7), ("P1", 2));
        var third = Observation(3, Time(9, 6), ("P1", 1));

        var merged = new LegacyMerge(TestSources.Canonicalizer).Merge([first, second, third]);

        Assert.Equal(2, merged.Observation.RowCount);
        Assert.All(merged.Observation.Rows, row => Assert.Equal(2, row.Source.BlockNo));
        // Block 1 is older than the kept copies (R); block 3 is a later, partial copy (C).
        Assert.Equal(2, merged.Observation.SetAside.Count(row => row.Disposition == RowDisposition.OlderBlock));
        Assert.Equal(1, merged.Observation.SetAside.Count(row => row.Disposition == RowDisposition.Collapsed));
    }

    [Fact]
    public void Legacy_blocks_that_differ_are_held()
    {
        // Block 2 holds a P3 line no earlier block holds: a grown invoice or a re-stated line, the importer cannot tell.
        var first = Observation(1, Time(7, 2), ("P1", 1), ("P2", 1));
        var second = Observation(2, Time(8, 7), ("P1", 1), ("P3", 1));

        var merged = new LegacyMerge(TestSources.Canonicalizer).Merge([first, second]);

        Assert.True(merged.IsHeld);
        Assert.Equal(ImportCodes.LegacyBlocksDiffer, merged.Observation.HoldCode);
        // The merged observation is still the proposal: every distinct line once.
        Assert.Equal(3, merged.Observation.RowCount);
        Assert.False(merged.Observation.ExportTime.IsKnown);
        Assert.Equal([new InSourceDecision(Invoice, 1, DocumentDecision.LegacyBlocksDiffer)], merged.Decisions);
    }

    [Fact]
    public void Unknown_export_times_order_last()
    {
        // Block 1 has no known time, so the dated block 2 is earlier; block 1 then holds a line block 2 lacks.
        var undated = Observation(1, ExportTime.Unknown, ("P1", 1), ("P2", 1));
        var dated = Observation(2, Time(8, 7), ("P1", 1));

        var merged = new LegacyMerge(TestSources.Canonicalizer).Merge([undated, dated]);

        Assert.Equal(ImportCodes.LegacyBlocksDiffer, merged.Observation.HoldCode);
        Assert.Equal(1, merged.Observation.BlockNo);
    }

    [Fact]
    public void Resolver_merges_legacy_blocks_and_reports_holds_as_warnings()
    {
        var source = new SourceDescription(SourceKind.ConsolidatedLegacy,
        [
            Block(1, Time(7, 2)), Block(2, Time(8, 7))
        ], [], []);
        var consistent = DocumentKey.ForDocument("R025", "WLMHW", new DateOnly(2026, 7, 1), "100002");
        BlockProjection[] projections =
        [
            new(1, [Observation(1, Time(7, 2), ("P1", 2)), Observation(1, Time(7, 2), ("P1", 1)) with { Key = consistent }], [], []),
            new(2, [Observation(2, Time(8, 7), ("P1", 1), ("P9", 1)), Observation(2, Time(8, 7), ("P1", 1)) with { Key = consistent }], [], [])
        ];

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        Assert.Equal(2, resolution.Authoritative.Count);
        var held = resolution.Authoritative.Single(observation => observation.Key == Invoice);
        Assert.Equal(ImportCodes.LegacyBlocksDiffer, held.HoldCode);
        Assert.Equal(3, held.RowCount);
        var merged = resolution.Authoritative.Single(observation => observation.Key == consistent);
        Assert.Null(merged.HoldCode);
        Assert.Equal(1, merged.RowCount);
        Assert.True(merged.IsLegacyMerge);
        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ImportCodes.LegacyBlocksDiffer, diagnostic.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Occurrences);
        Assert.Equal(Invoice.KeyText, diagnostic.DocumentRef);
    }

    [Fact]
    public void Timed_block_outranks_a_legacy_merge_in_a_contract_workbook()
    {
        var source = new SourceDescription(SourceKind.Consolidated,
        [
            Block(1, ExportTime.Unknown), Block(2, ExportTime.Unknown),
            Block(3, Time(9, 29)) with { Completeness = BlockCompleteness.Complete, Origin = BlockOrigin.Contract }
        ], [], []);
        BlockProjection[] projections =
        [
            new(1, [Observation(1, ExportTime.Unknown, ("P1", 1))], [], []),
            new(2, [Observation(2, ExportTime.Unknown, ("P1", 1))], [], []),
            new(3, [Observation(3, Time(9, 29), ("P1", 1), ("P2", 1))], [], [])
        ];

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        var winner = Assert.Single(resolution.Authoritative);
        Assert.Equal(3, winner.BlockNo);
        Assert.Null(winner.HoldCode);
        // Once the timed block wins, every legacy block is judged against it: both lack its P2 line.
        Assert.Equal(
            [new InSourceDecision(Invoice, 1, DocumentDecision.RestatedInSource), new InSourceDecision(Invoice, 2, DocumentDecision.RestatedInSource)],
            resolution.EarlierBlocks.OrderBy(decision => decision.BlockNo));
    }

    [Fact]
    public void Held_legacy_merge_outranked_by_a_timed_block_is_not_held()
    {
        // Blocks 1 and 2 are legacy and inconsistent (block 2 adds P3); block 3 is a timed export of a contract workbook.
        var source = new SourceDescription(SourceKind.Consolidated,
        [
            Block(1, ExportTime.Unknown), Block(2, ExportTime.Unknown),
            Block(3, Time(9, 29)) with { Completeness = BlockCompleteness.Complete, Origin = BlockOrigin.Contract }
        ], [], []);
        BlockProjection[] projections =
        [
            new(1, [Observation(1, ExportTime.Unknown, ("P1", 1), ("P2", 1))], [], []),
            new(2, [Observation(2, ExportTime.Unknown, ("P1", 1), ("P3", 1))], [], []),
            new(3, [Observation(3, Time(9, 29), ("P1", 1), ("P2", 1))], [], [])
        ];

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        var winner = Assert.Single(resolution.Authoritative);
        Assert.Equal(3, winner.BlockNo);
        Assert.Null(winner.HoldCode);
        Assert.Empty(resolution.Diagnostics);
        Assert.DoesNotContain(resolution.EarlierBlocks, decision => decision.Decision == DocumentDecision.LegacyBlocksDiffer);
        // Each legacy block is judged against the timed winner: block 1 says the same, block 2 does not.
        Assert.Contains(new InSourceDecision(Invoice, 1, DocumentDecision.AttestedInSource), resolution.EarlierBlocks);
        Assert.Contains(new InSourceDecision(Invoice, 2, DocumentDecision.RestatedInSource), resolution.EarlierBlocks);
        Assert.Equal(2, resolution.EarlierBlocks.Count);
    }

    [Theory]
    [InlineData(7, 15, true)]    // the raw 15 Jul export appended later (contract rule 10) is older than both legacy blocks
    [InlineData(8, 10, true)]    // newer than the 7 Aug block, older than the 25 Aug block
    [InlineData(8, 30, false)]   // newer than every merged block: it outranks the merge
    public void A_timed_block_outranks_a_legacy_merge_only_when_newer_than_every_merged_block(int month, int day, bool mergeStands)
    {
        // A contract R025 workbook: legacy blocks 2 (7 Aug) and 3 (25 Aug) both hold the invoice, block 3 with a line
        // block 2 lacks (inconsistent, held). Block 5 is a complete raw export.
        var source = new SourceDescription(SourceKind.Consolidated,
        [
            Block(2, Time(8, 7)), Block(3, Time(8, 25)),
            Block(5, Time(month, day)) with { Completeness = BlockCompleteness.Complete, Origin = BlockOrigin.Contract }
        ], [], []);
        BlockProjection[] projections =
        [
            new(2, [Observation(2, Time(8, 7), ("P1", 1))], [], []),
            new(3, [Observation(3, Time(8, 25), ("P1", 1), ("P3", 1))], [], []),
            new(5, [Observation(5, Time(month, day), ("P1", 1), ("P2", 1))], [], [])
        ];

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        var winner = Assert.Single(resolution.Authoritative);
        if (mergeStands)
        {
            // The merge's hold stands and the older export is a restatement of it, never the new content.
            Assert.True(winner.IsLegacyMerge);
            Assert.Equal(ImportCodes.LegacyBlocksDiffer, winner.HoldCode);
            Assert.Equal(ImportCodes.LegacyBlocksDiffer, Assert.Single(resolution.Diagnostics).Code);
            Assert.Contains(new InSourceDecision(Invoice, 2, DocumentDecision.LegacyBlocksDiffer), resolution.EarlierBlocks);
            Assert.Contains(new InSourceDecision(Invoice, 5, DocumentDecision.RestatedInSource), resolution.EarlierBlocks);
            var family = ContractCatalogue.Family("R025");
            var decision = Assert.Single(new DocumentDecisionEngine().Decide(new DecisionRequest("WLMHW", "R025", family.Identity!, [winner],
                new Dictionary<string, StoredDocument>(), [], new HashSet<DateOnly>(), ImporterRole.Owner)).Decisions);
            Assert.Equal(DocumentDecision.LegacyBlocksDiffer, decision.Decision);
        }
        else
        {
            Assert.Equal(5, winner.BlockNo);
            Assert.Null(winner.HoldCode);
            Assert.Empty(resolution.Diagnostics);
        }
    }

    [Fact]
    public void A_consistent_legacy_merge_of_newer_blocks_outranks_an_older_timed_block()
    {
        var source = new SourceDescription(SourceKind.Consolidated,
        [
            Block(2, Time(8, 7)), Block(3, Time(8, 25)),
            Block(5, Time(7, 15)) with { Completeness = BlockCompleteness.Complete, Origin = BlockOrigin.Contract }
        ], [], []);
        BlockProjection[] projections =
        [
            new(2, [Observation(2, Time(8, 7), ("P1", 1), ("P3", 1))], [], []),
            new(3, [Observation(3, Time(8, 25), ("P1", 1))], [], []),
            new(5, [Observation(5, Time(7, 15), ("P1", 1), ("P2", 1))], [], [])
        ];

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        var winner = Assert.Single(resolution.Authoritative);
        Assert.True(winner.IsLegacyMerge);
        Assert.Null(winner.HoldCode);
        Assert.Equal(2, winner.RowCount);
        Assert.Contains(new InSourceDecision(Invoice, 5, DocumentDecision.RestatedInSource), resolution.EarlierBlocks);
    }

    [Fact]
    public void A_merge_whose_rows_come_from_several_blocks_points_at_all_of_them()
    {
        // Block 1 holds lines A, B, C; block 2 re-sent A with a new timestamp (an Ignored column), so the merge is
        // consistent and keeps A from block 2 and B, C from block 1. The document's rows are its K rows in both blocks.
        var first = Observation(1, Time(8, 5), ("A", 1), ("B", 1), ("C", 1));
        var second = Observation(2, Time(8, 7), ("A", 1));

        var merged = new LegacyMerge(TestSources.Canonicalizer).Merge([first, second]).Observation;

        Assert.Equal(2, merged.BlockNo);
        Assert.Equal([1, 2], merged.RowsBlockNos);
        Assert.Equal(merged.Rows.Select(row => row.Source.BlockNo).Distinct().Order(), merged.RowsBlockNos);
        Assert.Equal([2], second.RowsBlockNos);
        var family = ContractCatalogue.Family("R025");
        var decision = Assert.Single(new DocumentDecisionEngine().Decide(new DecisionRequest("WLMHW", "R025", family.Identity!, [merged],
            new Dictionary<string, StoredDocument>(), [], new HashSet<DateOnly>(), ImporterRole.Owner)).Decisions);
        Assert.Equal(DocumentDecision.New, decision.Decision);
        Assert.Equal(2, decision.BlockNo);
        Assert.Equal([1, 2], decision.RowsBlockNos);
    }

    [Fact]
    public void A_typed_merge_keeps_a_canonical_hash_over_rows_from_several_blocks()
    {
        // Spec 6.6 step 5: a merged observation equal to the stored facts is PRESENT. Against a version whose basis is
        // not SOURCE_ROWS that needs the merge's canonical hash, also when its kept rows come from two blocks.
        var family = ProjectorTestCatalogue.R025;
        var projector = new DocumentProjector(FactCanonicalizer.Instance);
        DocumentObservation Project(int blockNo, ExportTime time, params string[] products) =>
            Assert.Single(projector.Project(new(family, ProjectorTestCatalogue.Store, ProjectorTestCatalogue.Block(products.Length, time, blockNo),
                products.Select((product, index) => ProjectorTestCatalogue.Row(family, new RowLocator(blockNo, ProjectorTestCatalogue.Sheet, index + 2),
                    ("product_code", product), ("source_store_timestamp", $"2026-08-0{blockNo} 10:00:00"))).ToArray())).Documents);
        var whole = Project(1, Time(8, 5), "ITEM-1", "ITEM-2");
        var partial = Project(2, Time(8, 7), "ITEM-1");

        var merged = new LegacyMerge(FactCanonicalizer.Instance).Merge([whole, partial]).Observation;

        Assert.Equal([1, 2], merged.RowsBlockNos);
        Assert.NotNull(whole.CanonicalSha256);
        Assert.Equal(whole.CanonicalSha256, merged.CanonicalSha256);
        foreach (var basis in new[] { VersionBasis.CanonicalOnly, VersionBasis.Unverified })
        {
            var version = new StoredVersion(1, 1, basis, new string('0', 64), whole.CanonicalSha256, merged.AttributeSha256, 2, ExportTime.Unknown, false);
            var stored = new StoredDocument(merged.Key, 7, DocumentStatus.Current, merged.DocumentDate, version);
            var decision = Assert.Single(new DocumentDecisionEngine().Decide(new DecisionRequest(ProjectorTestCatalogue.Store, "R025", family.Identity!,
                [merged], new Dictionary<string, StoredDocument> { [stored.Key.Hash] = stored }, [], new HashSet<DateOnly>(), ImporterRole.Owner)).Decisions);
            Assert.True(decision.Attest);
            Assert.Null(decision.Change);
        }
    }

    [Fact]
    public void Snapshot_legacy_blocks_are_ranked_not_merged()
    {
        // Two tiling Info blocks of R010 dated the same day: two readings of one snapshot, never a union by maximum.
        var snapshot = DocumentKey.ForSnapshot("R010", "WLMHW", new DateOnly(2026, 8, 7));
        var source = new SourceDescription(SourceKind.ConsolidatedLegacy, [Block(1, Time(8, 7)), Block(2, Time(8, 7)) with { ExportTime = Late(8, 7) }], [], []);
        BlockProjection[] projections =
        [
            new(1, [Observation(1, Time(8, 7), ("P1", 1), ("P2", 1)) with { Key = snapshot }], [], []),
            new(2, [Observation(2, Late(8, 7), ("P1", 1), ("P3", 1)) with { Key = snapshot }], [], [])
        ];
        var undated = source with { Blocks = [Block(1, ExportTime.Unknown), Block(2, ExportTime.Unknown)] };
        BlockProjection[] undatedProjections =
        [
            new(1, [Observation(1, ExportTime.Unknown, ("P1", 1), ("P2", 1)) with { Key = snapshot }], [], []),
            new(2, [Observation(2, ExportTime.Unknown, ("P1", 1), ("P3", 1)) with { Key = snapshot }], [], [])
        ];
        var resolver = new InSourceResolver(TestSources.Canonicalizer);

        var timed = resolver.Resolve(source, projections);
        var unordered = resolver.Resolve(undated, undatedProjections);

        var latest = Assert.Single(timed.Authoritative);
        Assert.False(latest.IsLegacyMerge);
        Assert.Equal(2, latest.BlockNo);
        Assert.Equal(2, latest.RowCount);
        Assert.Null(latest.HoldCode);
        Assert.Empty(timed.Diagnostics);
        Assert.Equal([new InSourceDecision(snapshot, 1, DocumentDecision.RestatedInSource)], timed.EarlierBlocks);
        // Without export times the two readings cannot be ordered and differ: held as an in-source conflict.
        var held = Assert.Single(unordered.Authoritative);
        Assert.False(held.IsLegacyMerge);
        Assert.Equal(ImportCodes.InSourceConflict, held.HoldCode);
        Assert.Equal(ImportCodes.InSourceConflict, Assert.Single(unordered.Diagnostics).Code);
    }

    [Fact]
    public void Legacy_info_blocks_that_tile_become_dated_legacy_blocks()
    {
        var family = ContractCatalogue.Family("R010");
        var data = new WorkbookSheet("Data", 1, family.Headers,
            Enumerable.Range(2, 4).Select(row => new WorkbookRow(row, [new("WLMHW"), new($"I{row}"), new(null), new("1")])).ToArray());
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, new string('f', 64), [data]);
        var table = new LegacyInfoTable(true, true,
        [
            new LegacyInfoBlock(4, 5, "202608071300_BinWise Stock.xlsx", Time(8, 7)) { RowsRetained = 2 },
            new LegacyInfoBlock(2, 3, "202607021200_BinWise Stock.xlsx", Time(7, 2)) { RowsRetained = 2 }
        ], []);

        var source = TestSources.Reader(new StubLegacyInfoReader(table)).Describe(new SourceDescriptionRequest(workbook, family, data));

        Assert.Equal(SourceKind.ConsolidatedLegacy, source.Kind);
        Assert.Equal([(1, 2, 3), (2, 4, 5)], source.Blocks.Select(block => (block.BlockNo, block.FirstRow!.Value, block.LastRow!.Value)));
        Assert.All(source.Blocks, block => Assert.Equal(BlockCompleteness.Legacy, block.Completeness));
        Assert.All(source.Blocks, block => Assert.Equal(BlockOrigin.InfoLegacy, block.Origin));
        Assert.Equal([new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7)], source.Blocks.Select(block => block.SnapshotDate!.Value));
        Assert.All(source.Blocks, block => Assert.Equal(SnapshotDateBasis.InfoBlock, block.SnapshotDateBasis));
    }

    [Fact]
    public void Legacy_info_blocks_that_do_not_tile_give_one_whole_file_block()
    {
        var family = ContractCatalogue.Family("R025");
        var data = new WorkbookSheet("Data", 1, family.Headers,
            Enumerable.Range(2, 3).Select(row => new WorkbookRow(row, [new("INV")])).ToArray());
        var workbook = new WorkbookSnapshot("R025.xlsx", 1, new string('f', 64), [data]);
        var unusable = new ImportDiagnostic(ImportCodes.InfoBlocksUnusable, ImportDiagnosticSeverity.Warning, "Info blocks do not tile.");
        var table = new LegacyInfoTable(true, false, [], [unusable]);

        var source = TestSources.Reader(new StubLegacyInfoReader(table)).Describe(new SourceDescriptionRequest(workbook, family, data));

        var block = Assert.Single(source.Blocks);
        Assert.Equal((BlockOrigin.WholeFile, BlockCompleteness.Legacy, 2, 4), (block.Origin, block.Completeness, block.FirstRow!.Value, block.LastRow!.Value));
        Assert.False(block.ExportTime.IsKnown);
        Assert.Equal([unusable], source.Diagnostics);
    }

    [Fact]
    public void Snapshot_history_without_a_contract_is_grouped_into_dated_blocks()
    {
        var book = ContractFixture.Load("s009-current-history.json").Workbook;
        book.Sheets.Remove(book.Sheet("Info"));
        book.Sheet("Snapshot History").Rows.Add(["AW330", "J-0009", "OPEN", "2", "2026-09-01", "PENDING REPAIR 01.09.2026.csv"]);
        var workbook = book.ToSnapshot();
        var family = ContractCatalogue.Family("S009");

        var source = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));

        Assert.Equal(SourceKind.ConsolidatedLegacy, source.Kind);
        Assert.Equal([BlockOrigin.WholeFile, BlockOrigin.HistoryRows, BlockOrigin.HistoryRows], source.Blocks.Select(block => block.Origin));
        Assert.Equal([new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 1)], source.Blocks.Skip(1).Select(block => block.SnapshotDate!.Value));
        Assert.Equal([ExportTime.OnDate(new DateOnly(2026, 8, 7)), ExportTime.OnDate(new DateOnly(2026, 9, 1))], source.Blocks.Skip(1).Select(block => block.ExportTime));
        var staging = HistorySheetBlockReader.StagingSheet(workbook.Sheets.Single(sheet => sheet.Name == "Snapshot History"));
        Assert.Equal(family.Headers, staging.Headers);
        Assert.All(staging.Rows, row => Assert.Equal(4, row.Cells.Count));
    }

    [Fact]
    public void Snapshot_history_rows_of_one_snapshot_split_by_other_rows_are_refused()
    {
        var book = ContractFixture.Load("s009-current-history.json").Workbook;
        book.Sheets.Remove(book.Sheet("Info"));
        book.Sheet("Snapshot History").Rows.Insert(2, ["AW330", "J-0009", "OPEN", "2", "2026-09-01", "PENDING REPAIR 01.09.2026.csv"]);
        var history = book.ToSnapshot().Sheets.Single(sheet => sheet.Name == "Snapshot History");

        var read = HistorySheetBlockReader.Read(history, 2);

        Assert.Equal(3, read.Blocks.Count);
        var split = Assert.Single(read.Diagnostics);
        Assert.Equal(ImportCodes.SnapshotDateAmbiguous, split.Code);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, split.Severity);
        Assert.Equal(4, split.RowNumber);
    }

    [Fact]
    public void Reviewed_tick_comes_before_a_bare_snapshot_history_sheet()
    {
        var book = ContractFixture.Load("s009-current-history.json").Workbook;
        book.Sheets.Remove(book.Sheet("Info"));
        var workbook = book.ToSnapshot();
        var data = workbook.Sheets.Single(sheet => sheet.Name == "Data");

        var reviewed = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, ContractCatalogue.Family("S009"), data) { ImportAsReviewed = true });

        Assert.Equal(SourceKind.Reviewed, reviewed.Kind);
        Assert.Equal(BlockCompleteness.Complete, Assert.Single(reviewed.Blocks).Completeness);
    }

    [Fact]
    public void Raw_and_reviewed_sources_are_one_complete_block()
    {
        var family = ContractCatalogue.Family("R025");
        var raw = ContractFixture.Load("r025-transactional-delta.json").Raw[1];
        raw.SourcePath = @"C:\Imports\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\202608071858_SDB-VariantwiseSales.xlsx";
        var workbook = raw.ToSnapshot();
        var reader = TestSources.Reader();

        var described = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets[0]));
        var reviewed = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets[0]) { ImportAsReviewed = true });
        raw.SourcePath = null;
        var observed = reader.Describe(new SourceDescriptionRequest(raw.ToSnapshot(), family, workbook.Sheets[0]));

        var block = Assert.Single(described.Blocks);
        Assert.Equal(SourceKind.Raw, described.Kind);
        Assert.Equal((BlockCompleteness.Complete, BlockOrigin.Raw, 2, 5, 4), (block.Completeness, block.Origin, block.FirstRow!.Value, block.LastRow!.Value, block.RowCount));
        Assert.Equal(ExportTime.AtMinute(new DateTime(2026, 8, 7, 18, 58, 0)), block.ExportTime);
        Assert.Equal(raw.Sha256, block.SourceSha256);
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 25), PeriodBasis.Declared), (block.PeriodFrom!.Value, block.PeriodTo!.Value, block.PeriodBasis));
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 6), PeriodBasis.Observed),
            (observed.Blocks[0].PeriodFrom!.Value, observed.Blocks[0].PeriodTo!.Value, observed.Blocks[0].PeriodBasis));
        var reviewedBlock = Assert.Single(reviewed.Blocks);
        Assert.Equal(SourceKind.Reviewed, reviewed.Kind);
        Assert.Equal(BlockCompleteness.Complete, reviewedBlock.Completeness);
        Assert.False(reviewedBlock.ExportTime.IsKnown);
    }

    [Theory]
    [InlineData("TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026", "2026-07-01", "2026-08-25")]
    [InlineData("pack 01 SEP 2024 TO 30 NOV 2024", "2024-09-01", "2024-11-30")]
    [InlineData("till 29 sep 2026", null, null)]
    [InlineData("31 FEB 2026 TO 01 MAR 2026", null, null)]
    public void Declared_period_comes_from_a_pack_name(string name, string? from, string? to)
    {
        var period = DeclaredPeriod.Parse(name);

        Assert.Equal(from is null ? ((DateOnly, DateOnly)?)null : (DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(to!, System.Globalization.CultureInfo.InvariantCulture)), period);
    }

    private static ExportTime Time(int month, int day) => ExportTime.AtMinute(new DateTime(2026, month, day, 12, 0, 0));

    private static ExportTime Late(int month, int day) => ExportTime.AtMinute(new DateTime(2026, month, day, 18, 0, 0));

    private static SourceBlock Block(int blockNo, ExportTime time) =>
        new(blockNo, "Data", blockNo * 10, blockNo * 10 + 5, 6, BlockCompleteness.Legacy, BlockOrigin.InfoLegacy, time);

    /// <summary>One block's observation of <see cref="Invoice"/>: each product line the given number of times.</summary>
    private static DocumentObservation Observation(int blockNo, ExportTime time, params (string Product, int Count)[] lines)
    {
        var family = ContractCatalogue.Family("R025");
        var row = blockNo * 100;
        var rows = lines.SelectMany(line => Enumerable.Repeat(line.Product, line.Count)).Select(product => new SourceRow(
            new RowLocator(blockNo, "Data", row++),
            new Dictionary<string, object?>
            {
                ["source_transaction_type"] = "INV", ["store_code"] = "WLMHW", ["invoice_number"] = "100001",
                ["transaction_date"] = new DateOnly(2026, 7, 1), ["product_code"] = product, ["source_quantity"] = 1m,
                ["source_net_value"] = 100m, ["source_store_timestamp"] = $"2026-0{blockNo}-01 10:00:00"
            })).ToArray();
        return TestSources.Observation(family, Invoice, Block(blockNo, time), rows);
    }
}
