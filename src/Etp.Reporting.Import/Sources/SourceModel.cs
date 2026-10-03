using Etp.Reporting.Import.Diagnostics;

namespace Etp.Reporting.Import.Sources;

/// <summary>What kind of workbook a source is, in order of detection (spec 6.1; <c>import_files.source_kind</c>).</summary>
public enum SourceKind { Raw, Consolidated, ConsolidatedLegacy, Reviewed }

/// <summary>Whether a block holds its export's full row multiset (spec 3; <c>import_file_blocks.completeness</c>).</summary>
public enum BlockCompleteness { Complete, Delta, Trimmed, Empty, Legacy }

/// <summary>Where a block's description came from (<c>import_file_blocks.origin</c>).</summary>
public enum BlockOrigin { Raw, Contract, InfoLegacy, HistoryRows, SourceColumns, WholeFile, Backfill }

/// <summary>How a block's period is known (<c>import_file_blocks.period_basis</c>).</summary>
public enum PeriodBasis { None, Declared, Observed }

/// <summary>The tier that dated a snapshot (spec 6.4; <c>import_file_blocks.snapshot_date_basis</c>).</summary>
public enum SnapshotDateBasis
{
    Column, Contract, InfoBlock, HistoryRows, SourceColumns, ExportName, InfoCoverage, Folder, Sibling, LegacyStamp
}

/// <summary>
/// Where one row of a source sits: its block, sheet and sheet row. A virtual row (spec 6.5) sits on
/// <c>ETP_Excluded</c> at its map row and carries its twin's values.
/// </summary>
public readonly record struct RowLocator(int BlockNo, string SheetName, int SourceRowNumber, bool IsVirtual = false);

/// <summary>A staged row placed in its block: canonical field name to staged value.</summary>
public sealed record SourceRow(RowLocator Locator, IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// The rows of one ETP export inside a source (spec 3, 5.2 <c>import_file_blocks</c>). Block numbers are
/// append order inside a workbook; exports are ordered by <see cref="ExportTime"/>, never by block number.
/// A raw export is exactly one <see cref="BlockCompleteness.Complete"/> block.
/// </summary>
public sealed record SourceBlock(
    int BlockNo,
    string SheetName,
    int? FirstRow,
    int? LastRow,
    int RowCount,
    BlockCompleteness Completeness,
    BlockOrigin Origin,
    ExportTime ExportTime)
{
    public int VirtualRowCount { get; init; }
    public string? SourceFileName { get; init; }
    /// <summary><c>xlsx</c> or <c>csv</c>.</summary>
    public string? SourceFormat { get; init; }
    /// <summary>SHA-256 of the raw export's bytes, lowercase hex, when known.</summary>
    public string? SourceSha256 { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public PeriodBasis PeriodBasis { get; init; } = PeriodBasis.None;
    public DateOnly? SnapshotDate { get; init; }
    public SnapshotDateBasis? SnapshotDateBasis { get; init; }
    public int? RawRows { get; init; }
    public int? ExcludedRows { get; init; }
    public int? SupersededRows { get; init; }
    /// <summary>The builder's human text; ignored by the importer.</summary>
    public string? Disposition { get; init; }

    /// <summary>A complete or delta block rebuilds into its raw export: it gets a content hash and takes part in the absence check.</summary>
    public bool IsRebuildable => Completeness is BlockCompleteness.Complete or BlockCompleteness.Delta;

    public bool Contains(string sheetName, int sheetRow) =>
        FirstRow is { } first && LastRow is { } last && sheetRow >= first && sheetRow <= last &&
        string.Equals(SheetName, sheetName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One <c>ETP_Excluded</c> map row (contract 4): a row of block <see cref="BlockNo"/>'s export that the builder
/// left out because it exactly repeated the physical twin row of a lower-numbered block. It is landed as its own
/// row with the twin's values (<c>import_file_block_copies</c>).
/// </summary>
public sealed record VirtualRow(int BlockNo, int MapRow, string TwinSheet, int TwinRow)
{
    public RowLocator Locator => new(BlockNo, ConsolidationContractLayout.ExcludedSheet, MapRow, IsVirtual: true);
}

/// <summary>Everything the importer knows about a source before projecting documents (spec 6).</summary>
public sealed record SourceDescription(
    SourceKind Kind,
    IReadOnlyList<SourceBlock> Blocks,
    IReadOnlyList<VirtualRow> VirtualRows,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    /// <summary>The contract version of a <see cref="SourceKind.Consolidated"/> workbook.</summary>
    public int? ContractVersion { get; init; }
    public ConsolidationContract? Contract { get; init; }
    /// <summary>
    /// Sheet rows that belong to no block, and why. Every staged row of the workbook is in exactly one block or in one
    /// of these; an integrator landing every staged row gives these their own disposition and decides nothing from them.
    /// </summary>
    public IReadOnlyList<SkippedBlock> SkippedBlocks { get; init; } = [];
    public bool HasBlockers => Diagnostics.Any(diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker);

    /// <summary>The skipped block holding a sheet row, or null when a block (or nothing) holds it.</summary>
    public SkippedBlock? SkippedBlockOf(string sheetName, int sheetRow) =>
        SkippedBlocks.FirstOrDefault(skipped => skipped.Contains(sheetName, sheetRow));
}

/// <summary>
/// A contract block that is not a block of the source: a <c>Snapshot History</c> block repeating the same export as the
/// <c>Data</c> block (contract 5, <c>CONTRACT_HISTORY_REPEATS_DATA</c>). Its rows are counted once, from
/// <see cref="AttestedByBlockNo"/>; they are landed for lineage only and observe nothing.
/// </summary>
public sealed record SkippedBlock(int BlockNo, string SheetName, int FirstRow, int LastRow, string Code, int AttestedByBlockNo)
{
    public bool Contains(string sheetName, int sheetRow) =>
        sheetRow >= FirstRow && sheetRow <= LastRow && string.Equals(SheetName, sheetName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The snapshot date of a run of sheet rows (spec 6.4). Planner 1 stamps every R010 row with the date of the
/// snapshot block that holds it, instead of one date for the whole file.
/// </summary>
public sealed record SnapshotBlock(string SheetName, int FirstRow, int LastRow, DateOnly SnapshotDate, SnapshotDateBasis Basis)
{
    public int? BlockNo { get; init; }

    public bool Contains(int sheetRow) => sheetRow >= FirstRow && sheetRow <= LastRow;

    /// <summary>The date of the block holding <paramref name="sheetRow"/>, or null when no block holds it.</summary>
    public static DateOnly? DateOf(IReadOnlyList<SnapshotBlock> blocks, string sheetName, int sheetRow) =>
        blocks.FirstOrDefault(block => block.Contains(sheetRow) &&
            string.Equals(block.SheetName, sheetName, StringComparison.OrdinalIgnoreCase))?.SnapshotDate;
}
