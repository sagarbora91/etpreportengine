using System.Globalization;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.DailyInputs;

/// <summary>
/// The daily-inputs workbook (family D001, decision 30): one sheet of key/value rows a shop PC fills in each day.
/// D001 is deliberately not in EtpReportFamilies.json: it has no landing table, header profile or staging. It is
/// recognised by content (<see cref="IsDailyInputsWorkbook"/>), never by file name.
/// </summary>
public static class DailyInputsTemplate
{
    public const string FamilyCode = "D001", SheetName = "Daily Inputs", CurrentVersion = "ETP-DAILY-INPUTS-1";

    /// <summary>Every template version starts with this; the parser accepts only <see cref="CurrentVersion"/>.</summary>
    public const string VersionPrefix = "ETP-DAILY-INPUTS-";

    /// <summary>
    /// How many days back a file may go (open question 3, recommended answer). Older corrections are made on the
    /// screen by the Owner. The template's date validation and the DAILY_INPUTS_DATE_TOO_OLD catalogue text use it.
    /// </summary>
    public const int LookbackDays = 31;

    public const int MaxTextLength = 1000;
    public const int MaxFilledByLength = 100;

    /// <summary>The OneDrive file name starts with this (the onedrive-scripts lane's dailyInputsPattern).</summary>
    public const string FileNamePrefix = "ETP-Daily-Inputs";

    // Keys in column A of the rows above the field rows.
    public const string TemplateKey = "TEMPLATE", KindKey = "KIND", StoreKey = "STORE", BusinessDateKey = "BUSINESS_DATE", FilledByKey = "FILLED_BY";

    public const string RetailText = "RETAIL", ServiceText = "SERVICE";

    public static IReadOnlyList<string> Headers { get; } = ["Field code", "Input", "Value", "Note"];

    // RETAIL = applies_to Store or Cash; SERVICE = applies_to Service. The two sets are disjoint, so the Titan World
    // RETAIL and SERVICE files for one day never write the same field. Labels are manual_input_definitions.display_name.
    private static readonly IReadOnlyList<DailyInputsField> RetailFields =
    [
        new("WALK_INS", "Walk-ins", DailyInputValueKind.Quantity, false),
        new("OPENING_CASH", "Opening cash", DailyInputValueKind.Money, false),
        new("EXPENSES", "Expenses", DailyInputValueKind.Money, false),
        new("CASH_DEPOSIT", "Cash deposit", DailyInputValueKind.Money, false),
        new("CASH_ADJUSTMENT", "Cash adjustment", DailyInputValueKind.Money, true),
        new("CLOSING_CASH_COUNTED", "Counted closing cash", DailyInputValueKind.Money, false),
        new("OPERATIONAL_REMARK", "Operational remark", DailyInputValueKind.Text, false)
    ];

    private static readonly IReadOnlyList<DailyInputsField> ServiceFields =
    [
        new("SERVICE_CASH", "Service cash", DailyInputValueKind.Money, false),
        new("SERVICE_CARD", "Service card", DailyInputValueKind.Money, false),
        new("SERVICE_UPI", "Service UPI", DailyInputValueKind.Money, false),
        new("SERVICE_WDC", "Service WDC", DailyInputValueKind.Money, false),
        new("WCC_WALKIN", "WCC walk-ins", DailyInputValueKind.Quantity, false),
        new("WCC_SALES", "WCC sales", DailyInputValueKind.Money, false),
        new("WDC_BILLS", "WDC bills", DailyInputValueKind.Quantity, false)
    ];

    /// <summary>The fields of one sheet type, in row order.</summary>
    public static IReadOnlyList<DailyInputsField> FieldsFor(DailyInputsKind kind) => kind switch
    {
        DailyInputsKind.Retail => RetailFields,
        DailyInputsKind.Service => ServiceFields,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Every field either sheet type carries.</summary>
    public static IReadOnlyList<DailyInputsField> AllFields { get; } = [.. RetailFields, .. ServiceFields];

    public static DailyInputsField? FindField(string? fieldCode) =>
        fieldCode is null ? null : AllFields.FirstOrDefault(f => string.Equals(f.FieldCode, fieldCode, StringComparison.OrdinalIgnoreCase));

    /// <summary>The sheet type a field belongs to, or null for a code neither template carries.</summary>
    public static DailyInputsKind? KindOf(string? fieldCode) =>
        FindField(fieldCode) is not { } field ? null
        : RetailFields.Contains(field) ? DailyInputsKind.Retail : DailyInputsKind.Service;

    public static string KindText(DailyInputsKind kind) => kind == DailyInputsKind.Retail ? RetailText : ServiceText;

    public static DailyInputsKind? ParseKind(string? text) => text?.Trim().ToUpperInvariant() switch
    {
        RetailText => DailyInputsKind.Retail,
        ServiceText => DailyInputsKind.Service,
        _ => null
    };

    /// <summary>The file name staff save each day: <c>ETP-Daily-Inputs-&lt;STORE&gt;-&lt;KIND&gt;-&lt;yyyy-MM-dd&gt;.xlsx</c>.</summary>
    public static string FileName(string storeCode, DailyInputsKind kind, DateOnly businessDate) =>
        $"{FileNamePrefix}-{storeCode.Trim().ToUpperInvariant()}-{KindText(kind)}-{businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx";

    /// <summary>
    /// True when the workbook is a daily-inputs sheet: an .xlsx with a sheet named <see cref="SheetName"/>
    /// (case-insensitive) whose headers are exactly <see cref="Headers"/> and whose first data row is
    /// <c>TEMPLATE | … | ETP-DAILY-INPUTS-…</c>. The file name is never used. Run before header matching.
    /// </summary>
    public static bool IsDailyInputsWorkbook(WorkbookSnapshot workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (!string.Equals(Path.GetExtension(workbook.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) return false;
        var sheet = FindSheet(workbook);
        if (sheet is null || !HasTemplateHeaders(sheet) || sheet.Rows.Count == 0) return false;
        var first = sheet.Rows[0];
        return string.Equals(CellText(first, 0), TemplateKey, StringComparison.OrdinalIgnoreCase)
            && (CellText(first, 2)?.StartsWith(VersionPrefix, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    internal static WorkbookSheet? FindSheet(WorkbookSnapshot workbook) =>
        workbook.Sheets.FirstOrDefault(s => string.Equals(s.Name?.Trim(), SheetName, StringComparison.OrdinalIgnoreCase));

    internal static bool HasTemplateHeaders(WorkbookSheet sheet)
    {
        // Formatted but empty cells to the right of D can widen the header row; only trailing blanks are ignored.
        var headers = sheet.Headers.Select(h => h?.Trim() ?? string.Empty).ToList();
        while (headers.Count > 0 && headers[^1].Length == 0) headers.RemoveAt(headers.Count - 1);
        return headers.Count == Headers.Count
            && headers.Zip(Headers).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A cell's trimmed text, or null when it is missing or blank.</summary>
    internal static string? CellText(WorkbookRow row, int column)
    {
        if (column >= row.Cells.Count) return null;
        var cell = row.Cells[column];
        var text = cell.Value switch
        {
            null => null,
            string s => s,
            _ => cell.DisplayText ?? Convert.ToString(cell.Value, CultureInfo.InvariantCulture)
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
