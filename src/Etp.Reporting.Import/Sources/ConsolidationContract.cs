using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>The consolidation rule of a contract workbook (contract 6), written in lowercase.</summary>
public enum ContractRule { Transactional, Snapshot, Current, Period, Empty }

/// <summary>Fixed names of the consolidation contract, version 1 (CONSOLIDATION-CONTRACT.md sections 2-4).</summary>
public static class ConsolidationContractLayout
{
    /// <summary><c>Info!A1</c> of a contract workbook.</summary>
    public const string Marker = "etp_contract";
    public const int CurrentVersion = 1;
    public const string InfoSheet = "Info";
    public const string DataSheet = "Data";
    public const string ExcludedSheet = "ETP_Excluded";
    public const string HistorySheet = "Snapshot History";

    /// <summary>The block-table header, exactly and in this order (contract 3.3).</summary>
    public static IReadOnlyList<string> BlockTableColumns { get; } =
    [
        "block", "sheet", "first_row", "last_row", "row_count", "source_file", "source_format", "source_sha256",
        "export_time", "period_from", "period_to", "period_basis", "snapshot_date", "raw_rows", "excluded_rows",
        "superseded_rows", "completeness", "disposition"
    ];

    /// <summary>The <c>ETP_Excluded</c> header (contract 4).</summary>
    public static IReadOnlyList<string> ExcludedColumns { get; } = ["block", "sheet", "row"];

    /// <summary>Sheets a data-sheet search skips (spec 6.1).</summary>
    public static IReadOnlyList<string> NonDataSheets { get; } = [InfoSheet, ExcludedSheet, HistorySheet];

    /// <summary>The contract's lowercase text for a rule, completeness or period basis.</summary>
    public static string ToContractText<TEnum>(this TEnum value) where TEnum : struct, Enum =>
        value.ToDatabaseCode().ToLowerInvariant();

    /// <summary>Reads a lowercase contract value exactly; any other spelling is not a value.</summary>
    public static bool TryParseContractText<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
            if (string.Equals(candidate.ToContractText(), text?.Trim(), StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        value = default;
        return false;
    }
}

/// <summary>The header keys of the Info machine section (contract 3.2). Keys are lowercase and exact.</summary>
public static class ContractKeys
{
    public const string FamilyCode = "family_code";
    public const string ReportName = "report_name";
    public const string StoreCode = "store_code";
    public const string BusinessUnit = "business_unit";
    public const string Rule = "rule";
    public const string DataSheet = "data_sheet";
    public const string HeaderRow = "header_row";
    public const string DataRows = "data_rows";
    public const string HistorySheet = "history_sheet";
    public const string HistoryExtraColumns = "history_extra_columns";
    public const string HistoryRows = "history_rows";
    public const string ExcludedSheet = "excluded_sheet";
    public const string ExcludedRows = "excluded_rows";
    public const string BlockCount = "block_count";
    public const string CoverageFrom = "coverage_from";
    public const string CoverageTo = "coverage_to";
    public const string BuiltAt = "built_at";
    public const string Builder = "builder";
    public const string Package = "package";

    /// <summary>Keys that must be present and non-blank in every contract workbook.</summary>
    public static IReadOnlyList<string> Required { get; } =
        [FamilyCode, StoreCode, BusinessUnit, Rule, DataSheet, HeaderRow, DataRows, BlockCount, BuiltAt, Builder];

    /// <summary>Every key version 1 defines; anything else is <c>CONTRACT_KEY_UNKNOWN</c>.</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        FamilyCode, ReportName, StoreCode, BusinessUnit, Rule, DataSheet, HeaderRow, DataRows, HistorySheet,
        HistoryExtraColumns, HistoryRows, ExcludedSheet, ExcludedRows, BlockCount, CoverageFrom, CoverageTo, BuiltAt,
        Builder, Package
    ];
}

/// <summary>One key row of the Info machine section, as written (contract 3.2).</summary>
public sealed record ContractHeaderKey(int RowNumber, string Key, string Value, bool IsText = true);

/// <summary>
/// <c>Info!B1</c> and the key rows. Values are kept as written so the validator can name what is wrong;
/// the typed accessors return null for a blank or unreadable value.
/// </summary>
public sealed record ConsolidationContractHeader(string VersionText, IReadOnlyList<ContractHeaderKey> Keys)
{
    public bool VersionIsText { get; init; } = true;

    public int? Version => ContractCell.Int(VersionText);

    /// <summary>The trimmed value of the first row with this exact key; null when absent or blank.</summary>
    public string? Value(string key)
    {
        var text = Keys.FirstOrDefault(row => string.Equals(row.Key.Trim(), key, StringComparison.Ordinal))?.Value.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public string? FamilyCode => Value(ContractKeys.FamilyCode);
    public string? ReportName => Value(ContractKeys.ReportName);
    public string? StoreCode => Value(ContractKeys.StoreCode);
    /// <summary><c>RETAIL</c> or <c>SERVICE</c>, as written.</summary>
    public string? BusinessUnit => Value(ContractKeys.BusinessUnit);
    public ContractRule? Rule => ConsolidationContractLayout.TryParseContractText<ContractRule>(Value(ContractKeys.Rule), out var rule) ? rule : null;
    public string? DataSheet => Value(ContractKeys.DataSheet);
    public int? HeaderRow => ContractCell.Int(Value(ContractKeys.HeaderRow));
    public int? DataRows => ContractCell.Int(Value(ContractKeys.DataRows));
    public string? HistorySheet => Value(ContractKeys.HistorySheet);
    public IReadOnlyList<string> HistoryExtraColumns => (Value(ContractKeys.HistoryExtraColumns) ?? "")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    public int? HistoryRows => ContractCell.Int(Value(ContractKeys.HistoryRows));
    public string? ExcludedSheet => Value(ContractKeys.ExcludedSheet);
    public int? ExcludedRows => ContractCell.Int(Value(ContractKeys.ExcludedRows));
    public int? BlockCount => ContractCell.Int(Value(ContractKeys.BlockCount));
    public DateOnly? CoverageFrom => ContractCell.Date(Value(ContractKeys.CoverageFrom));
    public DateOnly? CoverageTo => ContractCell.Date(Value(ContractKeys.CoverageTo));
    /// <summary><c>yyyy-MM-ddTHH:mm:ss+05:30</c>.</summary>
    public DateTimeOffset? BuiltAt => DateTimeOffset.TryParseExact(Value(ContractKeys.BuiltAt), "yyyy-MM-dd'T'HH:mm:sszzz",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var built) ? built : null;
    public string? Builder => Value(ContractKeys.Builder);
    public string? Package => Value(ContractKeys.Package);
}

/// <summary>One block-table row (contract 3.3): the cell text by column name, and typed readings of it.</summary>
public sealed record ContractBlockRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
{
    /// <summary>False when any cell of the row was not stored as text (<c>CONTRACT_CELL_NOT_TEXT</c>).</summary>
    public bool AllCellsText { get; init; } = true;

    /// <summary>The trimmed text of a column; empty when blank or absent.</summary>
    public string Text(string column) => Cells.TryGetValue(column, out var value) ? value.Trim() : "";

    public int? Block => ContractCell.Int(Text("block"));
    public string Sheet => Text("sheet");
    public int? FirstRow => ContractCell.Int(Text("first_row"));
    public int? LastRow => ContractCell.Int(Text("last_row"));
    public int? RowCount => ContractCell.Int(Text("row_count"));
    public string SourceFile => Text("source_file");
    public string SourceFormat => Text("source_format");
    public string SourceSha256 => Text("source_sha256");
    /// <summary>Null when blank or not one of the three contract formats.</summary>
    public ExportTime? ExportTime => Sources.ExportTime.TryParseContract(Text("export_time"), out var time) ? time : null;
    public DateOnly? PeriodFrom => ContractCell.Date(Text("period_from"));
    public DateOnly? PeriodTo => ContractCell.Date(Text("period_to"));
    public PeriodBasis? PeriodBasis =>
        ConsolidationContractLayout.TryParseContractText<PeriodBasis>(Text("period_basis"), out var basis) ? basis : null;
    public DateOnly? SnapshotDate => ContractCell.Date(Text("snapshot_date"));
    public int? RawRows => ContractCell.Int(Text("raw_rows"));
    public int? ExcludedRows => ContractCell.Int(Text("excluded_rows"));
    public int? SupersededRows => ContractCell.Int(Text("superseded_rows"));
    public BlockCompleteness? Completeness =>
        ConsolidationContractLayout.TryParseContractText<BlockCompleteness>(Text("completeness"), out var completeness) ? completeness : null;
    public string Disposition => Text("disposition");
}

/// <summary>One <c>ETP_Excluded</c> map row (contract 4): the cell text and typed readings of it.</summary>
public sealed record ContractExcludedRow(int RowNumber, IReadOnlyDictionary<string, string> Cells)
{
    public string Text(string column) => Cells.TryGetValue(column, out var value) ? value.Trim() : "";

    /// <summary>The block whose export contained the omitted row.</summary>
    public int? Block => ContractCell.Int(Text("block"));
    /// <summary>The sheet of the retained twin.</summary>
    public string Sheet => Text("sheet");
    /// <summary>The sheet row of the retained twin.</summary>
    public int? Row => ContractCell.Int(Text("row"));
}

/// <summary>
/// The machine section of a contract workbook as laid out on its sheets: the Info header and block table and,
/// when read, the <c>ETP_Excluded</c> map. Layout only; <see cref="IConsolidationContractValidator"/> judges it.
/// </summary>
public sealed record ConsolidationContract(
    ConsolidationContractHeader Header,
    IReadOnlyList<string> BlockTableHeader,
    IReadOnlyList<ContractBlockRow> Blocks)
{
    /// <summary>The Info row holding the block-table header.</summary>
    public int BlockTableHeaderRow { get; init; }
    public IReadOnlyList<string> ExcludedHeader { get; init; } = [];
    public IReadOnlyList<ContractExcludedRow> Excluded { get; init; } = [];
}

/// <summary>Readings of contract cell text shared by the reader and the validator.</summary>
public static class ContractCell
{
    /// <summary>A whole non-negative number written as digits; anything else is null.</summary>
    public static int? Int(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A <c>yyyy-MM-dd</c> date; anything else is null.</summary>
    public static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
}

/// <summary>The result of reading a workbook's Info sheet for a contract.</summary>
/// <param name="IsContract">True when <c>Info!A1</c> is <c>etp_contract</c>.</param>
/// <param name="Contract">The layout; null when the workbook is not a contract workbook or it could not be read.</param>
/// <param name="Diagnostics"><c>CONTRACT_UNREADABLE</c> when a contract workbook's keys or block table cannot be read.</param>
public sealed record ContractReadResult(bool IsContract, ConsolidationContract? Contract, IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public static ContractReadResult NotAContract { get; } = new(false, null, []);
}

/// <summary>
/// Reads the contract layout from the Info sheet, and the exclusion map from <c>ETP_Excluded</c> when present
/// (spec 6.1, layout only, from P1). It never judges the values.
/// </summary>
public interface IConsolidationContractReader
{
    ContractReadResult Read(WorkbookSnapshot workbook);
}
