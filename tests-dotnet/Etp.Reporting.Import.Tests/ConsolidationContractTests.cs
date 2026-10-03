using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Codes = Etp.Reporting.Application.Imports.ImportCodes.Contract;

namespace Etp.Reporting.Import.Tests;

/// <summary>Consolidation contract v1 (CONSOLIDATION-CONTRACT.md sections 3-8; spec 6.5): the validator, virtual rows and rebuild.</summary>
public sealed class ConsolidationContractTests
{
    private const string R025 = "r025-transactional-delta.json";
    private const string R010 = "r010-snapshot-stacked.json";
    private const string S009 = "s009-current-history.json";
    private const string R011 = "r011-snapshot-dated-rows.json";

    [Fact]
    public void Valid_workbook_round_trips_into_blocks_and_virtual_rows()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();

        var source = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));

        Assert.Equal(SourceKind.Consolidated, source.Kind);
        Assert.Equal(1, source.ContractVersion);
        Assert.False(source.HasBlockers, Describe(source.Diagnostics));
        Assert.Equal([1, 2, 3], source.Blocks.Select(block => block.BlockNo));
        Assert.All(source.Blocks, block => Assert.Equal(BlockOrigin.Contract, block.Origin));
        // Every block reads back as the contract wrote it.
        foreach (var block in source.Blocks)
        {
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "first_row"), block.FirstRow!.Value.ToString());
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "last_row"), block.LastRow!.Value.ToString());
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "source_file"), block.SourceFileName);
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "source_sha256"), block.SourceSha256);
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "export_time"), block.ExportTime.ToContractText());
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "completeness"), block.Completeness.ToContractText());
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "period_basis"), block.PeriodBasis.ToContractText());
            Assert.Equal(fixture.Workbook.Block(block.BlockNo, "raw_rows"), block.RawRows!.Value.ToString());
        }
        Assert.Equal([0, 2, 2], source.Blocks.Select(block => block.VirtualRowCount));
        Assert.Equal(
            [new VirtualRow(2, 2, "Data", 4), new VirtualRow(2, 3, "Data", 5), new VirtualRow(3, 4, "Data", 2), new VirtualRow(3, 5, "Data", 4)],
            source.VirtualRows);
        Assert.All(source.VirtualRows, copy => Assert.Equal(new RowLocator(copy.BlockNo, "ETP_Excluded", copy.MapRow, true), copy.Locator));
    }

    [Fact]
    public void Valid_snapshot_and_current_workbooks_have_no_blockers()
    {
        var r010 = Validate(ContractFixture.Load(R010).Workbook, "R010");
        var s009 = Validate(ContractFixture.Load(S009).Workbook, "S009");

        Assert.False(r010.HasBlockers, Describe(r010.Diagnostics));
        Assert.Equal([new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 29)], r010.Blocks.Select(block => block.SnapshotDate!.Value));
        Assert.All(r010.Blocks, block => Assert.Equal(SnapshotDateBasis.Contract, block.SnapshotDateBasis));
        Assert.Empty(r010.Diagnostics);

        Assert.False(s009.HasBlockers, Describe(s009.Diagnostics));
        Assert.Equal(["Snapshot History", "Data"], s009.Blocks.Select(block => block.SheetName));
        Assert.Equal(ExportBasis.Date, s009.Blocks[0].ExportTime.Basis);
    }

    /// <summary>One change to a valid workbook for each blocker of contract section 8.</summary>
    public static TheoryData<string, string> Blockers => new()
    {
        { "unreadable block number", Codes.Unreadable },
        { "unreadable header row", Codes.Unreadable },
        { "version 2", Codes.VersionUnsupported },
        { "builder key missing", Codes.KeyMissing },
        { "family code differs", Codes.FamilyMismatch },
        { "store code differs", Codes.StoreMismatch },
        { "rule snapshot for R025", Codes.RuleInvalid },
        { "excluded sheet missing", Codes.SheetMissing },
        { "SourceFile column on Data", Codes.ExtraColumns },
        { "data_rows wrong", Codes.RowCountMismatch },
        { "block table header renamed", Codes.BlockTableHeader },
        { "gap", Codes.BlockGap },
        { "overlap", Codes.BlockOverlap },
        { "outside data", Codes.BlockOutsideData },
        { "arithmetic", Codes.BlockArithmetic },
        { "sha upper case", Codes.ShaInvalid },
        { "export time contradicts name", Codes.ExportTimeInvalid },
        { "export time blank", Codes.ExportTimeMissing },
        { "period missing", Codes.PeriodMissing },
        { "R010 snapshot date missing", Codes.SnapshotDateMissing },
        { "R010 snapshot date duplicate", Codes.SnapshotDateDuplicate },
        { "S009 history block of another export shares the Data date", Codes.SnapshotDateDuplicate },
        { "R010 blocks out of block-number order", Codes.BlockOverlap },
        { "S009 Snapshot_As_Of disagrees", Codes.SnapshotDateDisagrees },
        { "S009 two Data blocks", Codes.CurrentShape },
        { "map row missing", Codes.ExcludedMapMissing },
        { "twin in own block", Codes.ExcludedUnresolved },
        { "map names unknown block", Codes.ExcludedUnresolved },
        { "twin reused", Codes.ExcludedTwinReused },
        { "duplicate sha", Codes.DuplicateExport },
        { "duplicate file and time", Codes.DuplicateExport },
    };

    [Theory]
    [MemberData(nameof(Blockers))]
    public void Each_blocker_refuses_the_workbook(string change, string code)
    {
        var (book, familyCode) = Changed(change);

        var result = Validate(book, familyCode);

        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, diagnostic.Severity);
        Assert.Equal(ImportIssueSeverity.Blocker, ImportCodes.DefaultSeverity(code));
        Assert.True(result.HasBlockers);
        Assert.DoesNotContain("100001", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown key", Codes.KeyUnknown, ImportDiagnosticSeverity.Information)]
    [InlineData("legacy block", Codes.LegacyBlocks, ImportDiagnosticSeverity.Information)]
    [InlineData("row outside period", Codes.RowOutsidePeriod, ImportDiagnosticSeverity.Warning)]
    [InlineData("S009 history repeats data", Codes.HistoryRepeatsData, ImportDiagnosticSeverity.Warning)]
    [InlineData("date-typed key cell", Codes.CellNotText, ImportDiagnosticSeverity.Warning)]
    public void Warnings_and_information_do_not_refuse(string change, string code, ImportDiagnosticSeverity severity)
    {
        var (book, familyCode) = Changed(change);

        var result = Validate(book, familyCode);

        Assert.Equal(severity, Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code).Severity);
        Assert.False(result.HasBlockers, Describe(result.Diagnostics));
    }

    [Fact]
    public void History_block_that_repeats_data_is_counted_once()
    {
        var (book, _) = Changed("S009 history repeats data");

        var result = Validate(book, "S009");

        Assert.Equal([2], result.Blocks.Select(block => block.BlockNo));
    }

    [Fact]
    public void Every_staged_row_is_in_one_block_or_a_skipped_block()
    {
        // The repeated history block is not a block, but its rows stay on Snapshot History: the description says so,
        // so an integrator landing every staged row knows each one's block or that it is counted from the Data block.
        var (book, _) = Changed("S009 history repeats data");
        var workbook = book.ToSnapshot();
        var family = ContractCatalogue.Family("S009");

        var source = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var staged = TestSources.Stage(workbook, family, "Data", "Snapshot History");

        Assert.False(source.HasBlockers, Describe(source.Diagnostics));
        var skipped = Assert.Single(source.SkippedBlocks);
        Assert.Equal((1, ConsolidationContractLayout.HistorySheet, Codes.HistoryRepeatsData, 2),
            (skipped.BlockNo, skipped.SheetName, skipped.Code, skipped.AttestedByBlockNo));
        Assert.Contains(staged, row => row.Locator.SheetName == ConsolidationContractLayout.HistorySheet);
        Assert.All(staged, row => Assert.Equal(1,
            source.Blocks.Count(block => block.Contains(row.Locator.SheetName, row.Locator.SourceRowNumber)) +
            source.SkippedBlocks.Count(block => block.Contains(row.Locator.SheetName, row.Locator.SourceRowNumber))));
        Assert.Same(skipped, source.SkippedBlockOf(ConsolidationContractLayout.HistorySheet, skipped.FirstRow));
    }

    [Fact]
    public void A_snapshot_block_never_passes_on_a_period()
    {
        // Contract 3.3: period_from and period_to are blank for snapshot blocks. A pack period written there anyway is
        // not what the snapshot read, so the block carries none and the absence check never treats it as coverage.
        var book = ContractFixture.Load(R010).Workbook;
        book.SetBlock(1, "period_from", "2026-07-01");
        book.SetBlock(1, "period_to", "2026-08-25");

        var result = Validate(book, "R010");

        Assert.False(result.HasBlockers, Describe(result.Diagnostics));
        Assert.All(result.Blocks, block => Assert.Equal(((DateOnly?)null, (DateOnly?)null, PeriodBasis.None), (block.PeriodFrom, block.PeriodTo, block.PeriodBasis)));
    }

    [Fact]
    public void Another_export_with_the_Data_date_on_history_is_not_a_repeat()
    {
        var (book, _) = Changed("S009 history block of another export shares the Data date");

        var result = Validate(book, "S009");

        // Contract 5 counts only the same export once; a different export of the same date breaks rule 9 and stays a block.
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Codes.HistoryRepeatsData);
        Assert.Equal([1, 2], result.Blocks.Select(block => block.BlockNo));
        Assert.True(result.HasBlockers);
    }

    [Theory]
    [InlineData("R010 Data sheet missing")]
    [InlineData("S009 history sheet missing")]
    [InlineData("S009 data_sheet names another sheet")]
    public void A_missing_data_or_history_sheet_refuses_the_workbook(string change)
    {
        var (book, familyCode) = Changed(change);

        var result = Validate(book, familyCode);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == Codes.SheetMissing && diagnostic.Severity == ImportDiagnosticSeverity.Blocker);
        Assert.True(result.HasBlockers);
    }

    [Fact]
    public void Legacy_block_with_an_excluded_count_needs_no_map()
    {
        var book = ContractFixture.Load(R010).Workbook;
        // A pre-contract block carried over with today's Info "Rows excluded" count: no map, checked for row_count only.
        book.SetBlock(1, "completeness", "legacy");
        book.SetBlock(1, "source_sha256", "");
        book.SetBlock(1, "raw_rows", "");
        book.SetBlock(1, "excluded_rows", "3");

        var result = Validate(book, "R010");

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Codes.KeyMissing);
        Assert.False(result.HasBlockers, Describe(result.Diagnostics));
    }

    [Fact]
    public void R011_rows_are_checked_against_the_production_catalogue_and_their_Date_column()
    {
        var family = EtpReportFamilyRegistry.Families.Single(candidate => candidate.FamilyCode == "R011");
        var book = ContractFixture.Load(R011).Workbook;

        var valid = Validate(book, family);
        book.Sheet("Data").Rows[3][7] = "2026-09-29";
        var disagrees = Validate(book, family);

        Assert.False(valid.HasBlockers, Describe(valid.Diagnostics));
        Assert.Equal([new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30)], valid.Blocks.Select(block => block.SnapshotDate!.Value));
        var diagnostic = Assert.Single(disagrees.Diagnostics, candidate => candidate.Code == Codes.SnapshotDateDisagrees);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, diagnostic.Severity);
        Assert.Equal((2, 4, "Date"), (diagnostic.BlockNo!.Value, diagnostic.RowNumber!.Value, diagnostic.ColumnName));
    }

    [Fact]
    public void Production_catalogue_allows_the_contract_rules_of_section_6()
    {
        ContractRule[] Allowed(string code) =>
            [.. ConsolidationContractValidator.AllowedRules(EtpReportFamilyRegistry.Families.Single(family => family.FamilyCode == code))];

        Assert.Equal([ContractRule.Transactional, ContractRule.Empty], Allowed("R025"));
        Assert.Equal([ContractRule.Snapshot, ContractRule.Empty], Allowed("R010"));
        Assert.Equal([ContractRule.Snapshot, ContractRule.Empty], Allowed("R011"));
    }

    [Fact]
    public void Unreadable_contract_returned_by_the_reader_is_refused()
    {
        var book = ContractFixture.Load(R025).Workbook;
        var info = book.Sheet("Info").Rows;
        info.RemoveRange(info.FindIndex(row => row.Count == 0), info.Count - info.FindIndex(row => row.Count == 0));
        var workbook = book.ToSnapshot();

        var source = TestSources.Reader().Describe(new SourceDescriptionRequest(workbook, ContractCatalogue.Family("R025"), workbook.Sheets[1]));

        Assert.Equal(SourceKind.Consolidated, source.Kind);
        Assert.Contains(source.Diagnostics, diagnostic => diagnostic.Code == Codes.Unreadable);
        Assert.True(source.HasBlockers);
        Assert.Empty(source.Blocks);
    }

    [Fact]
    public void Excluded_twin_reused_is_refused()
    {
        var book = ContractFixture.Load(R025).Workbook;
        // Block 2 left out two copies of one line; both map rows now point at the same physical twin (row 4).
        book.Sheet("ETP_Excluded").Rows[2][2] = "4";

        var result = Validate(book, "R025");

        var reused = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Codes.ExcludedTwinReused);
        Assert.Equal(2, reused.BlockNo);
        Assert.Equal(3, reused.RowNumber);
        Assert.True(result.HasBlockers);
        // Twins may repeat across blocks: block 3 also uses row 4 and is not reported.
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Codes.ExcludedTwinReused && diagnostic.BlockNo == 3);
    }

    [Fact]
    public void Virtual_rows_rebuild_raw_export_hash()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();
        var reader = TestSources.Reader();
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var staged = TestSources.Stage(workbook, family, "Data");

        foreach (var raw in fixture.Raw)
        {
            var block = source.Blocks.Single(candidate => candidate.SourceSha256 == raw.Sha256);
            var rebuilt = reader.RebuildBlockRows(source, block, staged);
            var rawWorkbook = raw.ToSnapshot();
            var rawSource = reader.Describe(new SourceDescriptionRequest(rawWorkbook, family, rawWorkbook.Sheets[0]));
            var rawBlock = Assert.Single(rawSource.Blocks);
            var rawRows = reader.RebuildBlockRows(rawSource, rawBlock, TestSources.Stage(rawWorkbook, family, rawWorkbook.Sheets[0].Name));

            Assert.Equal(SourceKind.Raw, rawSource.Kind);
            Assert.Equal(rawRows.Count, rebuilt.Count);
            Assert.Equal(block.RowCount + block.VirtualRowCount, rebuilt.Count);
            Assert.Equal(block.ExportTime, rawBlock.ExportTime);
            Assert.NotNull(ExportIdentity.ContentSha256(family, rawBlock, rawRows, TestSources.Canonicalizer));
            // The same export reached by either route has the same content hash and the same export key.
            Assert.Equal(ExportIdentity.ContentSha256(family, rawBlock, rawRows, TestSources.Canonicalizer),
                ExportIdentity.ContentSha256(family, block, rebuilt, TestSources.Canonicalizer));
            Assert.Equal(ExportIdentity.NormalisedReportName(rawBlock.SourceFileName), ExportIdentity.NormalisedReportName(block.SourceFileName));
        }

        var delta = source.Blocks.Single(block => block.BlockNo == 2);
        var rows = reader.RebuildBlockRows(source, delta, staged);
        Assert.Equal([6, 7, 2, 3], rows.Select(row => row.Locator.SourceRowNumber));
        Assert.Equal(["Data", "Data", "ETP_Excluded", "ETP_Excluded"], rows.Select(row => row.Locator.SheetName));
        Assert.All(rows, row => Assert.Equal(2, row.Locator.BlockNo));
        // Each virtual row carries its own twin's values (timestamps included): two distinct twins, rows 4 and 5.
        Assert.Equal(staged.Single(row => row.Locator.SourceRowNumber == 4).Values, rows[2].Values);
        Assert.Equal(staged.Single(row => row.Locator.SourceRowNumber == 5).Values, rows[3].Values);
    }

    [Fact]
    public void Rebuild_check_against_raw_exports_passes_and_finds_a_changed_export()
    {
        var fixture = ContractFixture.Load(R025);

        var clean = Validate(fixture.Workbook, "R025", fixture.RawBySha());
        fixture.Raw[1].Sheets[0].Rows[3][5] = "2";
        var changed = Validate(fixture.Workbook, "R025", fixture.RawBySha());

        Assert.DoesNotContain(clean.Diagnostics, diagnostic => diagnostic.Code == Codes.BlockRebuildMismatch);
        var mismatch = Assert.Single(changed.Diagnostics, diagnostic => diagnostic.Code == Codes.BlockRebuildMismatch);
        Assert.Equal(2, mismatch.BlockNo);
        Assert.Equal(2, mismatch.Occurrences);
    }

    [Fact]
    public void Trimmed_block_rebuild_includes_mapped_copies()
    {
        var fixture = ContractFixture.Load(R025);
        var book = fixture.Workbook;
        // A later build removed row 3 (invoice 100001, line P2) from block 1, the 2 Jul export, because the 20 Jul export
        // (block 3) re-stated invoice 100001 without it (contract rule 7: never from the latest block holding it). The
        // rows below move up one, so the map's twins move with them.
        book.Sheet("Data").Rows.RemoveAt(2);
        book.SetKey("data_rows", "6");
        book.SetBlock(1, "last_row", "4");
        book.SetBlock(1, "row_count", "3");
        book.SetBlock(1, "superseded_rows", "1");
        book.SetBlock(1, "completeness", "trimmed");
        book.SetBlock(2, "first_row", "5");
        book.SetBlock(2, "last_row", "6");
        book.SetBlock(3, "first_row", "7");
        book.SetBlock(3, "last_row", "7");
        var map = book.Sheet("ETP_Excluded").Rows;
        (map[1][2], map[2][2], map[3][2], map[4][2]) = ("3", "4", "2", "3");
        var family = ContractCatalogue.Family("R025");
        var workbook = book.ToSnapshot();
        var reader = TestSources.Reader();

        var validation = Validate(book, "R025", fixture.RawBySha());
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var staged = TestSources.Stage(workbook, family, "Data");
        var projections = source.Blocks.Select(block => TestSources.Project(family, "WLMHW", block, reader.RebuildBlockRows(source, block, staged))).ToArray();
        var trimmed = source.Blocks.Single(block => block.BlockNo == 1);
        var delta = source.Blocks.Single(block => block.BlockNo == 2);
        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);
        var coverage = InSourceResolver.CoverageBlocks(source, projections, family.Identity!.Scope);

        Assert.False(validation.HasBlockers, Describe(validation.Diagnostics));
        Assert.Equal(BlockCompleteness.Trimmed, trimmed.Completeness);
        Assert.False(trimmed.IsRebuildable);
        var trimmedRows = reader.RebuildBlockRows(source, trimmed, staged);
        Assert.Equal(3, trimmedRows.Count);
        Assert.Null(ExportIdentity.ContentSha256(family, trimmed, trimmedRows, TestSources.Canonicalizer));
        // The delta block's mapped copies still find their (moved) twins in the trimmed block, and it rebuilds its export.
        var deltaRows = reader.RebuildBlockRows(source, delta, staged);
        Assert.Equal(2, deltaRows.Count(row => row.Locator.IsVirtual));
        Assert.NotNull(ExportIdentity.ContentSha256(family, delta, deltaRows, TestSources.Canonicalizer));
        // The trimmed block is marked trimmed for the absence check, which uses only complete and delta blocks...
        Assert.Equal(BlockCompleteness.Trimmed, coverage.Single(block => block.BlockNo == 1).Completeness);
        Assert.All(coverage.Where(block => block.BlockNo != 1), block => Assert.NotEqual(BlockCompleteness.Trimmed, block.Completeness));
        // ...but is a normal observation of the documents it holds: invoice 100002 (both lines, kept in block 1) is
        // attested by the 7 Aug export, and its reading of 100001 (P1 only) is attested by the 20 Jul export.
        var invoice = resolution.Authoritative.Single(observation => observation.Key.KeyText == "2027|100002");
        Assert.Equal(2, invoice.BlockNo);
        Assert.Contains(new InSourceDecision(invoice.Key, 1, DocumentDecision.AttestedInSource), resolution.EarlierBlocks);
        var restated = resolution.Authoritative.Single(observation => observation.Key.KeyText == "2027|100001");
        Assert.Equal(3, restated.BlockNo);
        Assert.Contains(new InSourceDecision(restated.Key, 1, DocumentDecision.AttestedInSource), resolution.EarlierBlocks);
    }

    [Fact]
    public void Rebuild_without_every_twin_has_no_content_hash()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();
        var reader = TestSources.Reader();
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var delta = source.Blocks.Single(block => block.BlockNo == 2);
        // Only the block's own rows: its twins (rows 4 and 5, in block 1) are not among them.
        var ownRows = TestSources.Stage(workbook, family, "Data").Where(row => delta.Contains(row.Locator.SheetName, row.Locator.SourceRowNumber)).ToArray();

        var rows = reader.RebuildBlockRows(source, delta, ownRows);

        Assert.Equal(2, rows.Count);
        Assert.Null(ExportIdentity.ContentSha256(family, delta, rows, TestSources.Canonicalizer));
    }

    [Fact]
    public void Block_numbers_are_append_order_and_time_decides()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();
        var reader = TestSources.Reader();
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var staged = TestSources.Stage(workbook, family, "Data");
        var projections = source.Blocks.Select(block => TestSources.Project(family, "WLMHW", block, reader.RebuildBlockRows(source, block, staged))).ToArray();

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(source, projections);

        // Block 3 was appended last but is an older export: allowed, and reported as information only.
        var order = Assert.Single(source.Diagnostics, diagnostic => diagnostic.Code == Codes.BlocksNotInTimeOrder);
        Assert.Equal(ImportDiagnosticSeverity.Information, order.Severity);
        // Invoice 100002: block 1 (2 Jul) and block 2 (7 Aug) hold both lines; block 3 (20 Jul) one. The 7 Aug export
        // wins although block 3 has the higher number, and block 3's reading is a restatement by an older export.
        var invoice = resolution.Authoritative.Single(observation => observation.Key.KeyText == "2027|100002");
        Assert.Equal(2, invoice.BlockNo);
        Assert.Equal(2, invoice.RowCount);
        Assert.All(invoice.Rows, row => Assert.True(row.Source.IsVirtual));
        Assert.Contains(new InSourceDecision(invoice.Key, 1, DocumentDecision.AttestedInSource), resolution.EarlierBlocks);
        Assert.Contains(new InSourceDecision(invoice.Key, 3, DocumentDecision.RestatedInSource), resolution.EarlierBlocks);
        Assert.All(invoice.SetAside, row => Assert.Equal(RowDisposition.OlderBlock, row.Disposition));
        // Invoice 100001: the 20 Jul export (block 3) is newer than the 2 Jul export (block 1) and restates it.
        var first = resolution.Authoritative.Single(observation => observation.Key.KeyText == "2027|100001");
        Assert.Equal(3, first.BlockNo);
        Assert.Null(first.HoldCode);
        Assert.Empty(resolution.Diagnostics);
    }

    [Fact]
    public void Same_time_blocks_that_differ_are_held_in_source()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();
        var reader = TestSources.Reader();
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var sameTime = source with
        {
            Blocks = source.Blocks.Select(block => block.BlockNo == 3 ? block with { ExportTime = source.Blocks[1].ExportTime } : block).ToArray()
        };
        var staged = TestSources.Stage(workbook, family, "Data");
        var projections = sameTime.Blocks.Select(block => TestSources.Project(family, "WLMHW", block, reader.RebuildBlockRows(sameTime, block, staged))).ToArray();

        var resolution = new InSourceResolver(TestSources.Canonicalizer).Resolve(sameTime, projections);

        var invoice = resolution.Authoritative.Single(observation => observation.Key.KeyText == "2027|100002");
        Assert.Equal(ImportCodes.InSourceConflict, invoice.HoldCode);
        Assert.Contains(new InSourceDecision(invoice.Key, 2, DocumentDecision.InSourceConflict), resolution.EarlierBlocks);
        var held = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ImportCodes.InSourceConflict, held.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, held.Severity);
    }

    [Fact]
    public void Coverage_blocks_feed_the_absence_check()
    {
        var fixture = ContractFixture.Load(R025);
        var family = ContractCatalogue.Family("R025");
        var workbook = fixture.Workbook.ToSnapshot();
        var reader = TestSources.Reader();
        var source = reader.Describe(new SourceDescriptionRequest(workbook, family, workbook.Sheets.Single(sheet => sheet.Name == "Data")));
        var staged = TestSources.Stage(workbook, family, "Data");
        var projections = source.Blocks.Select(block => TestSources.Project(family, "WLMHW", block, reader.RebuildBlockRows(source, block, staged))).ToArray();

        var coverage = InSourceResolver.CoverageBlocks(source, projections, family.Identity!.Scope);

        var latest = coverage.Single(block => block.BlockNo == 2);
        Assert.True(latest.IsIncoming);
        Assert.True(latest.Covers(new DateOnly(2026, 7, 1)));
        // Invoice 100001 is in the period of the 7 Aug export but not in it: the decision engine's absence rule asks the Owner.
        Assert.False(latest.Observed(DocumentKey.ForDocument("R025", "WLMHW", new DateOnly(2026, 7, 1), "100001")));
        Assert.True(latest.Observed(DocumentKey.ForDocument("R025", "WLMHW", new DateOnly(2026, 7, 1), "100002")));
    }

    [Fact]
    public void Export_key_is_the_same_for_both_routes_and_needs_a_known_time()
    {
        var contractBlock = new SourceBlock(4, "Data", 2, 3, 2, BlockCompleteness.Complete, BlockOrigin.Contract,
            ExportTime.AtMinute(new DateTime(2026, 9, 29, 14, 49, 0)))
        {
            SourceFileName = "202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx",
            PeriodFrom = new DateOnly(2026, 7, 1),
            PeriodTo = new DateOnly(2026, 9, 29)
        };
        var rawBlock = contractBlock with { BlockNo = 1, Origin = BlockOrigin.Raw };
        var otherSelection = contractBlock with { PeriodFrom = new DateOnly(2026, 9, 1) };

        Assert.Equal(ExportIdentity.ExportKey("R025", "wlmhw", contractBlock), ExportIdentity.ExportKey("r025", "WLMHW", rawBlock));
        Assert.NotEqual(ExportIdentity.ExportKey("R025", "WLMHW", contractBlock), ExportIdentity.ExportKey("R025", "WLMHW", otherSelection));
        Assert.Null(ExportIdentity.ExportKey("R025", "WLMHW", contractBlock with { ExportTime = ExportTime.Unknown }));
        Assert.Equal("SDBVARIANTWISESALESSDBVARIANTWISESALES", ExportIdentity.NormalisedReportName(contractBlock.SourceFileName));
        Assert.Equal("PENDINGREPAIR", ExportIdentity.NormalisedReportName("PENDING REPAIR 29.09.2026.csv"));
        Assert.Equal("CLOSINGSTOCK", ExportIdentity.NormalisedReportName("ClosingStock_20260901133606.csv"));
    }

    [Theory]
    [InlineData("a", 1, "a", 1, ExportContentMatch.SameContent)]
    [InlineData("a", 1, "b", 1, ExportContentMatch.ContentMismatch)]
    [InlineData("a", 1, "b", 2, ExportContentMatch.NotComparable)]
    [InlineData(null, 1, "b", 1, ExportContentMatch.NotComparable)]
    public void Content_is_compared_only_under_the_same_hash_version(string? incoming, int incomingVersion, string? stored, int storedVersion, ExportContentMatch expected)
    {
        var block = new SourceBlock(1, "Data", 2, 2, 1, BlockCompleteness.Complete, BlockOrigin.Raw, ExportTime.Unknown) { SourceSha256 = new string('1', 64) };

        var match = ExportIdentity.Compare(block, new BlockIdentity(incoming, incomingVersion, null, 1), new string('1', 64),
            new BlockIdentity(stored, storedVersion, null, 1));

        Assert.Equal(expected, match);
        Assert.Equal(ExportContentMatch.DifferentExport, ExportIdentity.Compare(block, new BlockIdentity("a", 1, null, 1), new string('2', 64),
            new BlockIdentity("a", 1, null, 1)));
    }

    [Fact]
    public void Schema_describes_the_block_table_and_map_of_this_release()
    {
        var root = Directory.GetParent(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "docs", "schemas", "etp-consolidation-contract-v1.schema.json")));
        var definitions = schema.RootElement.GetProperty("$defs");

        var blockColumns = definitions.GetProperty("block").GetProperty("properties").EnumerateObject().Select(property => property.Name);
        var mapColumns = definitions.GetProperty("excludedRow").GetProperty("properties").EnumerateObject().Select(property => property.Name);
        var keys = definitions.GetProperty("header").GetProperty("properties").EnumerateObject().Select(property => property.Name);
        var required = definitions.GetProperty("header").GetProperty("required").EnumerateArray().Select(key => key.GetString());

        Assert.Equal(ConsolidationContractLayout.BlockTableColumns, blockColumns);
        Assert.Equal(ConsolidationContractLayout.ExcludedColumns, mapColumns);
        Assert.Equal(ContractKeys.Known.Order(), keys.Order());
        Assert.Equal(ContractKeys.Required.Order(), required.Order());
    }

    private static ContractValidationResult Validate(FixtureBook book, string familyCode,
        IReadOnlyDictionary<string, Workbooks.WorkbookSnapshot>? raw = null) =>
        Validate(book, ContractCatalogue.Family(familyCode), raw);

    private static ContractValidationResult Validate(FixtureBook book, EtpReportFamily family,
        IReadOnlyDictionary<string, Workbooks.WorkbookSnapshot>? raw = null)
    {
        var workbook = book.ToSnapshot();
        var read = new TestContractReader().Read(workbook);
        Assert.True(read.IsContract);
        Assert.NotNull(read.Contract);
        return new ConsolidationContractValidator().Validate(new ContractValidationRequest(workbook, read.Contract, family)
        {
            RawExports = raw ?? new Dictionary<string, Workbooks.WorkbookSnapshot>()
        });
    }

    private static string Describe(IEnumerable<ImportDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    /// <summary>A fixture changed in one way; the family code is the fixture's.</summary>
    private static (FixtureBook Book, string FamilyCode) Changed(string change)
    {
        var (name, family) = change.StartsWith("R010", StringComparison.Ordinal) ? (R010, "R010")
            : change.StartsWith("S009", StringComparison.Ordinal) ? (S009, "S009")
            : (R025, "R025");
        var book = ContractFixture.Load(name).Workbook;
        var excluded = name == R025 ? book.Sheet("ETP_Excluded").Rows : null;
        switch (change)
        {
            case "unreadable block number": book.SetBlock(3, "block", "three"); break;
            case "unreadable header row": book.SetKey("header_row", "first"); break;
            case "version 2": book.Sheet("Info").Rows[0][1] = "2"; break;
            case "builder key missing": book.RemoveKey("builder"); break;
            case "family code differs": book.SetKey("family_code", "R022"); break;
            case "store code differs": book.SetKey("store_code", "HEMW"); break;
            case "rule snapshot for R025": book.SetKey("rule", "snapshot"); break;
            case "excluded sheet missing": book.Sheets.Remove(book.Sheet("ETP_Excluded")); break;
            case "SourceFile column on Data":
                foreach (var row in book.Sheet("Data").Rows) row.Add(row == book.Sheet("Data").Rows[0] ? "SourceFile" : "x.xlsx");
                break;
            case "data_rows wrong": book.SetKey("data_rows", "8"); break;
            case "block table header renamed": book.Sheet("Info").Rows.First(row => row.Count > 0 && Equals(row[0], "block"))[8] = "exported_at"; break;
            case "gap":
                book.SetBlock(1, "last_row", "4");
                book.SetBlock(1, "row_count", "3");
                book.SetBlock(1, "raw_rows", "3");
                break;
            case "overlap":
                book.SetBlock(3, "first_row", "7");
                book.SetBlock(3, "row_count", "2");
                book.SetBlock(3, "raw_rows", "4");
                break;
            case "outside data":
                book.SetBlock(3, "last_row", "9");
                book.SetBlock(3, "row_count", "2");
                book.SetBlock(3, "raw_rows", "4");
                break;
            case "arithmetic": book.SetBlock(1, "raw_rows", "5"); break;
            case "sha upper case": book.SetBlock(1, "source_sha256", new string('A', 64)); break;
            case "export time contradicts name": book.SetBlock(1, "export_time", "2026-07-03T15:07"); break;
            case "export time blank": book.SetBlock(1, "export_time", ""); break;
            case "period missing": book.SetBlock(1, "period_to", ""); break;
            case "R010 snapshot date missing": book.SetBlock(2, "snapshot_date", ""); break;
            case "R010 snapshot date duplicate": book.SetBlock(2, "snapshot_date", "2026-07-02"); break;
            case "S009 Snapshot_As_Of disagrees": book.Sheet("Snapshot History").Rows[2][4] = "2026-08-08"; break;
            case "S009 two Data blocks":
                book.SetBlock(1, "sheet", "Data");
                book.SetBlock(1, "first_row", "2");
                book.SetBlock(1, "last_row", "2");
                book.SetBlock(1, "row_count", "1");
                book.SetBlock(1, "raw_rows", "1");
                book.SetBlock(2, "first_row", "3");
                book.SetBlock(2, "row_count", "1");
                book.SetBlock(2, "raw_rows", "1");
                book.Sheets.Remove(book.Sheet("Snapshot History"));
                book.RemoveKey("history_sheet");
                book.RemoveKey("history_extra_columns");
                book.RemoveKey("history_rows");
                break;
            case "map row missing":
                excluded!.RemoveAt(4);
                book.SetKey("excluded_rows", "3");
                break;
            case "twin in own block": excluded![1][2] = "7"; break;
            case "map names unknown block": excluded![4][0] = "9"; break;
            case "twin reused": excluded![2][2] = "4"; break;
            case "duplicate sha": book.SetBlock(3, "source_sha256", new string('1', 64)); break;
            case "duplicate file and time":
                book.SetBlock(3, "source_file", book.Block(2, "source_file"));
                book.SetBlock(3, "export_time", book.Block(2, "export_time"));
                break;
            case "unknown key": book.AddKey("colour", "blue"); break;
            case "legacy block":
                book.SetBlock(1, "completeness", "legacy");
                book.SetBlock(1, "source_sha256", "");
                book.SetBlock(1, "raw_rows", "");
                break;
            case "row outside period": book.SetBlock(1, "period_from", "2026-07-02"); break;
            case "S009 history repeats data":
                // The same export as the Data block (same file, time and SHA-256), also written on Snapshot History.
                foreach (var column in new[] { "snapshot_date", "source_file", "source_sha256", "export_time" })
                    book.SetBlock(1, column, book.Block(2, column));
                foreach (var row in book.Sheet("Snapshot History").Rows.Skip(1)) (row[4], row[5]) = ("2026-09-29", book.Block(2, "source_file"));
                break;
            case "S009 history block of another export shares the Data date":
                book.SetBlock(1, "snapshot_date", "2026-09-29");
                foreach (var row in book.Sheet("Snapshot History").Rows.Skip(1)) row[4] = "2026-09-29";
                break;
            case "R010 blocks out of block-number order":
                book.SetBlock(1, "first_row", "4");
                book.SetBlock(1, "last_row", "5");
                book.SetBlock(2, "first_row", "2");
                book.SetBlock(2, "last_row", "3");
                break;
            case "R010 Data sheet missing": book.Sheets.Remove(book.Sheet("Data")); break;
            case "S009 history sheet missing": book.Sheets.Remove(book.Sheet("Snapshot History")); break;
            case "S009 data_sheet names another sheet": book.SetKey("data_sheet", "Sheet1"); break;
            case "date-typed key cell": book.SetKey("coverage_from", new DateTime(2026, 7, 1)); break;
            default: throw new ArgumentOutOfRangeException(nameof(change), change, null);
        }
        return (book, family);
    }
}
