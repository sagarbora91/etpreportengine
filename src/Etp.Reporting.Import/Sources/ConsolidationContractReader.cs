using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Reads the machine section of a contract workbook (contract 3.1-3.3) and its <c>ETP_Excluded</c> map (contract 4), layout
/// only (spec 6.1, from P1). A workbook is a contract workbook when <c>Info!A1</c> is <c>etp_contract</c>. Cell text is kept as
/// written; only a section that cannot be read at all gives <c>CONTRACT_UNREADABLE</c>, everything else is the validator's.
/// </summary>
public sealed class ConsolidationContractReader : IConsolidationContractReader
{
    public ContractReadResult Read(WorkbookSnapshot workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var info = SheetText.Find(workbook, ConsolidationContractLayout.InfoSheet);
        if (info is null || !IsContract(info)) return ContractReadResult.NotAContract;

        var versionText = info.Headers.Count > 1 ? info.Headers[1].Trim() : "";
        if (ContractCell.Int(versionText) is null)
            return Unreadable(info.HeaderRowNumber, "B1 does not hold the contract version");

        var lines = SheetText.Lines(info).Where(line => line.RowNumber > info.HeaderRowNumber).ToArray();
        var index = 0;
        var expected = info.HeaderRowNumber + 1;

        // Header keys: rows 2..k, up to the blank row.
        var keys = new List<ContractHeaderKey>();
        while (index < lines.Length && lines[index].RowNumber == expected && !lines[index].IsBlank && !IsBlockTableHeader(lines[index]))
        {
            var line = lines[index];
            if (line.Text(0).Length == 0) return Unreadable(line.RowNumber, $"row {line.RowNumber} has a value but no key");
            keys.Add(new(line.RowNumber, line.Text(0), line.Text(1), IsText(line, 0) && IsText(line, 1)));
            index++;
            expected++;
        }
        if (keys.Count == 0) return Unreadable(expected, "there are no header keys under the contract version");

        // Block table: the next non-blank row is its header; its rows run to the next blank row.
        while (index < lines.Length && lines[index].IsBlank) index++;
        if (index == lines.Length || !IsBlockTableHeader(lines[index]))
            return Unreadable(index < lines.Length ? lines[index].RowNumber : expected, "there is no block table after the header keys");
        var headerLine = lines[index];
        var header = Header(headerLine.Cells);
        if (header.Any(name => name.Length == 0) || header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Count)
            return Unreadable(headerLine.RowNumber, "the block-table header has a blank or repeated column name");
        index++;
        expected = headerLine.RowNumber + 1;
        var blocks = new List<ContractBlockRow>();
        while (index < lines.Length && lines[index].RowNumber == expected && !lines[index].IsBlank)
        {
            var line = lines[index];
            if (line.Cells.Skip(header.Count).Any(cell => SheetText.Of(cell).Length > 0))
                return Unreadable(line.RowNumber, $"row {line.RowNumber} has values outside the block table");
            blocks.Add(new(line.RowNumber, Cells(header, line)) { AllCellsText = line.Cells.All(SheetText.IsText) });
            index++;
            expected++;
        }

        var contract = new ConsolidationContract(new(versionText, keys), header, blocks) { BlockTableHeaderRow = headerLine.RowNumber };
        if (SheetText.Find(workbook, ConsolidationContractLayout.ExcludedSheet) is { } excluded)
        {
            var excludedHeader = Header(excluded.Headers.Select(name => new WorkbookCell(name)).ToArray());
            contract = contract with
            {
                ExcludedHeader = excludedHeader,
                Excluded = SheetText.Lines(excluded).Where(line => line.RowNumber > excluded.HeaderRowNumber && !line.IsBlank)
                    .Select(line => new ContractExcludedRow(line.RowNumber, Cells(excludedHeader, line))).ToArray()
            };
        }
        return new(true, contract, []);
    }

    /// <summary>True when <c>Info!A1</c> holds the contract marker.</summary>
    public static bool IsContract(WorkbookSheet info) =>
        info.HeaderRowNumber == 1 && info.Headers.Count > 0 &&
        string.Equals(info.Headers[0].Trim(), ConsolidationContractLayout.Marker, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockTableHeader(SheetLine line) =>
        string.Equals(line.Text(0), ConsolidationContractLayout.BlockTableColumns[0], StringComparison.OrdinalIgnoreCase);

    private static bool IsText(SheetLine line, int column) => column >= line.Cells.Count || SheetText.IsText(line.Cells[column]);

    private static IReadOnlyList<string> Header(IReadOnlyList<WorkbookCell> cells)
    {
        var names = cells.Select(SheetText.Of).ToList();
        while (names.Count > 0 && names[^1].Length == 0) names.RemoveAt(names.Count - 1);
        return names;
    }

    // Column names are matched without regard to case, so a typed reading never misses a cell; the validator judges the
    // spelling. A repeated name keeps its first column.
    private static IReadOnlyDictionary<string, string> Cells(IReadOnlyList<string> header, SheetLine line)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var column = 0; column < header.Count; column++) cells.TryAdd(header[column], line.Text(column));
        return cells;
    }

    private static ContractReadResult Unreadable(int row, string reason) => new(true, null,
    [
        new ImportDiagnostic(ImportCodes.Contract.Unreadable, ImportDiagnosticSeverity.Blocker,
            $"The consolidation contract in Info cannot be read: {reason}.", ConsolidationContractLayout.InfoSheet, row)
    ]);
}
