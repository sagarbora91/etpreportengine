using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// Synthetic, anonymised shapes of today's consolidated workbooks (Info sheets of the 29 Sep package) and of contract v1
/// workbooks. Only file names, ranges, counts and dates; no customer values.
/// </summary>
internal static class SyntheticConsolidatedWorkbooks
{
    public const string Hash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static IReadOnlyList<string> BinWiseHeaders => EtpReportFamilyRegistry.Resolve("R010").Headers;

    /// <summary>An R010 Data sheet with <paramref name="rows"/> data rows from row 2.</summary>
    public static WorkbookSheet BinWiseData(int rows, string? store = "WLMHW", string name = "Data") =>
        new(name, 1, BinWiseHeaders, Enumerable.Range(2, rows).Select(number => Row(BinWiseHeaders, new Dictionary<string, object?>
        {
            ["STORE CODE"] = store, ["ITEMNUMBER"] = $"SYN-ITEM-{number:0000}", ["CLOSINGBALANCE"] = 1m, ["UCP"] = 100m, ["TOTALUCP"] = 100m
        }, number)).ToArray());

    public static WorkbookRow Row(IReadOnlyList<string> headers, IReadOnlyDictionary<string, object?> values, int number) =>
        new(number, headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());

    public static WorkbookRow Row(int number, params object?[] cells) =>
        new(number, cells.Select(value => new WorkbookCell(value, value?.ToString())).ToArray());

    /// <summary>One legacy Info block row: <c>Package | Source file | Raw rows | Rows retained | Rows excluded | Period from | Period to | Data row block | Disposition</c>.</summary>
    public sealed record LegacyBlock(string SourceFile, string Range, int Retained, string Disposition = "Snapshot retained as its own block",
        string Package = "Synthetic pack", int? Raw = null, int Excluded = 0, string? PeriodFrom = null, string? PeriodTo = null);

    public static readonly string[] LegacyHeader =
        ["Package", "Source file", "Raw rows", "Rows retained", "Rows excluded", "Period from", "Period to", "Data row block", "Disposition"];

    /// <summary>
    /// Today's Info layout: a title, an "Updated by" line full of dates, key rows (with <c>Coverage</c>), the block table,
    /// then free-text notes. <paramref name="repeatTable"/> appends the same table again, as an appended update does.
    /// </summary>
    public static WorkbookSheet LegacyInfo(string coverage, IReadOnlyList<LegacyBlock> blocks, bool repeatTable = false, string family = "R010")
    {
        var rows = new List<WorkbookRow>
        {
            Row(2, "Updated by pack 01/07-29/09/2026 (ALL REPORTS.zip, 15-Sep-2026)", "Rule: snapshot"),
            Row(4, "Family ID", family), Row(5, "Consolidation rule", "snapshot"), Row(6, "Coverage", coverage),
            Row(7, "Rows before this update", 0m), Row(8, "Status", "PASS - consolidated")
        };
        var next = 10;
        void Table()
        {
            rows.Add(Row(next++, LegacyHeader));
            foreach (var block in blocks)
                rows.Add(Row(next++, block.Package, block.SourceFile, (decimal)(block.Raw ?? block.Retained), (decimal)block.Retained,
                    (decimal)block.Excluded, block.PeriodFrom, block.PeriodTo, block.Range, block.Disposition));
            next++;
        }
        Table();
        if (repeatTable) Table();
        rows.Add(Row(next++, "Snapshot 03-Jan-2026 noted by hand; 20251231 was the previous build."));
        rows.Add(Row(next, "Raw headers and values are preserved in Data."));
        return new(ConsolidationContractLayout.InfoSheet, 1, [$"ETP Consolidation - {family} BinWise-Stock"], rows);
    }

    /// <summary>One contract block row; blanks where a snapshot block has none.</summary>
    public static string[] ContractBlock(int block, string sheet, int first, int last, string sourceFile, string exportTime, string snapshotDate) =>
        [block.ToString(), sheet, first.ToString(), last.ToString(), (last - first + 1).ToString(), sourceFile, "xlsx", new string('a', 63) + block,
         exportTime, "", "", "none", snapshotDate, (last - first + 1).ToString(), "0", "0", "complete", $"Snapshot {snapshotDate} retained"];

    /// <summary>A contract v1 Info sheet (contract 3.1-3.3) with free-text notes below the block table.</summary>
    public static WorkbookSheet ContractInfo(IReadOnlyList<(string Key, string Value)> keys, IReadOnlyList<string[]> blocks, string version = "1")
    {
        var rows = new List<WorkbookRow>();
        var next = 2;
        foreach (var (key, value) in keys) rows.Add(Row(next++, key, value));
        next++;
        rows.Add(Row(next++, [.. ConsolidationContractLayout.BlockTableColumns]));
        foreach (var block in blocks) rows.Add(Row(next++, [.. block]));
        next++;
        rows.Add(Row(next, "Human notes: 2026-12-31 is not a snapshot date."));
        return new(ConsolidationContractLayout.InfoSheet, 1, [ConsolidationContractLayout.Marker, version, "ETP Consolidation - synthetic"], rows);
    }

    public static IReadOnlyList<(string Key, string Value)> SnapshotKeys(string family, string? store, int dataRows, int blocks) =>
    [
        ("family_code", family), ("report_name", "BinWise-Stock"), ("store_code", store ?? ""), ("business_unit", "RETAIL"),
        ("rule", "snapshot"), ("data_sheet", "Data"), ("header_row", "1"), ("data_rows", dataRows.ToString()),
        ("block_count", blocks.ToString()), ("built_at", "2026-10-05T10:15:00+05:30"), ("builder", "synthetic-builder 2.0")
    ];

    /// <summary>A contract-shaped stacked R010: 2 Jul (rows 2-4), 7 Aug (5-7), 29 Sep (8-9), with an exclusion map.</summary>
    public static WorkbookSnapshot ContractBinWise(string? dataStore = "WLMHW", string? contractStore = "WLMHW", string fileName = "R010_BinWise_Stock.xlsx",
        string? sourcePath = null)
    {
        var info = ContractInfo(SnapshotKeys("R010", contractStore, 8, 3),
        [
            ContractBlock(1, "Data", 2, 4, "202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2026-07-02T14:46", "2026-07-02"),
            ContractBlock(2, "Data", 5, 7, "202608071848_BinWise-Stock - BinWise-Stock.xlsx", "2026-08-07T18:48", "2026-08-07"),
            ContractBlock(3, "Data", 8, 9, "202609291433_BinWise-Stock - BinWise-Stock.xlsx", "2026-09-29T14:33", "2026-09-29")
        ]);
        var excluded = new WorkbookSheet(ConsolidationContractLayout.ExcludedSheet, 1, ["block", "sheet", "row"],
            [Row(2, "2", "Data", "3"), Row(3, "3", "Data", "6")]);
        return new(fileName, 1, Hash, [BinWiseData(8, dataStore), info, excluded], sourcePath);
    }

    /// <summary>A WLMHW-shaped stacked R010: three tiling legacy blocks 2:4, 5:7, 8:9 exported 2 Jul, 7 Aug and 29 Sep.</summary>
    public static WorkbookSnapshot LegacyStackedBinWise(bool repeatTable = false) => new("R010_BinWise_Stock.xlsx", 1, Hash,
    [
        BinWiseData(8),
        LegacyInfo("No dated rows",
        [
            new("202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2:4", 3, "Snapshot 02-Jul retained", "Historical ZIP"),
            new("202608071848_BinWise-Stock - BinWise-Stock.xlsx", "5:7", 3, "Snapshot 07-Aug retained", "New ZIP"),
            new("202609291433_BinWise-Stock - BinWise-Stock.xlsx", "8:9", 2)
        ], repeatTable)
    ], @"V:\ETP\Consolidated data import package (29 Sep 2026)\Retail\WLMHW\R010_BinWise_Stock.xlsx");

    /// <summary>A HEMW-shaped R010: one legacy block 2:5 exported 7 Sep, kept in a folder named for 29 Sep.</summary>
    public static WorkbookSnapshot LegacySingleBinWise() => new("R010_BinWise_Stock.xlsx", 1, Hash,
    [
        BinWiseData(4, "HEMW"),
        LegacyInfo("No dated rows", [new("202609071457_BinWise-Stock - BinWise-Stock.xlsx", "2:5", 4, Package: "Snapshot 07-Sep-2026 (BinWise, HEMW)")])
    ], @"V:\ETP\Consolidated data import package (29 Sep 2026)\Retail\HEMW\R010_BinWise_Stock.xlsx");
}
