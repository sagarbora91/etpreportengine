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
                // CASH_DEPOSIT keeps the template's 0 (1.11.2); CLOSING_CASH_COUNTED stays blank: left unchanged.
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
                // 1.12 U4: the field rows start at row 10 (title and instructions above them); the figures are unchanged.
                ("WALK_INS", 10, 18m, null), ("OPENING_CASH", 11, 5000m, null), ("EXPENSES", 12, 1250.75m, null),
                ("CASH_DEPOSIT", 13, 0m, null), ("CASH_ADJUSTMENT", 14, -40m, null), ("OPERATIONAL_REMARK", 16, null, "Synthetic remark")
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
                ["FILLED_BY"] = "Synthetic staff",
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
        // The keyed rows keep their order with the key in A and the value in C; the TEMPLATE row stays first under the header.
        var keyed = sheet.Rows.Where(r => CellString(r, 0) is not null).ToArray();
        Assert.Equal(
            [DailyInputsTemplate.TemplateKey, DailyInputsTemplate.KindKey, DailyInputsTemplate.StoreKey,
             DailyInputsTemplate.BusinessDateKey, DailyInputsTemplate.FilledByKey,
             .. DailyInputsTemplate.FieldsFor(kind).Select(f => f.FieldCode)],
            keyed.Select(r => CellString(r, 0)));
        Assert.Equal([2, 6, 7, 8, 9, .. Enumerable.Range(10, DailyInputsTemplate.FieldsFor(kind).Count)], keyed.Select(r => r.RowNumber));
        Assert.Same(keyed[0], sheet.Rows[0]);
        Assert.Equal(DailyInputsTemplate.CurrentVersion, CellString(keyed[0], 2));
        Assert.Equal(DailyInputsTemplate.KindText(kind), CellString(keyed[1], 2));
        Assert.Equal("WLMHW", CellString(keyed[2], 2));
        Assert.Equal("Synthetic store", CellString(keyed[2], 3));
        // 1.12 U4: the title, instruction and "when done" rows carry text in B only; A and C are empty, so the parser skips them.
        var banners = sheet.Rows.Where(r => CellString(r, 0) is null).ToArray();
        Assert.Equal([3, 4, 5, 10 + DailyInputsTemplate.FieldsFor(kind).Count + (kind == DailyInputsKind.Retail ? 1 : 0)], banners.Select(r => r.RowNumber));
        Assert.All(banners, r => { Assert.NotNull(CellString(r, 1)); Assert.Null(r.Cells.Count > 2 ? r.Cells[2].Value : null); });
        Assert.StartsWith("Synthetic store - daily figures", CellString(banners[0], 1));

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(workbook));
        var result = DailyInputsParser.Parse(workbook, Today);
        Assert.False(result.Succeeded);
        // 1.11.2: amounts and counts start at 0, so only the missing date and (decision 43) the empty Filled by are wrong.
        Assert.Equal([DailyInputsCodes.DateMissing, DailyInputsCodes.FilledByMissing], result.Errors.Select(e => e.Code));
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
        var worksheet = book.WorksheetParts.Single().Worksheet!;
        var protection = Assert.Single(worksheet.Elements<SheetProtection>());
        Assert.True(protection.Sheet?.Value);
        Assert.Null(protection.Password);
        Assert.Null(protection.HashValue);

        var formats = book.WorkbookStylesPart!.Stylesheet!.CellFormats!.Elements<CellFormat>().ToArray();
        bool Unlocked(Cell cell) => formats[(int)(cell.StyleIndex?.Value ?? 0)].Protection?.Locked?.Value == false;
        var unlocked = worksheet.Descendants<Cell>().Where(Unlocked).Select(c => c.CellReference!.Value!).ToArray();
        var fieldRows = DailyInputsTemplate.FieldsFor(DailyInputsKind.Retail).Count;
        Assert.Equal(["C8", "C9", .. Enumerable.Range(10, fieldRows).Select(row => $"C{row}")], unlocked);

        var validations = worksheet.Descendants<DataValidation>().ToDictionary(v => v.SequenceOfReferences!.InnerText ?? "");
        var dateRule = validations["C8"];
        Assert.Equal(DataValidationValues.Date, dateRule.Type!.Value);
        Assert.Equal($"TODAY()-{DailyInputsTemplate.LookbackDays}", dateRule.Formula1!.Text);
        Assert.Equal("TODAY()", dateRule.Formula2!.Text);
        Assert.Equal(DataValidationValues.TextLength, validations["C9"].Type!.Value);      // FILLED_BY
        Assert.Equal(DataValidationValues.Whole, validations["C10"].Type!.Value);         // WALK_INS
        Assert.Equal("0", validations["C11"].Formula1!.Text);                              // OPENING_CASH >= 0
        Assert.Equal(DataValidationOperatorValues.Between, validations["C14"].Operator!.Value); // CASH_ADJUSTMENT may be negative
        Assert.Equal(DataValidationValues.TextLength, validations["C16"].Type!.Value);    // OPERATIONAL_REMARK
    }

    [Fact]
    public async Task An_optional_business_date_fills_the_date_cell_and_the_filled_sheet_imports_with_that_date()
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
                var dateCell = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().Single(c => c.CellReference?.Value == "C8");
                Assert.Equal(date.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture), dateCell.CellValue?.Text);
                Assert.Null(dateCell.DataType);
            }

            var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
            var dateRow = workbook.Sheets.Single().Rows.Single(r => r.Cells[0].Value as string == DailyInputsTemplate.BusinessDateKey);
            Assert.Equal(date.ToDateTime(TimeOnly.MinValue), dateRow.Cells[2].Value);
            var result = DailyInputsParser.Parse(workbook, Today);
            // Decision 43 (1.11.2): saved untouched, the sheet is refused only for its empty Filled by; the date and zeros read.
            Assert.Equal([DailyInputsCodes.FilledByMissing], result.Errors.Select(e => e.Code));
            Assert.Equal(date, result.Document!.BusinessDate);
            Assert.All(result.Document.Values.Where(v => v.NumericValue is not null), v => Assert.Equal(0m, v.NumericValue));
            Assert.DoesNotContain(result.Document.Values, v => v.FieldCode is "OPENING_CASH" or "CLOSING_CASH_COUNTED");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Without_a_date_the_date_cell_stays_empty()
    {
        using var stream = new MemoryStream();
        DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Service, "WLMHW", "Synthetic store");
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var dateCell = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().Single(c => c.CellReference?.Value == "C8");
        Assert.Null(dateCell.CellValue);
        Assert.Null(dateCell.InlineString);
    }

    [Fact]
    public void Staff_see_labels_first_the_codes_and_key_rows_are_hidden_and_the_cash_check_sits_under_the_table()
    {
        using var stream = new MemoryStream();
        DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, "HEMW", "Synthetic store", new DateOnly(2026, 10, 9));
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var worksheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;

        // Decision 45 #7 (OUT-23): column A keeps the codes for the parser but is hidden; so are the header and key rows.
        Assert.True(worksheet.Descendants<Column>().Single(c => c.Min!.Value == 1).Hidden?.Value);
        var hidden = worksheet.Descendants<Row>().Where(r => r.Hidden?.Value == true).Select(r => r.RowIndex!.Value).ToArray();
        Assert.Equal([1u, 2u, 6u, 7u], hidden);
        Assert.DoesNotContain(worksheet.Descendants<Column>(), c => c.Min!.Value >= 5);

        // OUT-24/26: title with the store name and the day, instructions, 125 % zoom, frozen above the boxes.
        string? Text(string reference) => worksheet.Descendants<Cell>().SingleOrDefault(c => c.CellReference?.Value == reference)?.InnerText;
        Assert.StartsWith("Synthetic store - daily figures for", Text("B3"));
        Assert.Contains("yellow boxes", Text("B4"), StringComparison.Ordinal);
        var view = worksheet.Descendants<SheetView>().Single();
        Assert.Equal(125u, view.ZoomScale!.Value);
        Assert.Equal(PaneStateValues.Frozen, view.Pane!.State!.Value);
        Assert.Contains(worksheet.Descendants<MergeCell>(), m => m.Reference!.Value == "B3:D3");

        // OUT-25: the cash check formula is in column B of the row under the last field, merged across B:D; nothing in A or C.
        var lastField = 10 + DailyInputsTemplate.FieldsFor(DailyInputsKind.Retail).Count - 1;
        var checkRow = worksheet.Descendants<Row>().Single(r => r.RowIndex!.Value == (uint)lastField + 1);
        var formula = Assert.Single(worksheet.Descendants<CellFormula>());
        Assert.Equal($"B{lastField + 1}", ((Cell)formula.Parent!).CellReference!.Value);
        Assert.Contains("C15", formula.Text, StringComparison.Ordinal); // CLOSING_CASH_COUNTED
        Assert.Contains("Cash check", formula.Text, StringComparison.Ordinal);
        Assert.Single(checkRow.Elements<Cell>());
        Assert.Contains(worksheet.Descendants<MergeCell>(), m => m.Reference!.Value == $"B{lastField + 1}:D{lastField + 1}");
    }

    [Fact]
    public async Task The_new_layout_reads_the_same_figures_as_a_sheet_in_the_old_layout()
    {
        // Same input, same figures: the 1.11.2 layout (keys in rows 2-6, fields from row 7, as on the shop PCs today) and the
        // 1.12 staff layout give the parser identical values; only the row numbers differ.
        var date = new DateOnly(2026, 10, 10);
        var values = new (string Code, object? Value)[]
        {
            ("WALK_INS", 18m), ("OPENING_CASH", 5000m), ("EXPENSES", 1250.75m), ("CASH_DEPOSIT", 0m), ("CASH_ADJUSTMENT", -40m),
            ("CLOSING_CASH_COUNTED", 12345.5m), ("OPERATIONAL_REMARK", "Synthetic remark")
        };
        var filled = new Dictionary<string, object?> { ["BUSINESS_DATE"] = date.ToDateTime(TimeOnly.MinValue), ["FILLED_BY"] = "Synthetic staff" };
        foreach (var (code, value) in values) filled[code] = value;
        var fileName = DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, date);

        var current = DailyInputsParser.Parse(await WriteFillAndReadAsync(DailyInputsKind.Retail, "HEMW", fileName, filled), Today);
        var old = DailyInputsParser.Parse(Sheet(DailyInputsKind.Retail, "HEMW", date.ToDateTime(TimeOnly.MinValue), fileName, fields: values), Today);

        Assert.True(current.Succeeded, string.Join(", ", current.Errors.Select(e => e.Code)));
        Assert.True(old.Succeeded, string.Join(", ", old.Errors.Select(e => e.Code)));
        Assert.Equal(old.Document!.Values.Select(v => (v.FieldCode, v.NumericValue, v.TextValue)), current.Document!.Values.Select(v => (v.FieldCode, v.NumericValue, v.TextValue)));
        Assert.Equal((old.Document.StoreCode, old.Document.Kind, old.Document.BusinessDate, old.Document.FilledBy),
            (current.Document.StoreCode, current.Document.Kind, current.Document.BusinessDate, current.Document.FilledBy));
    }

    [Fact]
    public async Task A_cash_check_value_saved_by_Excel_under_the_table_is_skipped_by_the_parser()
    {
        // Excel stores the formula's text result in B; the row has no key in A and no value in C, so it is not a field.
        var date = new DateOnly(2026, 10, 10);
        var folder = Path.Combine(Path.GetTempPath(), "etp-daily-inputs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, date));
            using (var stream = new MemoryStream())
            {
                DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, "HEMW", "Synthetic store", date);
                stream.Position = 0;
                using (var document = SpreadsheetDocument.Open(stream, true))
                {
                    var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;
                    var formulaCell = (Cell)sheet.Descendants<CellFormula>().Single().Parent!;
                    formulaCell.CellValue = new CellValue("Cash check: cash taken today, from your count: ₹1,000.00");
                    var name = sheet.Descendants<Cell>().Single(c => c.CellReference?.Value == "C9");
                    name.DataType = CellValues.InlineString; name.InlineString = new InlineString(new Text("Synthetic staff"));
                    sheet.Save();
                }
                await File.WriteAllBytesAsync(path, stream.ToArray());
            }
            var result = DailyInputsParser.Parse(await new OpenXmlWorkbookReader().ReadAsync(path), Today);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        }
        finally { Directory.Delete(folder, recursive: true); }
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
