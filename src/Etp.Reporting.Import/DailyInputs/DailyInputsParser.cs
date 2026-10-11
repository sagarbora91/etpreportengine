using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.DailyInputs;

/// <summary>
/// Reads a daily-inputs workbook (design section 3, parser steps 1-7). It checks structure and formats only; the
/// applier checks the database. Any error rejects the whole file. Issue messages are the value-free catalogue
/// templates, so History keeps them as written.
/// </summary>
public static partial class DailyInputsParser
{
    private const int KeyColumn = 0, ValueColumn = 2;

    // decimal(19,4) holds values below 10^15.
    private const decimal MaxMagnitude = 999_999_999_999_999.9999m;

    private static readonly string[] TextDateFormats =
    [
        "yyyy-MM-dd", "yyyy-M-d",
        "dd-MM-yyyy", "d-M-yyyy",
        "dd/MM/yyyy", "d/M/yyyy",
        "dd-MMM-yyyy", "d-MMM-yyyy"
    ];

    private static readonly CultureInfo[] DateCultures = [CultureInfo.GetCultureInfo("en-IN"), CultureInfo.InvariantCulture];

    [GeneratedRegex(@"^ETP-Daily-Inputs-([A-Z0-9]+)-(RETAIL|SERVICE)-(\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    public static DailyInputsParseResult Parse(WorkbookSnapshot workbook, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var errors = new List<DailyInputsIssue>();
        var notes = new List<DailyInputsIssue>();

        var sheet = DailyInputsTemplate.FindSheet(workbook);
        if (sheet is null || !DailyInputsTemplate.HasTemplateHeaders(sheet))
            return Fail(errors, DailyInputsCodes.TemplateVersion, null);

        // Key/value rows above the field rows, found by key so a moved row still reads.
        WorkbookRow? Keyed(string key) => sheet.Rows.FirstOrDefault(r =>
            string.Equals(DailyInputsTemplate.CellText(r, KeyColumn), key, StringComparison.OrdinalIgnoreCase));

        // 1. Template version.
        var templateRow = Keyed(DailyInputsTemplate.TemplateKey);
        var version = templateRow is null ? null : DailyInputsTemplate.CellText(templateRow, ValueColumn);
        if (!string.Equals(version, DailyInputsTemplate.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            return Fail(errors, DailyInputsCodes.TemplateVersion, templateRow?.RowNumber);

        // 2. Kind.
        var kindRow = Keyed(DailyInputsTemplate.KindKey);
        var kind = DailyInputsTemplate.ParseKind(kindRow is null ? null : DailyInputsTemplate.CellText(kindRow, ValueColumn));
        if (kind is null) errors.Add(Issue(kindRow?.RowNumber, null, DailyInputsCodes.KindInvalid));

        // 3. Store.
        var storeRow = Keyed(DailyInputsTemplate.StoreKey);
        var store = storeRow is null ? null : DailyInputsTemplate.CellText(storeRow, ValueColumn)?.ToUpperInvariant();
        if (store is null) errors.Add(Issue(storeRow?.RowNumber, null, DailyInputsCodes.StoreMissing));

        // 4. Business date.
        var dateRow = Keyed(DailyInputsTemplate.BusinessDateKey);
        DateOnly? date = null;
        var dateCell = dateRow is null || dateRow.Cells.Count <= ValueColumn ? null : dateRow.Cells[ValueColumn];
        if (dateCell is null || IsBlank(dateCell.Value))
            errors.Add(Issue(dateRow?.RowNumber, null, DailyInputsCodes.DateMissing));
        else if (TryReadDate(dateCell.Value) is not { } parsed)
            errors.Add(Issue(dateRow!.RowNumber, null, DailyInputsCodes.DateInvalid));
        else
        {
            date = parsed;
            if (parsed > today) errors.Add(Issue(dateRow!.RowNumber, null, DailyInputsCodes.DateInFuture));
            else if (parsed < today.AddDays(-DailyInputsTemplate.LookbackDays)) errors.Add(Issue(dateRow!.RowNumber, null, DailyInputsCodes.DateTooOld));
        }

        // Filled by (optional).
        var filledByRow = Keyed(DailyInputsTemplate.FilledByKey);
        var filledBy = filledByRow is null ? null : DailyInputsTemplate.CellText(filledByRow, ValueColumn);
        if (filledBy is not null && filledBy.Length > DailyInputsTemplate.MaxFilledByLength)
            errors.Add(Issue(filledByRow!.RowNumber, null, DailyInputsCodes.TextTooLong));

        // 5. Field rows.
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DailyInputsTemplate.TemplateKey, DailyInputsTemplate.KindKey, DailyInputsTemplate.StoreKey,
            DailyInputsTemplate.BusinessDateKey, DailyInputsTemplate.FilledByKey
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<DailyInputValue>();
        foreach (var row in sheet.Rows)
        {
            var code = DailyInputsTemplate.CellText(row, KeyColumn);
            var cell = row.Cells.Count > ValueColumn ? row.Cells[ValueColumn] : null;
            var blank = cell is null || IsBlank(cell.Value);
            if (code is null)
            {
                // A value with no field code cannot be placed.
                if (!blank) errors.Add(Issue(row.RowNumber, null, DailyInputsCodes.UnknownField));
                continue;
            }
            if (reserved.Contains(code)) continue;

            var field = DailyInputsTemplate.FindField(code);
            if (field is null) { errors.Add(Issue(row.RowNumber, code, DailyInputsCodes.UnknownField)); continue; }
            if (!seen.Add(field.FieldCode)) { errors.Add(Issue(row.RowNumber, field.FieldCode, DailyInputsCodes.DuplicateField)); continue; }
            if (kind is { } sheetKind && DailyInputsTemplate.KindOf(field.FieldCode) != sheetKind)
            {
                errors.Add(Issue(row.RowNumber, field.FieldCode, DailyInputsCodes.FieldNotForSheet));
                continue;
            }
            if (blank) continue; // Blank means leave unchanged: no row is emitted.

            var issueCode = TryReadValue(field, cell!.Value, out var numeric, out var text);
            if (issueCode is not null) errors.Add(Issue(row.RowNumber, field.FieldCode, issueCode));
            else values.Add(new DailyInputValue(row.RowNumber, field.FieldCode, field.ValueKind, numeric, text));
        }

        // 6. Name check, only once store, kind and date are known.
        if (kind is { } k && store is not null && date is { } d)
        {
            var match = FileNamePattern().Match(workbook.FileName ?? string.Empty);
            if (!match.Success)
                notes.Add(Issue(null, null, DailyInputsCodes.NameUnusual));
            else if (!string.Equals(match.Groups[1].Value, store, StringComparison.OrdinalIgnoreCase)
                || DailyInputsTemplate.ParseKind(match.Groups[2].Value) != k
                || !DateOnly.TryParseExact(match.Groups[3].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nameDate)
                || nameDate != d)
                errors.Add(Issue(null, null, DailyInputsCodes.NameMismatch));
        }

        // 7. Nothing to apply: the sheet was probably saved empty. Reported only when the rows themselves are fine.
        if (values.Count == 0 && !errors.Any(e => e.RowNumber is not null && e.FieldCode is not null))
            errors.Add(Issue(null, null, DailyInputsCodes.NoValues));

        // The document is kept whenever store, kind and date read, even with errors, so a failure can still be
        // registered under its store and date. Succeeded needs no errors as well.
        var document = kind is { } documentKind && store is not null && date is { } documentDate
            ? new DailyInputsDocument(workbook.FileName ?? string.Empty, workbook.Sha256, DailyInputsTemplate.CurrentVersion,
                documentKind, store, documentDate, filledBy, values)
            : null;
        return new DailyInputsParseResult(document, errors, notes);
    }

    private static DailyInputsParseResult Fail(List<DailyInputsIssue> errors, string code, int? row)
    {
        errors.Add(Issue(row, null, code));
        return new DailyInputsParseResult(null, errors, []);
    }

    private static DailyInputsIssue Issue(int? row, string? fieldCode, string code) =>
        new(row, fieldCode, code, ImportDiagnosticCatalogue.Template(code));

    private static bool IsBlank(object? value) => value is null || (value is string s && string.IsNullOrWhiteSpace(s));

    private static DateOnly? TryReadDate(object? value)
    {
        switch (value)
        {
            case DateTime dateTime:
                return DateOnly.FromDateTime(dateTime);
            case DateOnly dateOnly:
                return dateOnly;
            case string text:
                var trimmed = text.Trim();
                foreach (var culture in DateCultures)
                    if (DateTime.TryParseExact(trimmed, TextDateFormats, culture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
                        return DateOnly.FromDateTime(parsed);
                return null;
            default:
                // A bare number is not accepted: a date typed into the template's C5 is a date-styled cell.
                return null;
        }
    }

    /// <summary>Null when the value is valid; otherwise the issue code.</summary>
    private static string? TryReadValue(DailyInputsField field, object? value, out decimal? numeric, out string? text)
    {
        numeric = null; text = null;
        if (field.ValueKind == DailyInputValueKind.Text)
        {
            var raw = value switch
            {
                string s => s,
                DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bool b => b ? "TRUE" : "FALSE",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
            var trimmed = raw?.Trim() ?? string.Empty;
            if (trimmed.Length > DailyInputsTemplate.MaxTextLength) return DailyInputsCodes.TextTooLong;
            text = trimmed;
            return null;
        }

        if (TryReadNumber(value) is not { } number) return DailyInputsCodes.ValueInvalid;
        if (Math.Abs(number) > MaxMagnitude) return DailyInputsCodes.ValueInvalid;
        if (field.ValueKind == DailyInputValueKind.Quantity && number != decimal.Truncate(number)) return DailyInputsCodes.ValueInvalid;
        if (number < 0 && !field.AllowNegative) return DailyInputsCodes.ValueNegative;
        numeric = field.ValueKind == DailyInputValueKind.Money ? Math.Round(number, 2, MidpointRounding.AwayFromZero) : number;
        return null;
    }

    private static decimal? TryReadNumber(object? value)
    {
        switch (value)
        {
            case decimal d: return d;
            case int i: return i;
            case long l: return l;
            case double db when double.IsFinite(db) && Math.Abs(db) < 1e15: return (decimal)db;
            case float f when float.IsFinite(f) && Math.Abs(f) < 1e15: return (decimal)f;
            case string s:
                var cleaned = new string(s.Where(c => c != ',' && c != '₹' && !char.IsWhiteSpace(c)).ToArray());
                return cleaned.Length > 0 && decimal.TryParse(cleaned,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed : null;
            default: return null;
        }
    }
}
