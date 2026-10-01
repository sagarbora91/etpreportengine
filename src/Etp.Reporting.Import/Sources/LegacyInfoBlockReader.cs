using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Reads the block table of a pre-contract consolidated workbook's Info sheet (spec 6.6 steps 1-3; contract 11):
/// <list type="number">
/// <item>every row under every header row holding <c>Source file</c> and <c>Data row block</c>, exact repeats removed
/// (Info is appended per update, so headers repeat);</item>
/// <item>each row's <c>a:b</c> range and the export time of its source-file name;</item>
/// <item>the tiling test: the ranges partition <c>2..last Data row</c> with no gap or overlap and <c>Rows retained</c> adds up
/// to the Data rows. Otherwise <c>INFO_BLOCKS_UNUSABLE</c>, and the workbook is one whole-file block.</item>
/// </list>
/// A row with no range that retains no rows (an empty export, or a snapshot already held) holds no Data rows and is skipped.
/// </summary>
public sealed partial class LegacyInfoBlockReader : ILegacyInfoBlockReader
{
    private const string SourceFileColumn = "Source file";
    private const string RangeColumn = "Data row block";

    public LegacyInfoTable Read(WorkbookSnapshot workbook, WorkbookSheet dataSheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(dataSheet);
        var info = SheetText.Find(workbook, ConsolidationContractLayout.InfoSheet);
        if (info is null) return LegacyInfoTable.None;

        var lines = SheetText.Lines(info);
        var rows = new List<(SheetLine Line, Columns Columns)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = false;
        for (var index = 0; index < lines.Count; index++)
        {
            if (Columns.Find(lines[index]) is not { } columns) continue;
            found = true;
            for (var next = index + 1; next < lines.Count && lines[next].RowNumber == lines[next - 1].RowNumber + 1; next++)
            {
                var line = lines[next];
                if (Columns.Find(line) is not null || line.Text(columns.SourceFile).Length == 0) break;
                if (seen.Add(string.Join('\u001f', columns.All.Select(line.Text)))) rows.Add((line, columns));
            }
        }
        if (!found) return LegacyInfoTable.None;

        var blocks = new List<LegacyInfoBlock>();
        string? unusable = null;
        foreach (var (line, columns) in rows)
        {
            var retained = SheetText.Int(Cell(line, columns.RowsRetained));
            if (Range().Match(line.Text(columns.Range)) is not { Success: true } range)
            {
                if (retained != 0) unusable ??= $"Info row {line.RowNumber} names no Data row block for the rows it retains";
                continue;
            }
            var sourceFile = line.Text(columns.SourceFile);
            blocks.Add(new LegacyInfoBlock(int.Parse(range.Groups[1].Value), int.Parse(range.Groups[2].Value), sourceFile,
                ExportNameParser.Parse(sourceFile))
            {
                Package = NullIfEmpty(line.Text(columns.Package)),
                RawRows = SheetText.Int(Cell(line, columns.RawRows)),
                RowsRetained = retained,
                RowsExcluded = SheetText.Int(Cell(line, columns.RowsExcluded)),
                PeriodFrom = SheetText.Date(Cell(line, columns.PeriodFrom)),
                PeriodTo = SheetText.Date(Cell(line, columns.PeriodTo)),
                Disposition = NullIfEmpty(line.Text(columns.Disposition)),
                InfoRow = line.RowNumber
            });
        }

        unusable ??= TilingFailure(blocks, dataSheet);
        return unusable is null
            ? new(true, true, blocks, [])
            : new(true, false, blocks,
            [
                new ImportDiagnostic(ImportCodes.InfoBlocksUnusable, ImportDiagnosticSeverity.Warning,
                    $"The Info block table does not describe the Data rows ({unusable}); the workbook is read as one block.",
                    ConsolidationContractLayout.InfoSheet)
            ]);
    }

    /// <summary>Why the blocks do not tile the data rows, or null when they do.</summary>
    private static string? TilingFailure(IReadOnlyList<LegacyInfoBlock> blocks, WorkbookSheet dataSheet)
    {
        var firstRow = dataSheet.HeaderRowNumber + 1;
        var dataRows = dataSheet.Rows.Count(row => row.RowNumber > dataSheet.HeaderRowNumber);
        var lastRow = dataRows == 0 ? dataSheet.HeaderRowNumber : dataSheet.Rows.Max(row => row.RowNumber);
        var next = firstRow;
        foreach (var block in blocks.OrderBy(block => block.FirstRow).ThenBy(block => block.LastRow))
        {
            if (block.LastRow < block.FirstRow) return $"rows {block.FirstRow}:{block.LastRow} are not a range";
            if (block.FirstRow > next) return $"rows {next}:{block.FirstRow - 1} have no block";
            if (block.FirstRow < next) return $"blocks overlap at row {block.FirstRow}";
            next = block.LastRow + 1;
        }
        if (next - 1 > lastRow) return $"a block runs past the last Data row {lastRow}";
        if (next - 1 < lastRow) return $"rows {next}:{lastRow} have no block";
        if (blocks.Any(block => block.RowsRetained is null)) return "a block does not state the rows it retains";
        var retained = blocks.Sum(block => (long)block.RowsRetained!.Value);
        return retained == dataRows ? null : $"the blocks retain {retained} rows but Data holds {dataRows}";
    }

    private static WorkbookCell? Cell(SheetLine line, int column) => column >= 0 && column < line.Cells.Count ? line.Cells[column] : null;

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    [GeneratedRegex(@"^\s*(\d{1,9})\s*:\s*(\d{1,9})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Range();

    /// <summary>Column positions of one block-table header row; -1 when the header has no such column.</summary>
    private sealed record Columns(int Package, int SourceFile, int RawRows, int RowsRetained, int RowsExcluded,
        int PeriodFrom, int PeriodTo, int Range, int Disposition)
    {
        public IEnumerable<int> All => [Package, SourceFile, RawRows, RowsRetained, RowsExcluded, PeriodFrom, PeriodTo, Range, Disposition];

        public static Columns? Find(SheetLine line)
        {
            var names = line.Cells.Select(SheetText.Of).ToList();
            int At(string name) => names.FindIndex(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            var sourceFile = At(SourceFileColumn);
            var range = At(RangeColumn);
            return sourceFile < 0 || range < 0 ? null : new(At("Package"), sourceFile, At("Raw rows"), At("Rows retained"),
                At("Rows excluded"), At("Period from"), At("Period to"), range, At("Disposition"));
        }
    }
}
