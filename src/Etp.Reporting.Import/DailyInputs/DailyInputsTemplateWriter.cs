using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Import.DailyInputs;

/// <summary>
/// Writes the pre-filled daily-inputs template for one shop PC (design section 1): a plain .xlsx, no macros, one
/// sheet <c>Daily Inputs</c> protected without a password. Only the staff cells are unlocked: C5 (date), C6
/// (filled by) and the Value cells of the field rows. The templates are generated, never committed as binaries.
/// </summary>
public static class DailyInputsTemplateWriter
{
    // Cell formats (indexes into CellFormats below).
    private const uint StyleDefault = 0, StyleHeader = 1, StyleKey = 2, StyleInputText = 3, StyleInputDate = 4,
        StyleInputMoney = 5, StyleInputQuantity = 6, StyleNote = 7;

    private const uint DateFormatId = 164, MoneyFormatId = 165;
    private const int FirstFieldRow = 7;

    public static void Write(Stream output, DailyInputsKind kind, string storeCode, string storeLabel)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        var store = storeCode.Trim().ToUpperInvariant();
        var fields = DailyInputsTemplate.FieldsFor(kind);

        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var book = document.AddWorkbookPart();
        book.Workbook = new Workbook();
        var styles = book.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = Styles();

        var rows = new SheetData();
        rows.Append(Row(1, [Text(DailyInputsTemplate.Headers[0], StyleHeader), Text(DailyInputsTemplate.Headers[1], StyleHeader),
            Text(DailyInputsTemplate.Headers[2], StyleHeader), Text(DailyInputsTemplate.Headers[3], StyleHeader)]));
        rows.Append(Row(2, [Text(DailyInputsTemplate.TemplateKey, StyleKey), Text("Template"), Text(DailyInputsTemplate.CurrentVersion), Text("Do not change", StyleNote)]));
        rows.Append(Row(3, [Text(DailyInputsTemplate.KindKey, StyleKey), Text("Sheet type"), Text(DailyInputsTemplate.KindText(kind)), Text("Do not change", StyleNote)]));
        rows.Append(Row(4, [Text(DailyInputsTemplate.StoreKey, StyleKey), Text("Store"), Text(store), Text(storeLabel ?? string.Empty, StyleNote)]));
        rows.Append(Row(5, [Text(DailyInputsTemplate.BusinessDateKey, StyleKey), Text("Business date"), Empty(StyleInputDate),
            Text("The day the figures belong to (dd-mmm-yyyy)", StyleNote)]));
        rows.Append(Row(6, [Text(DailyInputsTemplate.FilledByKey, StyleKey), Text("Filled by"), Empty(StyleInputText),
            Text("Your name (optional)", StyleNote)]));

        var validations = new List<DataValidation>
        {
            new()
            {
                Type = DataValidationValues.Date, Operator = DataValidationOperatorValues.Between, AllowBlank = true,
                ShowErrorMessage = true, ErrorTitle = "Business date",
                Error = $"Enter the business date: today or up to {DailyInputsTemplate.LookbackDays} days back.",
                SequenceOfReferences = Refs("C5"),
                Formula1 = new Formula1($"TODAY()-{DailyInputsTemplate.LookbackDays}"),
                Formula2 = new Formula2("TODAY()")
            },
            TextLength("C6", DailyInputsTemplate.MaxFilledByLength)
        };

        var rowNumber = (uint)FirstFieldRow;
        foreach (var field in fields)
        {
            var reference = $"C{rowNumber}";
            var (style, note) = field.ValueKind switch
            {
                DailyInputValueKind.Quantity => (StyleInputQuantity, "count (whole number)"),
                DailyInputValueKind.Money when field.AllowNegative => (StyleInputMoney, "₹ (may be negative)"),
                DailyInputValueKind.Money => (StyleInputMoney, "₹"),
                _ => (StyleInputText, $"text (up to {DailyInputsTemplate.MaxTextLength} characters)")
            };
            rows.Append(Row(rowNumber, [Text(field.FieldCode, StyleKey), Text(field.Label), Empty(style), Text($"{note}; leave blank to keep unchanged", StyleNote)]));
            validations.Add(field.ValueKind switch
            {
                DailyInputValueKind.Quantity => Number(reference, DataValidationValues.Whole, "Enter a whole number, 0 or more."),
                DailyInputValueKind.Money when field.AllowNegative => new DataValidation
                {
                    Type = DataValidationValues.Decimal, Operator = DataValidationOperatorValues.Between, AllowBlank = true,
                    ShowErrorMessage = true, Error = "Enter an amount; it may be negative.", SequenceOfReferences = Refs(reference),
                    Formula1 = new Formula1("-999999999999.99"), Formula2 = new Formula2("999999999999.99")
                },
                DailyInputValueKind.Money => Number(reference, DataValidationValues.Decimal, "Enter an amount, 0 or more."),
                _ => TextLength(reference, DailyInputsTemplate.MaxTextLength)
            });
            rowNumber++;
        }

        var part = book.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(
            new SheetViews(new SheetView { WorkbookViewId = 0, TabSelected = true }),
            new Columns(
                new Column { Min = 1, Max = 1, Width = 24, CustomWidth = true },
                new Column { Min = 2, Max = 2, Width = 24, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 22, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 48, CustomWidth = true }),
            rows,
            new SheetProtection { Sheet = true, Objects = true, Scenarios = true },
            new DataValidations(validations) { Count = (uint)validations.Count });

        var sheets = book.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet { Id = book.GetIdOfPart(part), SheetId = 1, Name = DailyInputsTemplate.SheetName });
        book.Workbook.Save();
    }

    private static DataValidation Number(string reference, DataValidationValues type, string error) => new()
    {
        Type = type, Operator = DataValidationOperatorValues.GreaterThanOrEqual, AllowBlank = true,
        ShowErrorMessage = true, Error = error, SequenceOfReferences = Refs(reference), Formula1 = new Formula1("0")
    };

    private static DataValidation TextLength(string reference, int max) => new()
    {
        Type = DataValidationValues.TextLength, Operator = DataValidationOperatorValues.LessThanOrEqual, AllowBlank = true,
        ShowErrorMessage = true, Error = $"Up to {max} characters.", SequenceOfReferences = Refs(reference),
        Formula1 = new Formula1(max.ToString(CultureInfo.InvariantCulture))
    };

    private static ListValue<StringValue> Refs(string reference) => new() { InnerText = reference };

    private static Stylesheet Styles() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = DateFormatId, FormatCode = "dd-mmm-yyyy" },
            new NumberingFormat { NumberFormatId = MoneyFormatId, FormatCode = "#,##0.00" }) { Count = 2 },
        new Fonts(new Font(new FontSize { Val = 11 }), new Font(new Bold(), new FontSize { Val = 11 }), new Font(new Italic(), new FontSize { Val = 10 })) { Count = 3 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFD9D9D9" }) { PatternType = PatternValues.Solid }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFFFF2CC" }) { PatternType = PatternValues.Solid })) { Count = 4 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),                                                                                      // 0 default, locked
            new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true },                         // 1 header
            new CellFormat { FillId = 2, ApplyFill = true },                                                       // 2 field code (grey)
            Unlocked(new CellFormat { FillId = 3, ApplyFill = true, NumberFormatId = 49, ApplyNumberFormat = true }), // 3 input text
            Unlocked(new CellFormat { FillId = 3, ApplyFill = true, NumberFormatId = DateFormatId, ApplyNumberFormat = true }), // 4 input date
            Unlocked(new CellFormat { FillId = 3, ApplyFill = true, NumberFormatId = MoneyFormatId, ApplyNumberFormat = true }), // 5 input money
            Unlocked(new CellFormat { FillId = 3, ApplyFill = true, NumberFormatId = 1, ApplyNumberFormat = true }), // 6 input quantity ("0")
            new CellFormat { FontId = 2, ApplyFont = true, Alignment = new Alignment { WrapText = true }, ApplyAlignment = true }) { Count = 8 }, // 7 note
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });

    private static CellFormat Unlocked(CellFormat format)
    {
        format.Append(new Protection { Locked = false });
        format.ApplyProtection = true;
        return format;
    }

    private static Cell Text(string text, uint style = StyleDefault) =>
        new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(text)), StyleIndex = style };

    private static Cell Empty(uint style) => new() { StyleIndex = style };

    private static Row Row(uint index, IReadOnlyList<Cell> cells)
    {
        var row = new Row { RowIndex = index };
        for (var i = 0; i < cells.Count; i++)
        {
            cells[i].CellReference = $"{(char)('A' + i)}{index}";
            row.Append(cells[i]);
        }
        return row;
    }
}
