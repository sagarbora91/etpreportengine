using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Describes a source as blocks (spec 6.1, 6.2, 6.5, 6.6), detecting its kind in this order:
/// <list type="number">
/// <item><c>CONSOLIDATED</c>: <c>Info!A1</c> is <c>etp_contract</c>. The block table and the exclusion map, judged by
/// <see cref="IConsolidationContractValidator"/>; any contract blocker refuses the workbook.</item>
/// <item><c>CONSOLIDATED_LEGACY</c>: a pre-contract Info block table. Tiling Info blocks, or one whole-file block, plus
/// one block per dated run of history rows. Every block is <c>LEGACY</c>.</item>
/// <item><c>REVIEWED</c>: the Owner ticked "Import as reviewed file". One complete block, export time unknown.</item>
/// <item><c>CONSOLIDATED_LEGACY</c> again: a <c>Snapshot History</c> sheet without an Info block table (spec 6.4 tier 3).</item>
/// <item><c>RAW</c>: one complete block over all rows, timed by the file name (spec 6.3).</item>
/// </list>
/// It reads no database. S027/S028 consolidation columns (<c>SourceColumnBlockReader</c>) arrive with P8.
/// </summary>
public sealed class SourceDescriptionReader(
    IConsolidationContractReader contractReader,
    ILegacyInfoBlockReader legacyInfoReader,
    IConsolidationContractValidator validator) : ISourceDescriptionReader
{
    public SourceDescriptionReader(IConsolidationContractReader contractReader, ILegacyInfoBlockReader legacyInfoReader)
        : this(contractReader, legacyInfoReader, new ConsolidationContractValidator())
    {
    }

    public SourceDescription Describe(SourceDescriptionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workbook);
        ArgumentNullException.ThrowIfNull(request.Family);
        ArgumentNullException.ThrowIfNull(request.DataSheet);

        var contract = contractReader.Read(request.Workbook);
        if (contract.IsContract) return DescribeContract(request, contract);

        var legacy = legacyInfoReader.Read(request.Workbook, request.DataSheet);
        var history = ContractSheetView.Find(request.Workbook, ConsolidationContractLayout.HistorySheet);
        if (legacy.Found) return DescribeLegacy(request, legacy, history);
        if (request.ImportAsReviewed)
            return new SourceDescription(SourceKind.Reviewed, [WholeSheetBlock(request, BlockCompleteness.Complete, BlockOrigin.Raw, ExportTime.Unknown)], [], []);
        // Not in the spec 6.1 table, but its intent (spec 6.4 tier 3, contract 11): no raw ETP export has a Snapshot
        // History sheet, so a workbook with one is a pre-contract consolidation. The Owner's "reviewed" tick comes first.
        if (history is not null) return DescribeLegacy(request, legacy, history);
        return new SourceDescription(SourceKind.Raw, [RawBlock(request)], [], []);
    }

    public IReadOnlyList<SourceRow> RebuildBlockRows(SourceDescription source, SourceBlock block, IReadOnlyList<SourceRow> stagedRows) =>
        RebuildRows(source, block, stagedRows);

    /// <summary>
    /// The rows of one block as its export held them (spec 6.5 rebuild): its physical rows, then, for a delta or
    /// trimmed block, one virtual row per <c>ETP_Excluded</c> map row with the twin's staged values (timestamps
    /// included) and its own locator on <c>ETP_Excluded</c>. A legacy block is its physical rows only.
    /// <paramref name="stagedRows"/> must be every staged row of the workbook, on every sheet (<c>Data</c> and
    /// <c>Snapshot History</c>), because a twin lives in a lower-numbered block. A map row whose twin is not among them
    /// (staging left it out, or the caller passed too few rows) gives no virtual row; the rebuild then has fewer virtual
    /// rows than <see cref="SourceBlock.VirtualRowCount"/> and <see cref="ExportIdentity.ContentSha256"/> gives it no hash.
    /// </summary>
    public static IReadOnlyList<SourceRow> RebuildRows(SourceDescription source, SourceBlock block, IReadOnlyList<SourceRow> stagedRows)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(stagedRows);
        var rows = new List<SourceRow>();
        foreach (var row in stagedRows.Where(row => !row.Locator.IsVirtual && block.Contains(row.Locator.SheetName, row.Locator.SourceRowNumber))
                     .OrderBy(row => row.Locator.SourceRowNumber))
            rows.Add(row with { Locator = row.Locator with { BlockNo = block.BlockNo } });
        if (block.Completeness is not (BlockCompleteness.Delta or BlockCompleteness.Trimmed)) return rows;

        var physical = new Dictionary<(string Sheet, int Row), SourceRow>();
        foreach (var row in stagedRows.Where(row => !row.Locator.IsVirtual))
            physical.TryAdd((Sheet(row.Locator.SheetName), row.Locator.SourceRowNumber), row);
        foreach (var copy in source.VirtualRows.Where(copy => copy.BlockNo == block.BlockNo).OrderBy(copy => copy.MapRow))
            if (physical.TryGetValue((Sheet(copy.TwinSheet), copy.TwinRow), out var twin))
                rows.Add(new SourceRow(copy.Locator, twin.Values));
        return rows;
    }

    private static string Sheet(string name) => name.Trim().ToUpperInvariant();

    private SourceDescription DescribeContract(SourceDescriptionRequest request, ContractReadResult read)
    {
        var diagnostics = read.Diagnostics.ToList();
        if (read.Contract is not { } contract)
            return new SourceDescription(SourceKind.Consolidated, [], [], diagnostics);
        var result = validator.Validate(new ContractValidationRequest(request.Workbook, contract, request.Family));
        diagnostics.AddRange(result.Diagnostics);
        return new SourceDescription(SourceKind.Consolidated, result.Blocks, result.VirtualRows, diagnostics)
        {
            ContractVersion = contract.Header.Version,
            Contract = contract
        };
    }

    private static SourceDescription DescribeLegacy(SourceDescriptionRequest request, LegacyInfoTable legacy, WorkbookSheet? history)
    {
        var diagnostics = legacy.Diagnostics.ToList();
        var blocks = new List<SourceBlock>();
        var snapshot = IsSnapshotFamily(request.Family);
        if (legacy.Found && legacy.Tiles)
        {
            foreach (var info in legacy.Blocks.OrderBy(info => info.FirstRow))
                blocks.Add(new SourceBlock(blocks.Count + 1, request.DataSheet.Name, info.FirstRow, info.LastRow,
                    info.LastRow - info.FirstRow + 1, BlockCompleteness.Legacy, BlockOrigin.InfoLegacy, info.ExportTime)
                {
                    SourceFileName = info.SourceFile,
                    SourceFormat = Format(info.SourceFile),
                    PeriodFrom = info.PeriodFrom,
                    PeriodTo = info.PeriodTo,
                    PeriodBasis = info.PeriodFrom is not null && info.PeriodTo is not null ? PeriodBasis.Declared : PeriodBasis.None,
                    // Spec 6.4 tier 2: each tiling block of a snapshot family is dated by its source file's export date.
                    SnapshotDate = snapshot ? info.ExportTime.ExportDate : null,
                    SnapshotDateBasis = snapshot && info.ExportTime.IsKnown ? SnapshotDateBasis.InfoBlock : null,
                    RawRows = info.RawRows,
                    ExcludedRows = info.RowsExcluded,
                    Disposition = info.Disposition
                });
        }
        else
            blocks.Add(WholeSheetBlock(request, BlockCompleteness.Legacy, BlockOrigin.WholeFile, ExportTime.Unknown));

        if (history is not null)
        {
            var read = HistorySheetBlockReader.Read(history, blocks.Count + 1);
            blocks.AddRange(read.Blocks);
            diagnostics.AddRange(read.Diagnostics);
        }
        return new SourceDescription(SourceKind.ConsolidatedLegacy, blocks, [], diagnostics);
    }

    private static SourceBlock RawBlock(SourceDescriptionRequest request)
    {
        var time = ExportNameParser.Parse(request.Workbook.FileName);
        var block = WholeSheetBlock(request, BlockCompleteness.Complete, BlockOrigin.Raw, time) with
        {
            SourceSha256 = string.IsNullOrWhiteSpace(request.Workbook.Sha256) ? null : request.Workbook.Sha256.Trim().ToLowerInvariant(),
            RawRows = request.DataSheet.Rows.Count
        };
        if (IsSnapshotFamily(request.Family))
            // Spec 6.4 tier 4 is the only tier a raw export carries itself; later tiers belong to the snapshot-date resolver.
            return block with
            {
                SnapshotDate = time.ExportDate,
                SnapshotDateBasis = time.IsKnown ? SnapshotDateBasis.ExportName : null
            };
        if (DeclaredPeriod.FromPath(request.Workbook.SourcePath) is { } declared)
            return block with { PeriodFrom = declared.From, PeriodTo = declared.To, PeriodBasis = PeriodBasis.Declared };
        if (ObservedPeriod(request) is { } observed)
            return block with { PeriodFrom = observed.From, PeriodTo = observed.To, PeriodBasis = PeriodBasis.Observed };
        return block;
    }

    private static SourceBlock WholeSheetBlock(SourceDescriptionRequest request, BlockCompleteness completeness, BlockOrigin origin, ExportTime time)
    {
        var rows = request.DataSheet.Rows;
        return new SourceBlock(1, request.DataSheet.Name, rows.Count == 0 ? null : rows.Min(row => row.RowNumber),
            rows.Count == 0 ? null : rows.Max(row => row.RowNumber), rows.Count, completeness, origin, time)
        {
            SourceFileName = Path.GetFileName(request.Workbook.FileName),
            SourceFormat = Format(request.Workbook.FileName)
        };
    }

    /// <summary>Min..max of the family's primary date over the data rows (spec 6.2, observed coverage).</summary>
    private static (DateOnly From, DateOnly To)? ObservedPeriod(SourceDescriptionRequest request)
    {
        if (request.Family.PrimaryDateHeader is not { } header) return null;
        var view = new ContractSheetView(request.DataSheet);
        if (view.Column(header) is not { } column) return null;
        var dates = view.Rows.Select(row => ContractSheetView.Date(view.Value(row, column))).OfType<DateOnly>().ToArray();
        return dates.Length == 0 ? null : (dates.Min(), dates.Max());
    }

    /// <summary>
    /// A family whose documents are snapshots dated by block: an identity of Snapshot scope, or, before the family has
    /// an identity, a family without a row date.
    /// </summary>
    private static bool IsSnapshotFamily(EtpReportFamily family) => family.Identity is { } identity
        ? identity.Scope == DocumentScope.Snapshot && identity.SnapshotDateColumn is null
        : family.PrimaryDateHeader is null && ConsolidationContractValidator.SnapshotDateField(family) is null;

    private static string? Format(string? fileName) => Path.GetExtension(fileName ?? "").TrimStart('.').ToLowerInvariant() switch
    {
        "xlsx" => "xlsx",
        "csv" => "csv",
        _ => null
    };
}

/// <summary>
/// The period a pack or ZIP folder name declares (spec 6.2; contract rule 5), e.g.
/// <c>TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026</c>. Months are English, full or three-letter.
/// </summary>
public static partial class DeclaredPeriod
{
    [GeneratedRegex(@"(?<!\d)(\d{1,2})\s+([A-Za-z]{3,9})\.?\s+(\d{4})\s+TO\s+(\d{1,2})\s+([A-Za-z]{3,9})\.?\s+(\d{4})(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>The period in one name; null when the name declares none or a date does not exist.</summary>
    public static (DateOnly From, DateOnly To)? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var match = Pattern().Match(name);
        if (!match.Success) return null;
        var from = Date(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
        var to = Date(match.Groups[4].Value, match.Groups[5].Value, match.Groups[6].Value);
        return from is { } start && to is { } end && start <= end ? (start, end) : null;
    }

    /// <summary>The nearest parent folder (or ZIP) of <paramref name="path"/> whose name declares a period.</summary>
    public static (DateOnly From, DateOnly To)? FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var folders = Path.GetDirectoryName(path)?.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries) ?? [];
        return folders.Reverse().Select(Parse).FirstOrDefault(period => period is not null);
    }

    private static DateOnly? Date(string day, string month, string year)
    {
        var name = month.ToUpperInvariant();
        var number = Array.FindIndex(CultureInfo.InvariantCulture.DateTimeFormat.MonthNames,
            candidate => candidate.Length > 0 && (candidate.ToUpperInvariant() == name ||
                (name.Length == 3 && candidate.ToUpperInvariant().StartsWith(name, StringComparison.Ordinal)) ||
                (name == "SEPT" && candidate == "September")));
        if (number < 0) return null;
        var dayNo = int.Parse(day, CultureInfo.InvariantCulture);
        var yearNo = int.Parse(year, CultureInfo.InvariantCulture);
        return dayNo >= 1 && dayNo <= DateTime.DaysInMonth(yearNo, number + 1) ? new DateOnly(yearNo, number + 1, dayNo) : null;
    }
}
