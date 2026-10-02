using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class LegacyInfoBlockReaderTests
{
    private static LegacyInfoTable Read(WorkbookSnapshot workbook) =>
        new LegacyInfoBlockReader().Read(workbook, workbook.Sheets.First(sheet => sheet.Name == "Data"));

    [Fact]
    public void WLMHW_shaped_R010_has_three_tiling_blocks()
    {
        var table = Read(LegacyStackedBinWise());

        Assert.True(table.Found);
        Assert.True(table.Tiles);
        Assert.Empty(table.Diagnostics);
        Assert.Equal([(2, 4), (5, 7), (8, 9)], table.Blocks.Select(block => (block.FirstRow, block.LastRow)));
        Assert.Equal(
            [ExportTime.AtMinute(new(2026, 7, 2, 14, 46, 0)), ExportTime.AtMinute(new(2026, 8, 7, 18, 48, 0)), ExportTime.AtMinute(new(2026, 9, 29, 14, 33, 0))],
            table.Blocks.Select(block => block.ExportTime));
        var first = table.Blocks[0];
        Assert.Equal(("Historical ZIP", 3, 3, 0, "Snapshot 02-Jul retained"), (first.Package, first.RawRows, first.RowsRetained, first.RowsExcluded, first.Disposition));
        Assert.Equal(11, first.InfoRow);
    }

    [Fact]
    public void Repeated_update_tables_are_read_once()
    {
        var table = Read(LegacyStackedBinWise(repeatTable: true));

        Assert.True(table.Tiles);
        Assert.Equal(3, table.Blocks.Count);
    }

    [Fact]
    public void HEMW_shaped_R010_is_one_block_exported_on_7_Sep()
    {
        var table = Read(LegacySingleBinWise());

        Assert.True(table.Tiles);
        var block = Assert.Single(table.Blocks);
        Assert.Equal((2, 5, new DateOnly(2026, 9, 7)), (block.FirstRow, block.LastRow, block.ExportTime.ExportDate));
    }

    [Fact]
    public void HEMW_shaped_R025_with_a_gap_is_one_whole_file()
    {
        // Rows 2:9 have no block, as rows 2:684 of the package's HEMW R025 do.
        var workbook = Sales(20,
        [
            new("202609062129_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", "10:15", 6, "New unique rows retained", PeriodFrom: "2026-07-02", PeriodTo: "2026-08-31"),
            new("202609071451_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", "16:17", 2, "New unique rows retained", PeriodFrom: "2026-09-01", PeriodTo: "2026-09-06"),
            new("202609291436_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", "18:21", 4, "New unique rows retained", Raw: 9, Excluded: 5)
        ]);

        var table = Read(workbook);

        Assert.True(table.Found);
        Assert.False(table.Tiles);
        Assert.Equal(new DateOnly(2026, 7, 2), table.Blocks[0].PeriodFrom);
        var warning = Assert.Single(table.Diagnostics);
        Assert.Equal(ImportCodes.InfoBlocksUnusable, warning.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("rows 2:9 have no block", warning.Message);
    }

    [Fact]
    public void WLMHW_shaped_R030_with_stale_overlapping_ranges_is_one_whole_file()
    {
        var workbook = Sales(20,
        [
            new("202608071907_Variant Stock ledger - Variant Stock ledger.xlsx", "2:6", 5),
            new("202609062109_Variant Stock ledger - Variant Stock ledger.xlsx", "7:14", 8),
            new("202609291452_Variant Stock ledger - Variant Stock ledger.xlsx", "12:21", 10)
        ]);

        var table = Read(workbook);

        Assert.False(table.Tiles);
        Assert.Contains("overlap", Assert.Single(table.Diagnostics).Message);
    }

    [Fact]
    public void Retained_rows_must_add_up_to_the_data_rows()
    {
        var workbook = Sales(6, [new("202609291436_SDB-VariantwiseSales.xlsx", "2:7", 5)]);

        var table = Read(workbook);

        Assert.False(table.Tiles);
        Assert.Equal(ImportCodes.InfoBlocksUnusable, Assert.Single(table.Diagnostics).Code);
    }

    [Fact]
    public void Info_without_a_block_table_is_not_a_legacy_workbook()
    {
        var info = new WorkbookSheet("Info", 1, ["Family ID", "R010"], [Row(2, "Coverage", "2026-08-25")]);

        var table = Read(new WorkbookSnapshot("R010.xlsx", 1, Hash, [BinWiseData(1), info]));

        Assert.False(table.Found);
        Assert.Empty(table.Blocks);
    }

    private static WorkbookSnapshot Sales(int rows, IReadOnlyList<LegacyBlock> blocks)
    {
        var headers = EtpReportFamilyRegistry.Resolve("R025").Headers;
        var data = new WorkbookSheet("Data", 1, headers,
            Enumerable.Range(2, rows).Select(number => Row(headers, new Dictionary<string, object?> { ["STORE CODE"] = "HEMW" }, number)).ToArray());
        return new("R025_SDB_VariantwiseSales.xlsx", 1, Hash, [data, LegacyInfo("2024-09-16 to 2026-09-28", blocks, family: "R025")]);
    }
}
