using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class SnapshotDateResolverTests
{
    private static SnapshotDating Resolve(WorkbookSnapshot workbook) =>
        new SnapshotDateResolver().Resolve(workbook, workbook.Sheets.First(sheet => sheet.Name == "Data"));

    [Fact]
    public void WLMHW_shaped_stacked_R010_gives_three_block_dates()
    {
        var dating = Resolve(LegacyStackedBinWise());

        Assert.False(dating.HasBlockers);
        Assert.Equal(SnapshotDateBasis.InfoBlock, dating.Basis);
        Assert.Equal(
            [(2, 4, new DateOnly(2026, 7, 2)), (5, 7, new DateOnly(2026, 8, 7)), (8, 9, new DateOnly(2026, 9, 29))],
            dating.Blocks.Select(block => (block.FirstRow, block.LastRow, block.SnapshotDate)));
        Assert.Equal(new DateOnly(2026, 7, 2), dating.From);
        Assert.Equal(new DateOnly(2026, 9, 29), dating.To);
        Assert.Equal(new DateOnly(2026, 8, 7), SnapshotBlock.DateOf(dating.Blocks, "Data", 6));
        Assert.DoesNotContain(dating.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateFromFolder);
    }

    [Fact]
    public void Info_appended_twice_still_gives_three_block_dates()
    {
        var dating = Resolve(LegacyStackedBinWise(repeatTable: true));

        Assert.Equal(3, dating.Blocks.Count);
        Assert.Empty(dating.Diagnostics);
    }

    [Fact]
    public void HEMW_shaped_R010_in_a_29_Sep_folder_gives_7_Sep()
    {
        var dating = Resolve(LegacySingleBinWise());

        var block = Assert.Single(dating.Blocks);
        Assert.Equal(new DateOnly(2026, 9, 7), block.SnapshotDate);
        Assert.Equal(SnapshotDateBasis.InfoBlock, block.Basis);
        Assert.Empty(dating.Diagnostics);
    }

    [Fact]
    public void Contract_shaped_R010_gives_its_block_dates()
    {
        var dating = Resolve(ContractBinWise(sourcePath: @"V:\ETP\till 30 sep 2026\R010_BinWise_Stock.xlsx"));

        Assert.False(dating.HasBlockers);
        Assert.Equal(SnapshotDateBasis.Contract, dating.Basis);
        Assert.Equal([1, 2, 3], dating.Blocks.Select(block => block.BlockNo!.Value));
        Assert.Equal([new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 29)],
            dating.Blocks.Select(block => block.SnapshotDate));
        Assert.Empty(dating.Diagnostics);
    }

    [Fact]
    public void Contract_rows_outside_every_dated_block_are_not_dated_by_a_later_tier()
    {
        var workbook = ContractBinWise(fileName: "202609291433_BinWise-Stock - BinWise-Stock.xlsx");
        var data = workbook.Sheets[0];
        workbook = workbook with { Sheets = [data with { Rows = [.. data.Rows, data.Rows[0] with { RowNumber = 10 }] }, .. workbook.Sheets.Skip(1)] };

        var dating = Resolve(workbook);

        Assert.Empty(dating.Blocks);
        var unknown = Assert.Single(dating.Diagnostics);
        Assert.Equal(ImportCodes.SnapshotDateUnknown, unknown.Code);
        Assert.Equal(10, unknown.RowNumber);
    }

    [Fact]
    public void Two_folder_tokens_give_SNAPSHOT_DATE_AMBIGUOUS()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)],
            @"V:\ETP\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\R010_BinWise_Stock.xlsx");

        var dating = Resolve(workbook);

        Assert.Empty(dating.Blocks);
        var ambiguous = Assert.Single(dating.Diagnostics);
        Assert.Equal(ImportCodes.SnapshotDateAmbiguous, ambiguous.Code);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, ambiguous.Severity);
        Assert.Contains("2026-07-01, 2026-08-25", ambiguous.Message);
    }

    [Fact]
    public void Renamed_raw_R010_in_a_dated_folder_gives_the_folder_date_with_a_warning()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)],
            @"V:\ETP\HEMW\till 29 sep 2026\R010_BinWise_Stock.xlsx");

        var dating = Resolve(workbook);

        var block = Assert.Single(dating.Blocks);
        Assert.Equal((2, 4, new DateOnly(2026, 9, 29), SnapshotDateBasis.Folder),
            (block.FirstRow, block.LastRow, block.SnapshotDate, block.Basis));
        var warning = Assert.Single(dating.Diagnostics);
        Assert.Equal(ImportCodes.SnapshotDateFromFolder, warning.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Raw_export_name_wins_over_its_folder()
    {
        var workbook = new WorkbookSnapshot("202609291433_BinWise-Stock - BinWise-Stock.xlsx", 1, Hash, [BinWiseData(3)],
            @"V:\ETP\till 6 sep 26\202609291433_BinWise-Stock - BinWise-Stock.xlsx");

        var block = Assert.Single(Resolve(workbook).Blocks);

        Assert.Equal((new DateOnly(2026, 9, 29), SnapshotDateBasis.ExportName), (block.SnapshotDate, block.Basis));
    }

    [Fact]
    public void Info_free_text_is_never_read()
    {
        // The title, the "Updated by" line and the notes all carry dates; none is a coverage value or a block.
        var info = new WorkbookSheet("Info", 1, ["ETP Consolidation - R010 BinWise-Stock"],
        [
            Row(2, "Updated by pack 01/07-29/09/2026 (ALL REPORTS.zip, 29/09/2026)", "Rule: snapshot"),
            Row(4, "Family ID", "R010"), Row(5, "Coverage", "No dated rows"),
            Row(7, "Snapshot 07-Sep-2026 (BinWise, HEMW) 202609291433"), Row(8, "Previous build 2026-08-25")
        ]);
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3, "HEMW"), info]);

        var dating = Resolve(workbook);

        Assert.Empty(dating.Blocks);
        Assert.Equal(ImportCodes.SnapshotDateUnknown, Assert.Single(dating.Diagnostics).Code);
    }

    [Fact]
    public void Single_coverage_date_dates_a_workbook_without_blocks()
    {
        var info = new WorkbookSheet("Info", 1, ["Family ID", "R010"],
            [Row(2, "Source", "sample 20260101"), Row(3, "Coverage", "2026-08-25")]);
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(1), info], @"V:\ETP\till 29 sep 2026\R010.xlsx");

        var block = Assert.Single(Resolve(workbook).Blocks);

        Assert.Equal((new DateOnly(2026, 8, 25), SnapshotDateBasis.InfoCoverage), (block.SnapshotDate, block.Basis));
    }

    [Fact]
    public void Single_legacy_block_without_an_export_time_is_dated_by_its_disposition()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash,
            [BinWiseData(2), LegacyInfo("No dated rows", [new("BinWise-Stock.xlsx", "2:3", 2, "Snapshot 07-Sep-2026 retained")])]);

        var block = Assert.Single(Resolve(workbook).Blocks);

        Assert.Equal((new DateOnly(2026, 9, 7), SnapshotDateBasis.InfoCoverage), (block.SnapshotDate, block.Basis));
    }

    [Fact]
    public void Undated_family_scope_takes_block_dates_instead_of_the_largest_date_in_Info()
    {
        var accepted = new MatchedImportEnvelopeFactory(["WLMHW", "HEMW"]).RequireAccepted(LegacyStackedBinWise());

        Assert.Equal("R010", accepted.Profile.ReportCode);
        Assert.Equal(new DateOnly(2026, 7, 2), accepted.Scope.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 29), accepted.Scope.PeriodEnd);
        Assert.Equal(3, accepted.Scope.SnapshotBlocks.Count);
    }

    [Fact]
    public void Ambiguous_snapshot_date_refuses_the_workbook()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)],
            @"V:\ETP\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\R010_BinWise_Stock.xlsx");

        var inspection = new MatchedImportEnvelopeFactory(["WLMHW"]).Inspect(workbook);

        Assert.False(inspection.Accepted);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateAmbiguous);
    }

    [Fact]
    public void Undated_workbook_is_accepted_without_a_date_so_the_folder_can_settle_it()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)]);

        var accepted = new MatchedImportEnvelopeFactory(["WLMHW"]).RequireAccepted(workbook);

        Assert.Null(accepted.Scope.PeriodEnd);
        Assert.Empty(accepted.Scope.SnapshotBlocks);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateUnknown);
        Assert.True(accepted.Scope.AwaitsSiblingDate);
        // A route without folder siblings refuses it rather than taking a business or context date.
        Assert.Equal(ImportCodes.SnapshotDateUnknown, Assert.Throws<ImportSourceException>(accepted.Scope.RequireOwnSnapshotDate).Code);
    }

    [Fact]
    public void Partly_dated_contract_block_table_is_refused_and_not_left_to_the_siblings()
    {
        var workbook = ContractBinWise();
        var info = ContractInfo(SnapshotKeys("R010", "WLMHW", 8, 3),
        [
            ContractBlock(1, "Data", 2, 4, "202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2026-07-02T14:46", "2026-07-02"),
            ContractBlock(2, "Data", 5, 7, "BinWise-Stock renamed.xlsx", "", ""),
            ContractBlock(3, "Data", 8, 9, "202609291433_BinWise-Stock - BinWise-Stock.xlsx", "2026-09-29T14:33", "2026-09-29")
        ]);
        workbook = workbook with { Sheets = [workbook.Sheets[0], info, workbook.Sheets[2]] };

        var inspection = new MatchedImportEnvelopeFactory(["WLMHW"]).Inspect(workbook);

        Assert.False(inspection.Accepted);
        var unknown = Assert.Single(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateUnknown);
        Assert.Equal((ImportDiagnosticSeverity.Blocker, (int?)5), (unknown.Severity, unknown.RowNumber));
    }

    [Fact]
    public void Tiling_legacy_table_with_an_unprefixed_source_file_is_refused_and_not_left_to_the_siblings()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash,
        [
            BinWiseData(8),
            LegacyInfo("No dated rows",
            [
                new("202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2:4", 3),
                new("BinWise-Stock renamed.xlsx", "5:7", 3),
                new("202609291433_BinWise-Stock - BinWise-Stock.xlsx", "8:9", 2)
            ])
        ], @"V:\ETP	ill 29 sep 2026\R010_BinWise_Stock.xlsx");

        var inspection = new MatchedImportEnvelopeFactory(["WLMHW"]).Inspect(workbook);

        Assert.False(inspection.Accepted);
        Assert.Contains(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateUnknown && diagnostic.RowNumber == 5);
    }

    [Fact]
    public void Legacy_blocks_are_numbered_in_sheet_order_whatever_order_Info_lists_them()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash,
        [
            BinWiseData(8),
            LegacyInfo("No dated rows",
            [
                new("202609291433_BinWise-Stock - BinWise-Stock.xlsx", "8:9", 2),
                new("202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2:4", 3),
                new("202608071848_BinWise-Stock - BinWise-Stock.xlsx", "5:7", 3)
            ])
        ]);

        var dating = Resolve(workbook);

        Assert.Equal([(2, 1, new DateOnly(2026, 7, 2)), (5, 2, new DateOnly(2026, 8, 7)), (8, 3, new DateOnly(2026, 9, 29))],
            dating.Blocks.OrderBy(block => block.FirstRow).Select(block => (block.FirstRow, block.BlockNo!.Value, block.SnapshotDate)));
    }

    [Fact]
    public void Two_different_Coverage_rows_in_appended_Info_give_SNAPSHOT_DATE_AMBIGUOUS()
    {
        var info = new WorkbookSheet("Info", 1, ["Family ID", "R010"],
            [Row(2, "Coverage", "2026-08-25"), Row(4, "Family ID", "R010"), Row(5, "Coverage", "2026-09-29")]);
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(1), info]);

        var dating = Resolve(workbook);

        Assert.Empty(dating.Blocks);
        Assert.Equal(ImportCodes.SnapshotDateAmbiguous, Assert.Single(dating.Diagnostics).Code);
    }

    [Theory]
    // The folder holding the file states no date; a dated work, archive or pack folder further up is never read.
    [InlineData(@"V:\ETP\Work in progress 2026-10-01\till 29 sep 2026\HEMW\R010_BinWise_Stock.xlsx")]
    [InlineData(@"F:\ETP\Reference\Work in progress 2026-10-01\HEMW pack\R010_BinWise_Stock.xlsx")]
    [InlineData(@"F:\ETP\Migration 2026-10-02\review\R010_BinWise_Stock.xlsx")]
    public void Folder_tier_reads_only_the_folder_that_holds_the_file(string path)
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)], path);

        var dating = Resolve(workbook);

        // Undated, so the folder import may still date it from its siblings (tier 7) and the override is not refused.
        Assert.Empty(dating.Blocks);
        Assert.True(dating.NoTierDated);
        Assert.Equal(ImportCodes.SnapshotDateUnknown, Assert.Single(dating.Diagnostics).Code);
    }

    [Fact]
    public async Task Folder_tier_reads_the_name_of_the_ZIP_a_root_entry_came_from()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zipPath = Path.Combine(root, "HEMW stock 29 Sep 2026.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var stream = archive.CreateEntry("R010_BinWise_Stock.xlsx").Open()) stream.WriteByte(1);
                using (var stream = archive.CreateEntry("HEMW/R010_BinWise_Stock.xlsx").Open()) stream.WriteByte(1);
            }
            await using var source = await BatchImportSource.OpenAsync(zipPath);
            var atRoot = source.WorkbookPaths.Single(path => Path.GetFileName(Path.GetDirectoryName(path)) != "HEMW");
            var nested = source.WorkbookPaths.Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == "HEMW");

            var dated = Resolve(new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)], atRoot));
            var undated = Resolve(new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash, [BinWiseData(3)], nested));

            var block = Assert.Single(dated.Blocks);
            Assert.Equal((new DateOnly(2026, 9, 29), SnapshotDateBasis.Folder), (block.SnapshotDate, block.Basis));
            Assert.Equal(ImportCodes.SnapshotDateFromFolder, Assert.Single(dated.Diagnostics).Code);
            // An entry inside a folder of the ZIP is read by that folder alone, as in an unpacked folder.
            Assert.True(undated.NoTierDated);
        }
        finally { Directory.Delete(root, true); }
    }
}
