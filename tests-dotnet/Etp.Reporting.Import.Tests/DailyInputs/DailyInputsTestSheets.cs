using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests.DailyInputs;

/// <summary>
/// Synthetic daily-inputs sheets. They hold store codes and figures only, never customer data.
/// </summary>
internal static class DailyInputsTestSheets
{
    public static readonly DateOnly Today = new(2026, 10, 11);

    /// <summary>An in-memory snapshot shaped as OpenXmlWorkbookReader returns the template (header row 1, rows 2..).</summary>
    public static WorkbookSnapshot Sheet(
        DailyInputsKind kind = DailyInputsKind.Retail,
        string store = "HEMW",
        object? date = null,
        string? fileName = null,
        string version = DailyInputsTemplate.CurrentVersion,
        string? filledBy = null,
        string sheetName = DailyInputsTemplate.SheetName,
        IReadOnlyList<string>? headers = null,
        IReadOnlyList<(string Code, object? Value)>? fields = null)
    {
        var businessDate = date ?? new DateTime(2026, 10, 11);
        var rows = new List<WorkbookRow>
        {
            Row(2, DailyInputsTemplate.TemplateKey, "Template", version),
            Row(3, DailyInputsTemplate.KindKey, "Sheet type", DailyInputsTemplate.KindText(kind)),
            Row(4, DailyInputsTemplate.StoreKey, "Store", store),
            Row(5, DailyInputsTemplate.BusinessDateKey, "Business date", businessDate),
            Row(6, DailyInputsTemplate.FilledByKey, "Filled by", filledBy)
        };
        var number = 7;
        foreach (var (code, value) in fields ?? Array.Empty<(string Code, object? Value)>()) rows.Add(Row(number++, code, code, value));
        var name = fileName ?? DefaultFileName(store, kind, businessDate);
        return new WorkbookSnapshot(name, 1, new string('d', 64),
            [new WorkbookSheet(sheetName, 1, headers ?? DailyInputsTemplate.Headers, rows)]);
    }

    private static string DefaultFileName(string store, DailyInputsKind kind, object date) => date switch
    {
        DateTime d => DailyInputsTemplate.FileName(store, kind, DateOnly.FromDateTime(d)),
        _ => $"ETP-Daily-Inputs-{store}-{DailyInputsTemplate.KindText(kind)}-2026-10-11.xlsx"
    };

    private static WorkbookRow Row(int number, string code, string label, object? value) =>
        new(number, [Cell(code), Cell(label), Cell(value), Cell("note")]);

    private static WorkbookCell Cell(object? value) => new(value, value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture));

    /// <summary>
    /// Writes the real template with <see cref="DailyInputsTemplateWriter"/>, fills Value cells by column-A key, saves it
    /// under <paramref name="fileName"/> and reads it back with <see cref="OpenXmlWorkbookReader"/>.
    /// </summary>
    public static async Task<WorkbookSnapshot> WriteFillAndReadAsync(
        DailyInputsKind kind, string store, string fileName, IReadOnlyDictionary<string, object?> values)
    {
        var folder = Path.Combine(Path.GetTempPath(), "etp-daily-inputs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        try
        {
            using (var stream = new MemoryStream())
            {
                DailyInputsTemplateWriter.Write(stream, kind, store, "Synthetic store");
                stream.Position = 0;
                using (var document = SpreadsheetDocument.Open(stream, true))
                {
                    var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;
                    foreach (var (key, value) in values) SetValue(sheet, key, value);
                    sheet.Save();
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

    private static void SetValue(Worksheet sheet, string key, object? value)
    {
        var row = sheet.Descendants<Row>().Single(r =>
            r.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == $"A{r.RowIndex!.Value}")?.InnerText == key);
        var cell = row.Elements<Cell>().Single(c => c.CellReference?.Value == $"C{row.RowIndex!.Value}");
        cell.InlineString = null; cell.CellValue = null; cell.DataType = null;
        switch (value)
        {
            case null: break;
            case string text:
                cell.DataType = CellValues.InlineString;
                cell.InlineString = new InlineString(new Text(text));
                break;
            case DateTime date:
                cell.CellValue = new CellValue(date.ToOADate().ToString(CultureInfo.InvariantCulture));
                break;
            default:
                cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
                break;
        }
    }
}
