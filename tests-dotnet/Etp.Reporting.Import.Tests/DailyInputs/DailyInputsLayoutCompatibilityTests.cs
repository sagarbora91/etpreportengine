using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.DailyInputs.DailyInputsTestSheets;

namespace Etp.Reporting.Import.Tests.DailyInputs;

/// <summary>
/// 1.11.3 integration (UI lane U4 moved the staff sheet's rows): the sheets already on the shop PCs were written by the
/// 1.10.1 / 1.11.1 / 1.11.2 templates (header row 1, TEMPLATE 2, KIND 3, STORE 4, BUSINESS_DATE in C5, FILLED_BY in C6,
/// fields from row 7; 1.11.2 adds the cash-check text in column F). The 1.11.3 template keeps header and TEMPLATE in rows
/// 1-2, puts KIND/STORE in 6/7, the date in C8, the name in C9 and the fields from row 10, with banner rows in between.
/// The parser finds every key by its column-A label, so both layouts must read the same store, kind, date, name and
/// figures through the real <see cref="OpenXmlWorkbookReader"/>, and an empty "Filled by" (decision 43) is refused in both.
/// </summary>
public sealed class DailyInputsLayoutCompatibilityTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);

    // The 1.11.3 rows (DailyInputsTemplateWriter.BusinessDateRow / FilledByRow / FirstFieldRow are internal to the Import project).
    private const int NewBusinessDateRow = 8, NewFilledByRow = 9, NewFirstFieldRow = 10;

    private static readonly (string Code, object? Value)[] RetailFigures =
    [
        ("WALK_INS", 18m), ("OPENING_CASH", 5000m), ("EXPENSES", 1250.75m), ("CASH_DEPOSIT", 0m), ("CASH_ADJUSTMENT", -40m),
        ("CLOSING_CASH_COUNTED", 12345.5m), ("OPERATIONAL_REMARK", "Synthetic remark")
    ];

    private static readonly (string Code, object? Value)[] ServiceFigures =
    [
        ("SERVICE_CASH", 1200m), ("SERVICE_CARD", 0m), ("SERVICE_UPI", 350.5m), ("SERVICE_WDC", 0m),
        ("WCC_WALKIN", 4m), ("WCC_SALES", 2100m), ("WDC_BILLS", 2m)
    ];

    private static (string Code, object? Value)[] Figures(DailyInputsKind kind) => kind == DailyInputsKind.Retail ? RetailFigures : ServiceFigures;

    public static TheoryData<DailyInputsKind, bool> OldLayouts => new()
    {
        // false = 1.10.1 / 1.11.1 (A-D only); true = 1.11.2 (cash-check text saved by Excel in column F of the counted-cash row).
        { DailyInputsKind.Retail, false }, { DailyInputsKind.Retail, true }, { DailyInputsKind.Service, false }
    };

    [Theory]
    [MemberData(nameof(OldLayouts))]
    public async Task A_sheet_in_the_old_layout_is_recognised_and_reads_the_same_as_the_new_layout(DailyInputsKind kind, bool cashCheckColumn)
    {
        var fileName = DailyInputsTemplate.FileName("HEMW", kind, Day);
        var old = await WriteOldLayoutAndReadAsync(kind, "HEMW", Day, "Synthetic staff", Figures(kind), cashCheckColumn, fileName);
        var current = await WriteFillAndReadAsync(kind, "HEMW", fileName, NewLayoutValues(Day, "Synthetic staff", Figures(kind)));

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(old));
        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(current));
        var oldResult = DailyInputsParser.Parse(old, Today);
        var newResult = DailyInputsParser.Parse(current, Today);
        Assert.True(oldResult.Succeeded, string.Join(", ", oldResult.Errors.Select(e => e.Code)));
        Assert.True(newResult.Succeeded, string.Join(", ", newResult.Errors.Select(e => e.Code)));

        Assert.Equal((kind, "HEMW", Day, "Synthetic staff"),
            (oldResult.Document!.Kind, oldResult.Document.StoreCode, oldResult.Document.BusinessDate, oldResult.Document.FilledBy));
        Assert.Equal((kind, "HEMW", Day, "Synthetic staff"),
            (newResult.Document!.Kind, newResult.Document.StoreCode, newResult.Document.BusinessDate, newResult.Document.FilledBy));
        Assert.Equal(oldResult.Document.Values.Select(v => (v.FieldCode, v.NumericValue, v.TextValue)),
            newResult.Document.Values.Select(v => (v.FieldCode, v.NumericValue, v.TextValue)));
        // Only the row numbers differ: the old fields start at row 7, the new ones at row 10.
        Assert.Equal(7, oldResult.Document.Values.Min(v => v.RowNumber));
        Assert.Equal(NewFirstFieldRow, newResult.Document.Values.Min(v => v.RowNumber));
    }

    public static TheoryData<bool, string?> BlankNames => new()
    {
        { false, null }, { false, "" }, { false, "   " },
        { true, null }, { true, "" }, { true, "   " }
    };

    [Theory]
    [MemberData(nameof(BlankNames))]
    public async Task An_empty_Filled_by_is_refused_in_both_layouts(bool newLayout, string? name)
    {
        var fileName = DailyInputsTemplate.FileName("WLMHW", DailyInputsKind.Retail, Day);
        var workbook = newLayout
            ? await WriteFillAndReadAsync(DailyInputsKind.Retail, "WLMHW", fileName, NewLayoutValues(Day, name, RetailFigures))
            : await WriteOldLayoutAndReadAsync(DailyInputsKind.Retail, "WLMHW", Day, name, RetailFigures, cashCheckColumn: true, fileName);

        var result = DailyInputsParser.Parse(workbook, Today);

        Assert.False(result.Succeeded);
        var refusal = Assert.Single(result.Errors);
        Assert.Equal(DailyInputsCodes.FilledByMissing, refusal.Code);
        // Found by its label, wherever the row is: C6 in the old layout, C9 in the new one.
        Assert.Equal(newLayout ? NewFilledByRow : 6, refusal.RowNumber);
        // Store, kind and date still read, so the refusal is registered under the right shop and day.
        Assert.Equal((DailyInputsKind.Retail, "WLMHW", Day), (result.Document!.Kind, result.Document.StoreCode, result.Document.BusinessDate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_date_is_reported_on_the_date_row_of_each_layout(bool newLayout)
    {
        var fileName = DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, Day);
        var workbook = newLayout
            ? await WriteFillAndReadAsync(DailyInputsKind.Retail, "HEMW", fileName, NewLayoutValues(null, "Synthetic staff", RetailFigures))
            : await WriteOldLayoutAndReadAsync(DailyInputsKind.Retail, "HEMW", null, "Synthetic staff", RetailFigures, cashCheckColumn: false, fileName);

        var result = DailyInputsParser.Parse(workbook, Today);

        var missing = Assert.Single(result.Errors);
        Assert.Equal(DailyInputsCodes.DateMissing, missing.Code);
        Assert.Equal(newLayout ? NewBusinessDateRow : 5, missing.RowNumber);
    }

    private static Dictionary<string, object?> NewLayoutValues(DateOnly? date, string? filledBy, IEnumerable<(string Code, object? Value)> figures)
    {
        var values = new Dictionary<string, object?> { ["BUSINESS_DATE"] = date?.ToDateTime(TimeOnly.MinValue), ["FILLED_BY"] = filledBy };
        foreach (var (code, value) in figures) values[code] = value;
        return values;
    }

    /// <summary>
    /// A sheet as the 1.10.1-1.11.2 templates laid it out and as Excel saves it on a shop PC: shared strings, the date a
    /// serial in a dd-mmm-yyyy cell, keys in rows 2-6, fields from row 7, and (1.11.2) the cash-check formula's text in F.
    /// </summary>
    private static async Task<WorkbookSnapshot> WriteOldLayoutAndReadAsync(DailyInputsKind kind, string store, DateOnly? date, string? filledBy,
        IReadOnlyList<(string Code, object? Value)> figures, bool cashCheckColumn, string fileName)
    {
        var folder = Path.Combine(Path.GetTempPath(), "etp-daily-inputs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        try
        {
            using (var stream = new MemoryStream())
            {
                using (var document = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
                {
                    var book = document.AddWorkbookPart();
                    book.Workbook = new Workbook();
                    book.AddNewPart<WorkbookStylesPart>().Stylesheet = new Stylesheet(
                        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "dd-mmm-yyyy" }) { Count = 1 },
                        new Fonts(new Font()) { Count = 1 },
                        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
                        new Borders(new Border()) { Count = 1 },
                        new CellStyleFormats(new CellFormat()) { Count = 1 },
                        new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true }) { Count = 2 });

                    var strings = new List<string>();
                    Cell MakeCell(string column, uint row, object? value)
                    {
                        var cell = new Cell { CellReference = $"{column}{row}" };
                        switch (value)
                        {
                            case null: break;
                            case string text when text.Length == 0: break;
                            case string text:
                                var index = strings.IndexOf(text);
                                if (index < 0) { strings.Add(text); index = strings.Count - 1; }
                                cell.DataType = CellValues.SharedString;
                                cell.CellValue = new CellValue(index.ToString(CultureInfo.InvariantCulture));
                                break;
                            case DateOnly day:
                                cell.StyleIndex = 1;
                                cell.CellValue = new CellValue(day.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture));
                                break;
                            default:
                                cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                                break;
                        }
                        return cell;
                    }
                    Row Line(uint number, params object?[] values)
                    {
                        var row = new Row { RowIndex = number };
                        for (var i = 0; i < values.Length; i++) row.Append(MakeCell(((char)('A' + i)).ToString(), number, values[i]));
                        return row;
                    }

                    var data = new SheetData();
                    data.Append(Line(1, DailyInputsTemplate.Headers[0], DailyInputsTemplate.Headers[1], DailyInputsTemplate.Headers[2], DailyInputsTemplate.Headers[3]));
                    data.Append(Line(2, DailyInputsTemplate.TemplateKey, "Template", DailyInputsTemplate.CurrentVersion, "Do not change"));
                    data.Append(Line(3, DailyInputsTemplate.KindKey, "Sheet type", DailyInputsTemplate.KindText(kind), "Do not change"));
                    data.Append(Line(4, DailyInputsTemplate.StoreKey, "Store", store, "Synthetic store"));
                    data.Append(date is { } d
                        ? Line(5, DailyInputsTemplate.BusinessDateKey, "Business date", d, "The day the figures belong to (dd-mmm-yyyy)")
                        : Line(5, DailyInputsTemplate.BusinessDateKey, "Business date", null, "The day the figures belong to (dd-mmm-yyyy)"));
                    data.Append(Line(6, DailyInputsTemplate.FilledByKey, "Filled by", filledBy, "Your name (required: a sheet without it is refused)"));
                    var figureValues = figures.ToDictionary(f => f.Code, f => f.Value, StringComparer.OrdinalIgnoreCase);
                    uint fieldRow = 7;
                    foreach (var field in DailyInputsTemplate.FieldsFor(kind))
                    {
                        var fieldLine = Line(fieldRow, field.FieldCode, field.Label, figureValues.GetValueOrDefault(field.FieldCode), "leave blank to keep unchanged");
                        if (cashCheckColumn && field.FieldCode == "CLOSING_CASH_COUNTED")
                        {
                            // What Excel stores for the 1.11.2 formula cell: the formula plus its cached text result.
                            fieldLine.Append(new Cell { CellReference = $"F{fieldRow}", DataType = CellValues.String,
                                CellFormula = new CellFormula("IF(C12=\"\",\"\",\"Cash check\")"),
                                CellValue = new CellValue("Cash check - cash taken today, from your count: 7,135.75") });
                        }
                        data.Append(fieldLine);
                        fieldRow++;
                    }

                    var part = book.AddNewPart<WorksheetPart>();
                    part.Worksheet = new Worksheet(data);
                    var table = book.AddNewPart<SharedStringTablePart>();
                    table.SharedStringTable = new SharedStringTable(strings.Select(s => new SharedStringItem(new Text(s))))
                        { Count = (uint)strings.Count, UniqueCount = (uint)strings.Count };
                    book.Workbook.AppendChild(new Sheets()).Append(new Sheet { Id = book.GetIdOfPart(part), SheetId = 1, Name = DailyInputsTemplate.SheetName });
                    book.Workbook.Save();
                }
                await File.WriteAllBytesAsync(path, stream.ToArray());
            }
            return await new OpenXmlWorkbookReader().ReadAsync(path);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
