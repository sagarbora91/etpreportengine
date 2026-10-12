using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Import.DailyInputs;

/// <summary>
/// Writes the pre-filled daily-inputs template for one shop PC (design section 1): a plain .xlsx, no macros, one
/// sheet <c>Daily Inputs</c> protected without a password. Only the staff cells are unlocked: the date, the name and the
/// Value cells of the field rows. The templates are generated, never committed as binaries.
/// 1.11.1: an optional business date pre-fills the date cell (a date serial in the dd-mmm-yyyy input style; still
/// unlocked so staff can correct it). Without it the cell stays blank, as the shop-PC templates need.
/// <para>
/// 1.12 lane U4 (decision 45 #7, UIUX-OUTPUTS OUT-23..26, MOCK-OUT-23-after): the sheet is laid out for shop staff. The
/// parser contract is unchanged (<see cref="DailyInputsParser"/> and <see cref="DailyInputsTemplate.IsDailyInputsWorkbook"/>
/// are not touched): row 1 is still the <c>Field code | Input | Value | Note</c> header and the first row under it is
/// still TEMPLATE, every key still sits in column A and every value in column C, and the parser finds keyed rows
/// wherever they are, so sheets already on the shop PCs import as before. What staff see: column A (the codes), the
/// header row and the TEMPLATE/KIND/STORE rows are hidden; a title with the store name and two instruction lines sit
/// above the boxes (rows whose column A and C are empty, which the parser skips); labels and inputs are 14 pt at 125 %
/// zoom with taller rows; input boxes are yellow with a border, the ones that must be filled darker; the cash check is a
/// bold line under the table instead of column F.
/// </para>
/// </summary>
public static class DailyInputsTemplateWriter
{
    // Cell formats (indexes into CellFormats below).
    private const uint StyleDefault = 0, StyleHeader = 1, StyleKey = 2, StyleInputText = 3, StyleInputDate = 4,
        StyleInputMoney = 5, StyleInputQuantity = 6, StyleNote = 7, StyleTitle = 8, StyleInstruction = 9, StyleLabel = 10,
        StyleInputTextRequired = 11, StyleInputMoneyRequired = 12, StyleCashCheck = 13, StyleDone = 14;

    private const uint DateFormatId = 164, MoneyFormatId = 165;

    /// <summary>Indian grouping (12,34,567.89) for amounts; Excel allows two conditions, so crore and lakh are tested.</summary>
    internal const string IndianMoneyFormat = "[>=10000000]##\\,##\\,##\\,##0.00;[>=100000]##\\,##\\,##0.00;##,##0.00";

    // Row layout. Rows 1-2 must stay header then TEMPLATE (the recognition rule); everything else is found by key.
    internal const int HeaderRow = 1, TemplateRow = 2, TitleRow = 3, FirstInstructionRow = 4, SecondInstructionRow = 5,
        KindRow = 6, StoreRow = 7, BusinessDateRow = 8, FilledByRow = 9, FirstFieldRow = 10;

    // Shop staff words for the retail fields (OUT-24); a field without an entry gets a note from its value kind.
    private static readonly IReadOnlyDictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["WALK_INS"] = "People who came in today (whole number)",
        ["OPENING_CASH"] = "₹ in the till at opening",
        ["EXPENSES"] = "₹ paid out of the till today",
        ["CASH_DEPOSIT"] = "₹ taken to the bank today",
        ["CASH_ADJUSTMENT"] = "₹ correction, may be negative (ask the Owner)",
        ["CLOSING_CASH_COUNTED"] = "₹ counted in the till now",
        ["OPERATIONAL_REMARK"] = "Anything the Owner should know (optional)"
    };

    public static void Write(Stream output, DailyInputsKind kind, string storeCode, string storeLabel, DateOnly? businessDate = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        var store = storeCode.Trim().ToUpperInvariant();
        var storeName = string.IsNullOrWhiteSpace(storeLabel) ? store : storeLabel.Trim();
        var fields = DailyInputsTemplate.FieldsFor(kind);
        var india = CultureInfo.GetCultureInfo("en-IN");

        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        var book = document.AddWorkbookPart();
        book.Workbook = new Workbook();
        var styles = book.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = Styles();

        var rows = new SheetData();
        var merges = new List<string>();
        rows.Append(Hidden(Row(HeaderRow, [Text(DailyInputsTemplate.Headers[0], StyleHeader), Text(DailyInputsTemplate.Headers[1], StyleHeader),
            Text(DailyInputsTemplate.Headers[2], StyleHeader), Text(DailyInputsTemplate.Headers[3], StyleHeader)])));
        rows.Append(Hidden(Row(TemplateRow, [Text(DailyInputsTemplate.TemplateKey, StyleKey), Text("Template"), Text(DailyInputsTemplate.CurrentVersion), Text("Do not change", StyleNote)])));
        var title = businessDate is { } day ? $"{storeName} - daily figures for {day.ToString("dddd dd MMM yyyy", india)}" : $"{storeName} - daily figures";
        rows.Append(Tall(Banner(TitleRow, title, StyleTitle, merges), 30));
        rows.Append(Tall(Banner(FirstInstructionRow, "1. Fill the yellow boxes. The darker boxes must be filled. A box left at 0 means zero.", StyleInstruction, merges), 22));
        rows.Append(Tall(Banner(SecondInstructionRow, kind == DailyInputsKind.Retail
            ? "2. Check the cash line under the boxes against the till.   3. Save (Ctrl+S) and close Excel. Keep the file name."
            : "2. Save (Ctrl+S) and close Excel. Keep the file name.", StyleInstruction, merges), 22));
        rows.Append(Hidden(Row(KindRow, [Text(DailyInputsTemplate.KindKey, StyleKey), Text("Sheet type"), Text(DailyInputsTemplate.KindText(kind)), Text("Do not change", StyleNote)])));
        rows.Append(Hidden(Row(StoreRow, [Text(DailyInputsTemplate.StoreKey, StyleKey), Text("Store"), Text(store), Text(storeLabel ?? string.Empty, StyleNote)])));
        rows.Append(Tall(Row(BusinessDateRow, [Text(DailyInputsTemplate.BusinessDateKey, StyleKey), Text("Date of these figures", StyleLabel),
            businessDate is { } date ? DateSerial(date, StyleInputDate) : Empty(StyleInputDate),
            Text($"Today, or up to {DailyInputsTemplate.LookbackDays} days back (dd-mmm-yyyy)", StyleNote)]), 26));
        rows.Append(Tall(Row(FilledByRow, [Text(DailyInputsTemplate.FilledByKey, StyleKey), Text("Your name", StyleLabel), Empty(StyleInputTextRequired),
            Text("Must be filled, or the sheet is refused", StyleNote)]), 26));

        var dateReference = $"C{BusinessDateRow}";
        var validations = new List<DataValidation>
        {
            new()
            {
                Type = DataValidationValues.Date, Operator = DataValidationOperatorValues.Between, AllowBlank = true,
                ShowErrorMessage = true, ErrorTitle = "Business date",
                Error = $"Enter the business date: today or up to {DailyInputsTemplate.LookbackDays} days back.",
                SequenceOfReferences = Refs(dateReference),
                Formula1 = new Formula1($"TODAY()-{DailyInputsTemplate.LookbackDays}"),
                Formula2 = new Formula2("TODAY()")
            },
            TextLength($"C{FilledByRow}", DailyInputsTemplate.MaxFilledByLength)
        };

        var rowNumber = (uint)FirstFieldRow;
        uint openingRow = 0, expensesRow = 0, depositRow = 0, adjustmentRow = 0, countedRow = 0;
        foreach (var field in fields)
        {
            var reference = $"C{rowNumber}";
            var (style, note) = field.ValueKind switch
            {
                DailyInputValueKind.Quantity => (StyleInputQuantity, "Count (whole number)"),
                DailyInputValueKind.Money when field.AllowNegative => (StyleInputMoney, "₹, may be negative"),
                DailyInputValueKind.Money => (StyleInputMoney, "₹"),
                _ => (StyleInputText, $"Text, up to {DailyInputsTemplate.MaxTextLength} characters (optional)")
            };
            if (field.FieldCode == "CLOSING_CASH_COUNTED") style = StyleInputMoneyRequired;
            // 1.11.2: amounts and counts start at 0 so a forgotten box is a deliberate zero, not a refusal. Opening and
            // counted cash stay blank (a 0 there would be a claim), and so does text; blank still means "keep unchanged".
            var valueCell = PrefillsZero(field) ? Zero(style) : Empty(style);
            if (field.FieldCode == "OPENING_CASH") openingRow = rowNumber;
            else if (field.FieldCode == "EXPENSES") expensesRow = rowNumber;
            else if (field.FieldCode == "CASH_DEPOSIT") depositRow = rowNumber;
            else if (field.FieldCode == "CASH_ADJUSTMENT") adjustmentRow = rowNumber;
            else if (field.FieldCode == "CLOSING_CASH_COUNTED") countedRow = rowNumber;
            var text = Notes.TryGetValue(field.FieldCode, out var plain) ? plain : note;
            rows.Append(Tall(Row(rowNumber, [Text(field.FieldCode, StyleKey), Text(field.Label, StyleLabel), valueCell, Text($"{text}. Blank = no change.", StyleNote)]), 26));
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

        if (countedRow != 0 && openingRow != 0 && expensesRow != 0 && depositRow != 0 && adjustmentRow != 0)
        {
            // Cash check line under the table (OUT-25): column A and C stay empty, so the parser skips the row. Cash taken
            // today = count - opening + expenses + deposit - adjustment; staff compare it with the till before saving.
            var taken = $"C{countedRow}-N(C{openingRow})+N(C{expensesRow})+N(C{depositRow})-N(C{adjustmentRow})";
            var lakh = IndianMoneyFormat.Replace("\"", "\"\"", StringComparison.Ordinal);
            var formula = $"IF(C{countedRow}=\"\",\"Cash check: fill in counted closing cash to see the cash taken today\","
                + $"\"Cash check: cash taken today, from your count: \"&IF({taken}<0,\"-\",\"\")&\"₹\"&TEXT(ABS({taken}),\"{lakh}\"))";
            var check = new Row { RowIndex = rowNumber };
            check.Append(new Cell { CellReference = $"B{rowNumber}", CellFormula = new CellFormula(formula), DataType = CellValues.String, StyleIndex = StyleCashCheck });
            merges.Add($"B{rowNumber}:D{rowNumber}");
            rows.Append(Tall(check, 28));
            rowNumber++;
        }
        rows.Append(Tall(Banner(rowNumber, "When done: save and close Excel. ETP picks the file up from the shared folder. Questions: ask the Owner.", StyleDone, merges), 22));

        var part = book.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(
            new SheetProperties(new PageSetupProperties { FitToPage = true }),
            new SheetViews(new SheetView(new Pane { VerticalSplit = SecondInstructionRow, TopLeftCell = $"A{KindRow}", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
                { WorkbookViewId = 0, TabSelected = true, ZoomScale = 125, ZoomScaleNormal = 125 }),
            new Columns(
                new Column { Min = 1, Max = 1, Width = 24, CustomWidth = true, Hidden = true },
                new Column { Min = 2, Max = 2, Width = 30, CustomWidth = true },
                new Column { Min = 3, Max = 3, Width = 22, CustomWidth = true },
                new Column { Min = 4, Max = 4, Width = 52, CustomWidth = true }),
            rows,
            new SheetProtection { Sheet = true, Objects = true, Scenarios = true },
            new MergeCells(merges.Select(reference => new MergeCell { Reference = reference })) { Count = (uint)merges.Count },
            new DataValidations(validations) { Count = (uint)validations.Count },
            new PageMargins { Left = 0.5, Right = 0.5, Top = 0.6, Bottom = 0.6, Header = 0.3, Footer = 0.3 },
            new PageSetup { PaperSize = 9, Orientation = OrientationValues.Portrait, FitToWidth = 1, FitToHeight = 1 });

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

    private static Stylesheet Styles()
    {
        static Font MakeFont(double size, bool bold = false, string? argb = null)
        {
            var font = new Font(); if (bold) font.Append(new Bold()); font.Append(new FontSize { Val = size });
            if (argb is not null) font.Append(new Color { Rgb = argb });
            return font;
        }
        static Fill MakeFill(string argb) => new(new PatternFill(new ForegroundColor { Rgb = argb }) { PatternType = PatternValues.Solid });
        static Border Box(string argb) => new(
            new LeftBorder(new Color { Rgb = argb }) { Style = BorderStyleValues.Thin }, new RightBorder(new Color { Rgb = argb }) { Style = BorderStyleValues.Thin },
            new TopBorder(new Color { Rgb = argb }) { Style = BorderStyleValues.Thin }, new BottomBorder(new Color { Rgb = argb }) { Style = BorderStyleValues.Thin }, new DiagonalBorder());
        static CellFormat Format(uint font = 0, uint fill = 0, uint border = 0, uint numberFormat = 0, bool wrap = false)
        {
            var format = new CellFormat { FontId = font, FillId = fill, BorderId = border, NumberFormatId = numberFormat,
                ApplyFont = font != 0, ApplyFill = fill != 0, ApplyBorder = border != 0, ApplyNumberFormat = numberFormat != 0 };
            var alignment = new Alignment { Vertical = VerticalAlignmentValues.Center };
            if (wrap) alignment.WrapText = true;
            format.Append(alignment); format.ApplyAlignment = true;
            return format;
        }
        // Colours mirror the app tokens (ETP-DESIGN-SYSTEM.md): PrimaryText, SecondaryText, Success; the input yellows
        // are the sheet's own (FFF2CC since 1.11.0, darker FFE699 for the boxes that must be filled).
        const string primary = "FF10252D", secondary = "FF56666B", success = "FF087C68", border = "FFBF9000";
        return new(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = DateFormatId, FormatCode = "dd-mmm-yyyy" },
                new NumberingFormat { NumberFormatId = MoneyFormatId, FormatCode = IndianMoneyFormat }) { Count = 2 },
            new Fonts(
                MakeFont(11),                       // 0 default
                MakeFont(11, true),                 // 1 header
                MakeFont(16, true, primary),        // 2 title
                MakeFont(12, false, primary),       // 3 instruction
                MakeFont(14, false, primary),       // 4 label
                MakeFont(14, true, primary),        // 5 input
                MakeFont(11, false, secondary),     // 6 note
                MakeFont(12, true, success)) { Count = 7 }, // 7 cash check
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                MakeFill("FFD9D9D9"),               // 2 key / header grey
                MakeFill("FFFFF2CC"),               // 3 input yellow
                MakeFill("FFFFE699")) { Count = 5 }, // 4 input that must be filled
            new Borders(new Border(), Box(border)) { Count = 2 },
            new CellStyleFormats(new CellFormat()) { Count = 1 },
            new CellFormats(
                new CellFormat(),                                                         // 0 default, locked
                Format(font: 1, fill: 2),                                                 // 1 header
                Format(fill: 2),                                                          // 2 field code (grey, column hidden)
                Unlocked(Format(font: 5, fill: 3, border: 1, numberFormat: 49)),          // 3 input text
                Unlocked(Format(font: 5, fill: 4, border: 1, numberFormat: DateFormatId)), // 4 input date (must be filled)
                Unlocked(Format(font: 5, fill: 3, border: 1, numberFormat: MoneyFormatId)), // 5 input money
                Unlocked(Format(font: 5, fill: 3, border: 1, numberFormat: 1)),           // 6 input quantity ("0")
                Format(font: 6, wrap: true),                                              // 7 note
                Format(font: 2),                                                          // 8 title
                Format(font: 3, wrap: true),                                              // 9 instruction
                Format(font: 4),                                                          // 10 label
                Unlocked(Format(font: 5, fill: 4, border: 1, numberFormat: 49)),          // 11 input text (must be filled)
                Unlocked(Format(font: 5, fill: 4, border: 1, numberFormat: MoneyFormatId)), // 12 input money (must be filled)
                Format(font: 7),                                                          // 13 cash check
                Format(font: 6)) { Count = 15 },                                          // 14 "when done"
            new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
    }

    private static CellFormat Unlocked(CellFormat format)
    {
        format.Append(new Protection { Locked = false });
        format.ApplyProtection = true;
        return format;
    }

    private static Cell Text(string text, uint style = StyleDefault) =>
        new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(text)), StyleIndex = style };

    private static bool PrefillsZero(DailyInputsField field) =>
        field.ValueKind != DailyInputValueKind.Text && field.FieldCode is not ("OPENING_CASH" or "CLOSING_CASH_COUNTED");

    private static Cell Zero(uint style) => new() { CellValue = new CellValue("0"), StyleIndex = style };

    private static Cell Empty(uint style) => new() { StyleIndex = style };

    private static Cell DateSerial(DateOnly date, uint style) => new()
    {
        CellValue = new CellValue(date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)),
        StyleIndex = style
    };

    /// <summary>A text line merged across B:D. Column A and C stay empty, so the parser skips the row.</summary>
    private static Row Banner(uint index, string text, uint style, List<string> merges)
    {
        var row = new Row { RowIndex = index };
        var cell = Text(text, style); cell.CellReference = $"B{index}"; row.Append(cell);
        merges.Add($"B{index}:D{index}");
        return row;
    }

    private static Row Hidden(Row row) { row.Hidden = true; return row; }

    private static Row Tall(Row row, double height) { row.Height = height; row.CustomHeight = true; return row; }

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

    private static Row Row(int index, IReadOnlyList<Cell> cells) => Row((uint)index, cells);
    private static Row Banner(int index, string text, uint style, List<string> merges) => Banner((uint)index, text, style, merges);
}
