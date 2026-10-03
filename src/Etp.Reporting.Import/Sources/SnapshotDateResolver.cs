using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// The snapshot dates of an undated family's data sheet (spec 6.4): the row ranges and the date of each, from the
/// one tier that dated them. Empty when the workbook could not be dated; the diagnostics then say why.
/// </summary>
public sealed record SnapshotDating(IReadOnlyList<SnapshotBlock> Blocks, IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public SnapshotDateBasis? Basis => Blocks.Count == 0 ? null : Blocks[0].Basis;
    public DateOnly? From => Blocks.Count == 0 ? null : Blocks.Min(block => block.SnapshotDate);
    public DateOnly? To => Blocks.Count == 0 ? null : Blocks.Max(block => block.SnapshotDate);
    public bool HasBlockers => Diagnostics.Any(diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker);

    /// <summary>
    /// True only when no tier yielded any date: the workbook is silent about its date, and only the folder import may
    /// still date it from its siblings (tier 7). A tier that dated some rows but not others is a refusal, never this.
    /// </summary>
    public bool NoTierDated { get; init; }
}

/// <summary>
/// Dates the snapshots of an undated family (R010, R023, SOR_AGEING) by tiers (spec 6.4). The first tier that yields
/// any date is used and no later tier is consulted:
/// <list type="number">
/// <item>contract block <c>snapshot_date</c> (<see cref="SnapshotDateBasis.Contract"/>), per block;</item>
/// <item>a legacy Info block table that tiles the data rows: each block's source-file export date
/// (<see cref="SnapshotDateBasis.InfoBlock"/>), per block;</item>
/// <item>the workbook's own export name (<see cref="SnapshotDateBasis.ExportName"/>);</item>
/// <item>the Info <c>Coverage</c> value when it is one date, or the date in the disposition of a single legacy block
/// (<see cref="SnapshotDateBasis.InfoCoverage"/>);</item>
/// <item>the name of the folder (or ZIP) that holds the file, when it states a date (<see cref="SnapshotDateBasis.Folder"/>), with the
/// warning <c>SNAPSHOT_DATE_FROM_FOLDER</c>; folders further up are never read.</item>
/// </list>
/// A whole-file tier with more than one distinct date refuses <c>SNAPSHOT_DATE_AMBIGUOUS</c>; it never takes a maximum and
/// never falls through. No date at all gives <c>SNAPSHOT_DATE_UNKNOWN</c> with <see cref="SnapshotDating.NoTierDated"/>,
/// which only the folder import may still settle from its siblings (tier 7). Info titles and free-text notes are never read.
/// </summary>
public sealed class SnapshotDateResolver(
    IConsolidationContractReader? contractReader = null,
    ILegacyInfoBlockReader? legacyReader = null)
{
    private const string CoverageKey = "Coverage";
    private readonly IConsolidationContractReader contracts = contractReader ?? new ConsolidationContractReader();
    private readonly ILegacyInfoBlockReader legacy = legacyReader ?? new LegacyInfoBlockReader();

    /// <param name="workbook">The workbook, with its <see cref="WorkbookSnapshot.SourcePath"/> when read from disk.</param>
    /// <param name="dataSheet">The sheet whose rows are imported.</param>
    /// <param name="contract">The contract already read by the preflight; read here when null.</param>
    public SnapshotDating Resolve(WorkbookSnapshot workbook, WorkbookSheet dataSheet, ContractReadResult? contract = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(dataSheet);
        contract ??= contracts.Read(workbook);
        if (contract.IsContract && contract.Contract is null) return new([], contract.Diagnostics);

        var rows = dataSheet.Rows.Where(row => row.RowNumber > dataSheet.HeaderRowNumber).Select(row => row.RowNumber).ToArray();
        var notes = new List<ImportDiagnostic>();

        // Tier 1: the contract's block dates.
        if (contract.Contract is { } layout)
        {
            var dated = layout.Blocks.Where(block => block.SnapshotDate is not null &&
                    string.Equals(block.Sheet, dataSheet.Name, StringComparison.OrdinalIgnoreCase))
                .Select(block => (Range: Range(block.FirstRow, block.LastRow, dataSheet), Date: block.SnapshotDate!.Value, Block: block.Block))
                .ToArray();
            if (dated.Length > 0) return PerBlock(dated, SnapshotDateBasis.Contract, dataSheet, rows, notes);
        }

        // Tier 2: a legacy Info block table that tiles the data rows.
        var table = contract.IsContract ? LegacyInfoTable.None : legacy.Read(workbook, dataSheet);
        notes.AddRange(table.Diagnostics);
        if (table.Tiles)
        {
            // Blocks are numbered in sheet order before the undated ones are set aside, so a number names the same block.
            var dated = table.Blocks.OrderBy(block => block.FirstRow)
                .Select((block, index) => (Block: block, Number: index + 1))
                .Where(item => item.Block.ExportTime.ExportDate is not null)
                .Select(item => (Range: ((int?)item.Block.FirstRow, (int?)item.Block.LastRow), Date: item.Block.ExportTime.ExportDate!.Value, Block: (int?)item.Number))
                .ToArray();
            if (dated.Length > 0) return PerBlock(dated, SnapshotDateBasis.InfoBlock, dataSheet, rows, notes);
        }

        // Tier 3 (Snapshot History rows) dates history blocks, which planner 1 does not import; it never dates Data.

        // Tier 4: the workbook's own export name.
        if (ExportNameParser.Parse(workbook.FileName).ExportDate is { } exported)
            return WholeFile([exported], SnapshotDateBasis.ExportName, "its export name", dataSheet, notes);

        // Tier 5: the Info coverage, or the disposition of a single legacy block. Titles and notes are never read.
        if (!contract.IsContract)
        {
            var stated = new List<DateOnly>();
            // Info is appended per update, so every Coverage row counts; two different values are ambiguous.
            if (SheetText.Find(workbook, ConsolidationContractLayout.InfoSheet) is { } info)
                stated.AddRange(SheetText.Lines(info)
                    .Where(line => string.Equals(line.Text(0), CoverageKey, StringComparison.OrdinalIgnoreCase))
                    .Select(line => SheetText.Date(line.Cells.Count > 1 ? line.Cells[1] : null))
                    .OfType<DateOnly>());
            if (table.Blocks.Count == 1 && table.Blocks[0].Disposition is { } disposition)
                stated.AddRange(SheetText.Dates(disposition).Select(found => found.Date));
            if (stated.Count > 0) return WholeFile(stated, SnapshotDateBasis.InfoCoverage, "the Info coverage", dataSheet, notes);
        }

        // Tier 6: the name of the folder that holds the file (for a ZIP entry at the archive's root, the ZIP's name; see
        // BatchImportSource). Only that one folder is read: when it states no date, a folder further up (an archive or
        // work folder) is never consulted, and the folder import may still date the file from its siblings (tier 7).
        var folders = Folders(workbook.SourcePath) is { Count: > 0 } names
            ? SheetText.Dates(names[^1], compactDigits: true).Select(found => found.Date).ToArray()
            : [];
        if (folders.Length > 0)
        {
            var dating = WholeFile(folders, SnapshotDateBasis.Folder, "its folder names", dataSheet, notes);
            if (dating.HasBlockers) return dating;
            return dating with
            {
                Diagnostics = [.. dating.Diagnostics, new ImportDiagnostic(ImportCodes.SnapshotDateFromFolder, ImportDiagnosticSeverity.Warning,
                    $"The snapshot date {Text(folders[0])} was taken from the folder name; ETP's file name and the workbook do not state it.",
                    dataSheet.Name)]
            };
        }

        return new([], [.. notes, new ImportDiagnostic(ImportCodes.SnapshotDateUnknown, ImportDiagnosticSeverity.Blocker,
            "The snapshot date of this stock or status report could not be found. Import the ETP file under its original name, or keep it in a folder named for its date.",
            dataSheet.Name)]) { NoTierDated = true };
    }

    /// <summary>The parent folder names of a source path, nearest last; the file name itself is never read here.</summary>
    internal static IReadOnlyList<string> Folders(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return [];
        var directory = Path.GetDirectoryName(sourcePath);
        return string.IsNullOrEmpty(directory) ? [] :
            directory.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
    }

    // Every data row must sit in exactly one dated block. A row no block holds cannot be dated, and a row two blocks of
    // different dates hold is ambiguous; neither falls through to a later tier. Two blocks holding rows of one snapshot
    // date are two readings of that day: planner 1 would store both and double the stock, so the sheet is refused
    // (the contract validator's CONTRACT_SNAPSHOT_DATE_DUPLICATE does not run under planner 1).
    private static SnapshotDating PerBlock(IReadOnlyList<((int? First, int? Last) Range, DateOnly Date, int? Block)> dated,
        SnapshotDateBasis basis, WorkbookSheet sheet, IReadOnlyList<int> rows, List<ImportDiagnostic> notes)
    {
        var blocks = dated.Select(block => new SnapshotBlock(sheet.Name, block.Range.First ?? sheet.HeaderRowNumber + 1,
            block.Range.Last ?? sheet.HeaderRowNumber, block.Date, basis) { BlockNo = block.Block }).ToArray();
        var repeated = blocks.Where(block => rows.Any(block.Contains)).GroupBy(block => block.SnapshotDate)
            .FirstOrDefault(group => group.Count() > 1)?.ToArray();
        if (repeated is not null)
            return new([], [.. notes, new ImportDiagnostic(ImportCodes.SnapshotDateRepeated, ImportDiagnosticSeverity.Blocker,
                $"Blocks {string.Join(", ", repeated.Select(BlockLabel))} of the {Source(basis)} share the snapshot date {Text(repeated[0].SnapshotDate)}. " +
                "Keep only the later export of that date.", sheet.Name, repeated[1].FirstRow)]);
        foreach (var row in rows)
        {
            var holding = blocks.Where(block => block.Contains(row)).Select(block => block.SnapshotDate).Distinct().ToArray();
            if (holding.Length == 0)
                return new([], [.. notes, new ImportDiagnostic(ImportCodes.SnapshotDateUnknown, ImportDiagnosticSeverity.Blocker,
                    $"Row {row} is in no dated block of the {Source(basis)}, so its snapshot date is not known.", sheet.Name, row)]);
            if (holding.Length > 1)
                return new([], [.. notes, Ambiguous(holding, $"row {row} of the {Source(basis)}", sheet.Name, row)]);
        }
        return new(blocks, notes);
    }

    private static SnapshotDating WholeFile(IReadOnlyCollection<DateOnly> dates, SnapshotDateBasis basis, string source,
        WorkbookSheet sheet, List<ImportDiagnostic> notes)
    {
        var distinct = dates.Distinct().Order().ToArray();
        if (distinct.Length > 1) return new([], [.. notes, Ambiguous(distinct, source, sheet.Name)]);
        var lastRow = sheet.Rows.Where(row => row.RowNumber > sheet.HeaderRowNumber).Select(row => row.RowNumber).DefaultIfEmpty(sheet.HeaderRowNumber).Max();
        return new([new SnapshotBlock(sheet.Name, sheet.HeaderRowNumber + 1, lastRow, distinct[0], basis)], notes);
    }

    private static (int?, int?) Range(int? first, int? last, WorkbookSheet sheet) =>
        first is null && last is null ? (sheet.HeaderRowNumber + 1, sheet.HeaderRowNumber) : (first, last);

    private static ImportDiagnostic Ambiguous(IEnumerable<DateOnly> dates, string source, string sheet, int? row = null) =>
        new(ImportCodes.SnapshotDateAmbiguous, ImportDiagnosticSeverity.Blocker,
            $"More than one snapshot date was found in {source} ({string.Join(", ", dates.Order().Select(Text))}); the importer never guesses between them.",
            sheet, row);

    private static string BlockLabel(SnapshotBlock block) =>
        block.BlockNo is { } number ? number.ToString(CultureInfo.InvariantCulture) : $"{block.FirstRow}:{block.LastRow}";

    private static string Source(SnapshotDateBasis basis) =>
        basis == SnapshotDateBasis.Contract ? "contract block table" : "Info block table";

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
