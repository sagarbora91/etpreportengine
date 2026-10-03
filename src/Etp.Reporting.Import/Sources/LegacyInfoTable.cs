using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// One row of a pre-contract Info block table (spec 6.6; contract 11), header
/// <c>Package | Source file | Raw rows | Rows retained | Rows excluded | Period from | Period to | Data row block | Disposition</c>.
/// The export time comes from the 12-digit prefix of the source file name.
/// </summary>
public sealed record LegacyInfoBlock(int FirstRow, int LastRow, string SourceFile, ExportTime ExportTime)
{
    public string? Package { get; init; }
    public int? RawRows { get; init; }
    public int? RowsRetained { get; init; }
    public int? RowsExcluded { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public string? Disposition { get; init; }
    /// <summary>The Info row the block was read from.</summary>
    public int InfoRow { get; init; }
}

/// <summary>
/// A legacy Info block table. <see cref="Tiles"/> is true only when the ranges partition <c>2..last Data row</c>
/// with no gap or overlap and <c>Rows retained</c> adds up to the Data rows; otherwise the workbook is one
/// <see cref="BlockOrigin.WholeFile"/> block and the diagnostics carry <c>INFO_BLOCKS_UNUSABLE</c>.
/// </summary>
public sealed record LegacyInfoTable(bool Found, bool Tiles, IReadOnlyList<LegacyInfoBlock> Blocks, IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public static LegacyInfoTable None { get; } = new(false, false, [], []);
}

/// <summary>Reads the pre-contract Info block table of a consolidated workbook (spec 6.6, steps 1-3).</summary>
public interface ILegacyInfoBlockReader
{
    LegacyInfoTable Read(WorkbookSnapshot workbook, WorkbookSheet dataSheet);
}
