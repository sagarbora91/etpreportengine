using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.DailyInputs.DailyInputsTestSheets;

namespace Etp.Reporting.Import.Tests.DailyInputs;

/// <summary>The generated template (design section 1) and its round trip through the real reader and the parser.</summary>
public sealed class DailyInputsTemplateWriterTests
{
    [Fact]
    public async Task A_filled_retail_template_round_trips_through_the_reader_and_the_parser()
    {
        var date = new DateOnly(2026, 10, 10);
        var workbook = await WriteFillAndReadAsync(DailyInputsKind.Retail, "HEMW",
            DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, date),
            new Dictionary<string, object?>
            {
                ["BUSINESS_DATE"] = date.ToDateTime(TimeOnly.MinValue),
                ["FILLED_BY"] = "Cashier",
                ["WALK_INS"] = 18m,
                ["OPENING_CASH"] = 5000m,
                ["EXPENSES"] = "1,250.75",
                ["CASH_ADJUSTMENT"] = -40m,
                ["OPERATIONAL_REMARK"] = "Synthetic remark"
                // CASH_DEPOSIT and CLOSING_CASH_COUNTED stay blank: left unchanged.
            });

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(workbook));
        var result = DailyInputsParser.Parse(workbook, Today);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        Assert.Empty(result.Notes);
        var document = result.Document!;
        Assert.Equal((DailyInputsKind.Retail, "HEMW", date, "Cashier"), (document.Kind, document.StoreCode, document.BusinessDate, document.FilledBy));
        Assert.Equal(workbook.Sha256, document.Sha256);
        Assert.Equal(workbook.FileName, document.FileName);
        Assert.Equal(
            new (string, int, decimal?, string?)[]
            {
                ("WALK_INS", 7, 18m, null), ("OPENING_CASH", 8, 5000m, null), ("EXPENSES", 9, 1250.75m, null),
                ("CASH_ADJUSTMENT", 11, -40m, null), ("OPERATIONAL_REMARK", 13, null, "Synthetic remark")
            },
            document.Values.Select(v => (v.FieldCode, v.RowNumber, v.NumericValue, v.TextValue)));
    }

    [Fact]
    public async Task A_filled_service_template_round_trips_through_the_reader_and_the_parser()
    {
        var workbook = await WriteFillAndReadAsync(DailyInputsKind.Service, "WLMHW",
            DailyInputsTemplate.FileName("WLMHW", DailyInputsKind.Service, Today),
            new Dictionary<string, object?>
            {
                ["BUSINESS_DATE"] = "11-10-2026",
                ["SERVICE_CASH"] = 1500m,
                ["SERVICE_CARD"] = 900.5m,
                ["SERVICE_UPI"] = 0m,
                ["SERVICE_WDC"] = 350m,
                ["WCC_WALKIN"] = 4m,
                ["WCC_SALES"] = 2200m,
                ["WDC_BILLS"] = 1m
            });

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(workbook));
        var result = DailyInputsParser.Parse(workbook, Today);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        Assert.Equal((DailyInputsKind.Service, "WLMHW", Today), (result.Document!.Kind, result.Document.StoreCode, result.Document.BusinessDate));
        Assert.Equal(DailyInputsTemplate.FieldsFor(DailyInputsKind.Service).Select(f => f.FieldCode), result.Document.Values.Select(v => v.FieldCode));
        Assert.Equal(0m, result.Document.Values.Single(v => v.FieldCode == "SERVICE_UPI").NumericValue);
    }

    [Theory]
    [InlineData(DailyInputsKind.Retail)]
    [InlineData(DailyInputsKind.Service)]
    public async Task The_blank_template_has_the_design_layout_and_is_recognised_but_not_importable(DailyInputsKind kind)
    {
        var workbook = await WriteFillAndReadAsync(kind, "WLMHW", "template.xlsx", new Dictionary<string, object?>());

        var sheet = Assert.Single(workbook.Sheets);
        Assert.Equal(DailyInputsTemplate.SheetName, sheet.Name);
        Assert.Equal(1, sheet.HeaderRowNumber);
        Assert.Equal(DailyInputsTemplate.Headers, sheet.Headers);
        string? CellString(WorkbookRow row, int column) => row.Cells.Count > column ? row.Cells[column].Value as string : null;
        Assert.Equal(
            [DailyInputsTemplate.TemplateKey, DailyInputsTemplate.KindKey, DailyInputsTemplate.StoreKey,
             DailyInputsTemplate.BusinessDateKey, DailyInputsTemplate.FilledByKey,
             .. DailyInputsTemplate.FieldsFor(kind).Select(f => f.FieldCode)],
            sheet.Rows.Select(r => CellString(r, 0)));
        Assert.Equal(Enumerable.Range(2, 5 + DailyInputsTemplate.FieldsFor(kind).Count), sheet.Rows.Select(r => r.RowNumber));
        Assert.Equal(DailyInputsTemplate.CurrentVersion, CellString(sheet.Rows[0], 2));
        Assert.Equal(DailyInputsTemplate.KindText(kind), CellString(sheet.Rows[1], 2));
        Assert.Equal("WLMHW", CellString(sheet.Rows[2], 2));
        Assert.Equal("Synthetic store", CellString(sheet.Rows[2], 3));

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(workbook));
        var result = DailyInputsParser.Parse(workbook, Today);
        Assert.False(result.Succeeded);
        Assert.Equal([DailyInputsCodes.DateMissing, DailyInputsCodes.NoValues], result.Errors.Select(e => e.Code));
    }

    [Fact]
    public void The_sheet_is_protected_without_a_password_and_only_the_staff_cells_are_unlocked()
    {
        using var stream = new MemoryStream();
        DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, "HEMW", "Synthetic store");
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var book = document.WorkbookPart!;

        Assert.Null(book.VbaProjectPart);
        var worksheet = book.WorksheetParts.Single().Worksheet;
        var protection = Assert.Single(worksheet.Elements<SheetProtection>());
        Assert.True(protection.Sheet?.Value);
        Assert.Null(protection.Password);
        Assert.Null(protection.HashValue);

        var formats = book.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().ToArray();
        bool Unlocked(Cell cell) => formats[(int)(cell.StyleIndex?.Value ?? 0)].Protection?.Locked?.Value == false;
        var unlocked = worksheet.Descendants<Cell>().Where(Unlocked).Select(c => c.CellReference!.Value!).ToArray();
        var fieldRows = DailyInputsTemplate.FieldsFor(DailyInputsKind.Retail).Count;
        Assert.Equal(["C5", "C6", .. Enumerable.Range(7, fieldRows).Select(row => $"C{row}")], unlocked);

        var validations = worksheet.Descendants<DataValidation>().ToDictionary(v => v.SequenceOfReferences!.InnerText ?? "");
        var dateRule = validations["C5"];
        Assert.Equal(DataValidationValues.Date, dateRule.Type!.Value);
        Assert.Equal($"TODAY()-{DailyInputsTemplate.LookbackDays}", dateRule.Formula1!.Text);
        Assert.Equal("TODAY()", dateRule.Formula2!.Text);
        Assert.Equal(DataValidationValues.Whole, validations["C7"].Type!.Value);          // WALK_INS
        Assert.Equal("0", validations["C8"].Formula1!.Text);                               // OPENING_CASH >= 0
        Assert.Equal(DataValidationOperatorValues.Between, validations["C11"].Operator!.Value); // CASH_ADJUSTMENT may be negative
        Assert.Equal(DataValidationValues.TextLength, validations["C13"].Type!.Value);     // OPERATIONAL_REMARK
    }

    [Fact]
    public async Task An_optional_business_date_fills_C5_and_the_filled_sheet_imports_with_that_date()
    {
        var date = new DateOnly(2026, 10, 9);
        var folder = Path.Combine(Path.GetTempPath(), "etp-daily-inputs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, date));
            using (var file = File.Create(path))
                DailyInputsTemplateWriter.Write(file, DailyInputsKind.Retail, "HEMW", "Synthetic store", date);

            using (var document = SpreadsheetDocument.Open(path, false))
            {
                var c5 = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c => c.CellReference?.Value == "C5");
                Assert.Equal(date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture), c5.CellValue?.Text);
                Assert.Null(c5.DataType);
            }

            var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
            var dateRow = workbook.Sheets.Single().Rows.Single(r => r.Cells[0].Value as string == DailyInputsTemplate.BusinessDateKey);
            Assert.Equal(date.ToDateTime(TimeOnly.MinValue), dateRow.Cells[2].Value);
            var result = DailyInputsParser.Parse(workbook, Today);
            Assert.Equal([DailyInputsCodes.NoValues], result.Errors.Select(e => e.Code));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Without_a_date_C5_stays_empty_and_unlocked()
    {
        using var stream = new MemoryStream();
        DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Service, "WLMHW", "Synthetic store");
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var c5 = document.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Cell>().Single(c => c.CellReference?.Value == "C5");
        Assert.Null(c5.CellValue);
        Assert.Null(c5.InlineString);
    }

    [Fact]
    public void A_blank_store_code_is_refused()
    {
        using var stream = new MemoryStream();
        Assert.ThrowsAny<ArgumentException>(() => DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, " ", "Synthetic store"));
    }

    /// <summary>
    /// Explicit: writes the three shop-PC templates into the folder named by ETP_DAILY_INPUTS_TEMPLATE_OUT (the integrator
    /// then places them in E:\ETP\Installers\Daily inputs templates\). Without the variable it does nothing.
    /// </summary>
    [Fact]
    public void WriteShopTemplates()
    {
        var folder = Environment.GetEnvironmentVariable("ETP_DAILY_INPUTS_TEMPLATE_OUT");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        foreach (var (kind, store, label) in new[]
        {
            (DailyInputsKind.Retail, "WLMHW", "Titan World"),
            (DailyInputsKind.Retail, "HEMW", "Helios"),
            (DailyInputsKind.Service, "WLMHW", "Titan World (Service money)")
        })
        {
            // The yyyy-MM-dd placeholder shows staff what to replace on Save As; it is not a date, so a file sent
            // without renaming still imports with a DAILY_INPUTS_NAME_UNUSUAL note.
            var path = Path.Combine(folder, $"{DailyInputsTemplate.FileNamePrefix}-{store}-{DailyInputsTemplate.KindText(kind)}-yyyy-MM-dd.xlsx");
            using var file = File.Create(path);
            DailyInputsTemplateWriter.Write(file, kind, store, label);
        }
    }
}
