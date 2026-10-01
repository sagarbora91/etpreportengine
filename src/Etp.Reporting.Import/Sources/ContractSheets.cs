using System.Globalization;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// A worksheet of a consolidated workbook with its columns found by normalised header and its rows by sheet row
/// number. Horizontally doubled ETP layouts are collapsed first, as the preflight collapses them.
/// </summary>
internal sealed class ContractSheetView
{
    private static readonly TypedCellConverter Converter = new();
    private readonly Dictionary<string, int> columns = new(StringComparer.Ordinal);
    private readonly Dictionary<int, WorkbookRow> rows = [];

    public ContractSheetView(WorkbookSheet sheet)
    {
        Sheet = WorkbookLayoutNormalizer.Normalize(sheet).Sheet ?? sheet;
        for (var index = 0; index < Sheet.Headers.Count; index++)
            if (!string.IsNullOrWhiteSpace(Sheet.Headers[index]))
                columns.TryAdd(ImportProfile.NormalizeHeader(Sheet.Headers[index]), index);
        foreach (var row in Sheet.Rows) rows.TryAdd(row.RowNumber, row);
    }

    public WorkbookSheet Sheet { get; }
    public string Name => Sheet.Name;
    public IReadOnlyList<WorkbookRow> Rows => Sheet.Rows;
    public IReadOnlyList<string> Headers => Sheet.Headers;

    /// <summary>The highest sheet row the contract allows for this sheet: header row 1 plus one row per data row.</summary>
    public int LastDataRow => 1 + Sheet.Rows.Count;

    /// <summary>True when the header is on row 1 and the data rows are 2..n with no blank row between them.</summary>
    public bool RowsAreContiguous => Sheet.HeaderRowNumber == 1 &&
        Sheet.Rows.Select((row, index) => row.RowNumber == index + 2).All(contiguous => contiguous);

    public bool HasRow(int rowNumber) => rows.ContainsKey(rowNumber);

    public int? Column(string header) =>
        !string.IsNullOrWhiteSpace(header) && columns.TryGetValue(ImportProfile.NormalizeHeader(header), out var index) ? index : null;

    public object? Value(WorkbookRow row, int column) => column < row.Cells.Count ? row.Cells[column].Value : null;

    public object? Value(int rowNumber, int column) => rows.TryGetValue(rowNumber, out var row) ? Value(row, column) : null;

    /// <summary>A cell read as a date the way staging reads it: Excel dates, <c>yyyyMMdd</c> numbers and date text.</summary>
    public static DateOnly? Date(object? value) =>
        Converter.Convert(value, CanonicalDataType.Date, isRequired: false).Value as DateOnly?;

    /// <summary>The trimmed text of a cell; empty when blank.</summary>
    public static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";

    /// <summary>The sheet of the workbook with this name, matched without regard to case (contract 2).</summary>
    public static WorkbookSheet? Find(WorkbookSnapshot workbook, string? name) => string.IsNullOrWhiteSpace(name) ? null :
        workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The contract spelling of a block's sheet: <c>Data</c> or <c>Snapshot History</c>; null for any other.</summary>
    public static string? CanonicalBlockSheet(string? name) =>
        string.Equals(name?.Trim(), ConsolidationContractLayout.DataSheet, StringComparison.OrdinalIgnoreCase) ? ConsolidationContractLayout.DataSheet
        : string.Equals(name?.Trim(), ConsolidationContractLayout.HistorySheet, StringComparison.OrdinalIgnoreCase) ? ConsolidationContractLayout.HistorySheet
        : null;

    /// <summary>The sheet of a raw export whose header is the family's ETP header, collapsed like the preflight does.</summary>
    public static ContractSheetView? FamilySheet(WorkbookSnapshot workbook, EtpReportFamily family)
    {
        var expected = NormalizedHeaders(family.Headers);
        return workbook.Sheets
            .Where(sheet => !ConsolidationContractLayout.NonDataSheets.Contains(sheet.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(sheet => new ContractSheetView(sheet))
            .FirstOrDefault(view => NormalizedHeaders(view.Headers).SetEquals(expected));
    }

    public static HashSet<string> NormalizedHeaders(IEnumerable<string> headers) =>
        headers.Where(header => !string.IsNullOrWhiteSpace(header)).Select(ImportProfile.NormalizeHeader).ToHashSet(StringComparer.Ordinal);
}

/// <summary>Short, value-free lists for diagnostic messages (issue messages are stored in 500 characters).</summary>
internal static class DiagnosticLists
{
    /// <summary>Up to ten items, then a count of the rest.</summary>
    public static string Of<T>(IReadOnlyCollection<T> items) => items.Count <= 10
        ? string.Join(", ", items)
        : $"{string.Join(", ", items.Take(10))} and {items.Count - 10} more";

    /// <summary>Text the builder wrote, cut to a length that keeps the message short.</summary>
    public static string Clip(string? text, int length = 40)
    {
        var value = text?.Trim() ?? "";
        return value.Length <= length ? value : value[..length] + "…";
    }
}
