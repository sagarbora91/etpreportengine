using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>The <c>Snapshot History</c> sheet ready for staging, and the dated blocks read from its rows.</summary>
/// <param name="StagingSheet">The sheet with the trailing non-ETP columns removed, so it stages like <c>Data</c>.</param>
/// <param name="Blocks">Without a contract: one block per run of rows with the same <c>Snapshot_As_Of</c> and <c>SourceFile</c>.</param>
public sealed record HistorySheetRead(WorkbookSheet StagingSheet, IReadOnlyList<SourceBlock> Blocks, IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>
/// Reads the <c>Snapshot History</c> sheet of a rule-<c>current</c> workbook (spec 6.1, 6.4 tier 3; contract 5). The
/// sheet repeats the <c>Data</c> header and adds trailing columns that no ETP export carries (today
/// <c>Snapshot_As_Of</c> and <c>SourceFile</c>); they are removed before staging, so a history row stages exactly like
/// the raw export row it came from. In a contract workbook the block table places the blocks; without a contract the
/// rows are grouped into dated blocks by those two columns.
/// </summary>
public static class HistorySheetBlockReader
{
    public const string SnapshotAsOfColumn = "Snapshot_As_Of";
    public const string SourceFileColumn = "SourceFile";

    /// <summary>The trailing columns today's builder writes, and the default when a contract names none.</summary>
    public static IReadOnlyList<string> DefaultExtraColumns { get; } = [SnapshotAsOfColumn, SourceFileColumn];

    /// <summary>
    /// The history sheet with the named extra columns removed from the header and from every row, wherever they stand.
    /// Sheet row numbers are kept, so lineage still names the history row.
    /// </summary>
    public static WorkbookSheet StagingSheet(WorkbookSheet history, IReadOnlyList<string>? extraColumns = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        var extras = ContractSheetView.NormalizedHeaders(extraColumns is { Count: > 0 } ? extraColumns : DefaultExtraColumns);
        var keep = Enumerable.Range(0, history.Headers.Count)
            .Where(index => string.IsNullOrWhiteSpace(history.Headers[index]) ||
                !extras.Contains(ImportProfile.NormalizeHeader(history.Headers[index])))
            .ToArray();
        if (keep.Length == history.Headers.Count) return history;
        var dropped = Enumerable.Range(0, history.Headers.Count).Except(keep).ToHashSet();
        return history with
        {
            Headers = keep.Select(index => history.Headers[index]).ToArray(),
            Rows = history.Rows.Select(row => row with
            {
                Cells = row.Cells.Where((_, index) => !dropped.Contains(index)).ToArray()
            }).ToArray()
        };
    }

    /// <summary>
    /// Without a contract (spec 6.4 tier 3): one <see cref="BlockCompleteness.Legacy"/> block per run of consecutive rows
    /// with the same <c>Snapshot_As_Of</c> date and <c>SourceFile</c>, dated by that column (<see cref="SnapshotDateBasis.HistoryRows"/>)
    /// and timed by the <c>SourceFile</c> name (spec 6.3). Rows without a readable <c>Snapshot_As_Of</c> date cannot be
    /// dated and refuse the workbook with <c>SNAPSHOT_DATE_UNKNOWN</c>; nothing is guessed.
    /// </summary>
    /// <param name="firstBlockNo">The number of the first history block; the <c>Data</c> blocks come before it.</param>
    public static HistorySheetRead Read(WorkbookSheet history, int firstBlockNo, IReadOnlyList<string>? extraColumns = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        var view = new ContractSheetView(history);
        var diagnostics = new List<ImportDiagnostic>();
        var dateColumn = view.Column(SnapshotAsOfColumn);
        var fileColumn = view.Column(SourceFileColumn);
        var staging = StagingSheet(history, extraColumns);
        if (dateColumn is not { } dateIndex)
        {
            if (view.Rows.Count > 0)
                diagnostics.Add(new(ImportCodes.SnapshotDateUnknown, ImportDiagnosticSeverity.Blocker,
                    $"The {history.Name} sheet has no {SnapshotAsOfColumn} column, so its {view.Rows.Count} rows cannot be dated.",
                    history.Name) { Occurrences = view.Rows.Count });
            return new(staging, [], diagnostics);
        }

        var blocks = new List<SourceBlock>();
        var undated = new List<int>();
        (DateOnly Date, string File, int First, int Last, int Count)? run = null;
        foreach (var row in view.Rows)
        {
            if (ContractSheetView.Date(view.Value(row, dateIndex)) is not { } date)
            {
                undated.Add(row.RowNumber);
                continue;
            }
            var file = fileColumn is { } fileIndex ? ContractSheetView.Text(view.Value(row, fileIndex)) : "";
            if (run is { } current && current.Date == date && string.Equals(current.File, file, StringComparison.OrdinalIgnoreCase) &&
                current.Last == row.RowNumber - 1)
            {
                run = (current.Date, current.File, current.First, row.RowNumber, current.Count + 1);
                continue;
            }
            if (run is { } finished) blocks.Add(Block(firstBlockNo + blocks.Count, history.Name, finished));
            run = (date, file, row.RowNumber, row.RowNumber, 1);
        }
        if (run is { } last) blocks.Add(Block(firstBlockNo + blocks.Count, history.Name, last));
        if (undated.Count > 0)
            diagnostics.Add(new(ImportCodes.SnapshotDateUnknown, ImportDiagnosticSeverity.Blocker,
                $"{undated.Count} {history.Name} rows have no readable {SnapshotAsOfColumn} date (rows {DiagnosticLists.Of(undated)}).",
                history.Name, undated[0], SnapshotAsOfColumn) { Occurrences = undated.Count });
        return new(staging, blocks, diagnostics);
    }

    private static SourceBlock Block(int blockNo, string sheetName, (DateOnly Date, string File, int First, int Last, int Count) run) =>
        new(blockNo, sheetName, run.First, run.Last, run.Count, BlockCompleteness.Legacy, BlockOrigin.HistoryRows,
            ExportNameParser.Parse(run.File))
        {
            SourceFileName = run.File.Length == 0 ? null : run.File,
            SnapshotDate = run.Date,
            SnapshotDateBasis = SnapshotDateBasis.HistoryRows
        };
}
