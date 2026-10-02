using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.TestSupport;

/// <summary>
/// Synthetic R010 (BinWise) workbooks whose units repeat unchanged from one snapshot to the next, as a watch in
/// stock does: a legacy stacked workbook (Info block table, blocks 2 Jul, 7 Aug and 29 Sep) and single-date raw
/// exports dated by their export name. Item numbers and values are synthetic. Linked into the SQL test assemblies.
/// </summary>
internal static class StackedBinWiseWorkbooks
{
    /// <summary>The units every snapshot holds unchanged.</summary>
    public static readonly (string Item, decimal Quantity)[] Units = [("UNIT-A", 1m), ("UNIT-B", 2m)];

    /// <summary>A stacked R010: 2 Jul (rows 2-3) and 7 Aug (4-5) hold <see cref="Units"/>; 29 Sep (6-8) adds UNIT-C.</summary>
    public static WorkbookSnapshot Stacked(string store, string hashSeed, string firstBlockExport = "202607021446",
        string secondBlockExport = "202608071848") =>
        new("R010_BinWise_Stock.xlsx", 10, Hash(hashSeed),
        [
            Data(store, [.. Units, .. Units, .. Units, ("UNIT-C", 3m)]),
            new("Info", 1, ["ETP Consolidation - R010 BinWise-Stock"],
            [
                Text(2, "Updated by pack 01/07-29/09/2026", "Rule: snapshot"), Text(4, "Family ID", "R010"), Text(5, "Coverage", "No dated rows"),
                Text(7, "Package", "Source file", "Raw rows", "Rows retained", "Rows excluded", "Period from", "Period to", "Data row block", "Disposition"),
                Text(8, Block($"{firstBlockExport}_BinWise-Stock - BinWise-Stock.xlsx", 2, 3)),
                Text(9, Block($"{secondBlockExport}_BinWise-Stock - BinWise-Stock.xlsx", 4, 5)),
                Text(10, Block("202609291433_BinWise-Stock - BinWise-Stock.xlsx", 6, 8))
            ])
        ]);

    /// <summary>A raw single-date R010 export, dated by its export name <paramref name="exportPrefix"/> (yyyyMMddHHmm).</summary>
    public static WorkbookSnapshot Single(string store, string exportPrefix, string hashSeed, params (string Item, decimal Quantity)[] units) =>
        new($"{exportPrefix}_BinWise-Stock - BinWise-Stock.xlsx", 10, Hash(hashSeed), [Data(store, units)]);

    /// <summary>A 64-character SHA-256 text from a two-hex-digit seed.</summary>
    public static string Hash(string hashSeed) => string.Concat(Enumerable.Repeat(hashSeed, 32));

    private static WorkbookSheet Data(string store, IReadOnlyList<(string Item, decimal Quantity)> units)
    {
        var family = EtpReportFamilyRegistry.Resolve("R010");
        var columns = family.Headers.Select(header => family.Columns.Single(column => column.SourceHeader == header)).ToArray();
        WorkbookRow Row(int number, string item, decimal quantity) => new(number, columns.Select(column => new WorkbookCell(column.CanonicalField switch
        {
            "store_code" => store,
            "itemnumber" => item,
            "lotnumber" => "LOT-1",
            "uid" => null,
            "closingbalance" => quantity,
            "ucp" => 10m,
            "totalucp" => 10m * quantity,
            _ => column.DataType == CanonicalDataType.Decimal ? (object)0m : "SYNTHETIC"
        })).ToArray());
        return new("Data", 1, family.Headers, units.Select((unit, index) => Row(index + 2, unit.Item, unit.Quantity)).ToArray());
    }

    private static string[] Block(string sourceFile, int first, int last) =>
        ["Synthetic pack", sourceFile, (last - first + 1).ToString(), (last - first + 1).ToString(), "0", "", "", $"{first}:{last}", "Snapshot retained"];

    private static WorkbookRow Text(int number, params string[] cells) => new(number, cells.Select(value => new WorkbookCell(value, value)).ToArray());
}
