using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;
using static Etp.Reporting.Import.Tests.DailyInputs.DailyInputsTestSheets;

namespace Etp.Reporting.Import.Tests.DailyInputs;

public sealed class DailyInputsParserTests
{
    private static DailyInputsParseResult Parse(Etp.Reporting.Import.Workbooks.WorkbookSnapshot workbook) =>
        DailyInputsParser.Parse(workbook, Today);

    private static string[] ErrorCodes(DailyInputsParseResult result) => result.Errors.Select(e => e.Code).ToArray();

    [Fact]
    public void A_filled_retail_sheet_parses_store_kind_date_and_values()
    {
        var result = Parse(Sheet(filledBy: "Cashier", fields: [("WALK_INS", 14m), ("OPENING_CASH", 2500m), ("OPERATIONAL_REMARK", "  Power cut at noon  ")]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        var document = result.Document!;
        Assert.Equal(DailyInputsKind.Retail, document.Kind);
        Assert.Equal("HEMW", document.StoreCode);
        Assert.Equal(Today, document.BusinessDate);
        Assert.Equal("Cashier", document.FilledBy);
        Assert.Equal(DailyInputsTemplate.CurrentVersion, document.TemplateVersion);
        Assert.Equal(new string('d', 64), document.Sha256);
        Assert.Collection(document.Values,
            v => { Assert.Equal(("WALK_INS", 7, 14m), (v.FieldCode, v.RowNumber, v.NumericValue!.Value)); Assert.Null(v.TextValue); },
            v => Assert.Equal(("OPENING_CASH", DailyInputValueKind.Money, 2500m), (v.FieldCode, v.ValueKind, v.NumericValue!.Value)),
            v => { Assert.Equal("Power cut at noon", v.TextValue); Assert.Null(v.NumericValue); });
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void A_blank_value_emits_no_row_and_an_all_blank_sheet_has_no_values()
    {
        var some = Parse(Sheet(fields: [("WALK_INS", null), ("EXPENSES", 300m), ("OPERATIONAL_REMARK", "   ")]));
        Assert.True(some.Succeeded);
        Assert.Equal(["EXPENSES"], some.Document!.Values.Select(v => v.FieldCode));

        var none = Parse(Sheet(fields: [("WALK_INS", null), ("EXPENSES", null)]));
        Assert.False(none.Succeeded);
        Assert.Equal([DailyInputsCodes.NoValues], ErrorCodes(none));
        Assert.NotNull(none.Document); // store and date still known for the failure register
    }

    [Theory]
    [InlineData("2026-10-05")]
    [InlineData("05-10-2026")]
    [InlineData("05/10/2026")]
    [InlineData("05-Oct-2026")]
    [InlineData(" 5-Oct-2026 ")]
    public void The_business_date_reads_each_text_form(string date)
    {
        var result = Parse(Sheet(date: date, fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-05.xlsx", fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal(new DateOnly(2026, 10, 5), result.Document!.BusinessDate);
    }

    [Fact]
    public void The_business_date_reads_as_a_date_cell()
    {
        var result = Parse(Sheet(date: new DateTime(2026, 10, 5), fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal(new DateOnly(2026, 10, 5), result.Document!.BusinessDate);
    }

    [Fact]
    public void A_september_date_reads_whatever_the_culture_abbreviation()
    {
        var result = Parse(Sheet(date: "20-Sep-2026", fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-09-20.xlsx", fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal(new DateOnly(2026, 9, 20), result.Document!.BusinessDate);
    }

    [Theory]
    [InlineData("11-Sept-2026")]
    [InlineData("11-SEPT-2026")]
    [InlineData("11-Sep-2026")]
    public void A_september_date_is_read_with_either_spelling(string typed)
    {
        var result = Parse(Sheet(date: typed, fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-09-11.xlsx", fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal(new DateOnly(2026, 9, 11), result.Document!.BusinessDate);
    }

    [Fact]
    public void A_future_date_fails()
    {
        var result = Parse(Sheet(date: new DateTime(2026, 10, 12), fields: [("WALK_INS", 3m)]));

        Assert.Equal([DailyInputsCodes.DateInFuture], ErrorCodes(result));
        Assert.Equal(5, result.Errors[0].RowNumber);
    }

    [Fact]
    public void The_lookback_is_inclusive_and_an_older_date_fails()
    {
        var oldest = Today.AddDays(-DailyInputsTemplate.LookbackDays).ToDateTime(TimeOnly.MinValue);
        Assert.True(Parse(Sheet(date: oldest, fields: [("WALK_INS", 3m)])).Succeeded);

        var tooOld = Parse(Sheet(date: oldest.AddDays(-1), fields: [("WALK_INS", 3m)]));
        Assert.Equal([DailyInputsCodes.DateTooOld], ErrorCodes(tooOld));
    }

    [Theory]
    [InlineData(null, DailyInputsCodes.DateMissing)]
    [InlineData("  ", DailyInputsCodes.DateMissing)]
    [InlineData("next Tuesday", DailyInputsCodes.DateInvalid)]
    [InlineData("31-02-2026", DailyInputsCodes.DateInvalid)]
    public void A_missing_or_unreadable_date_fails_without_a_document(string? date, string code)
    {
        var workbook = Sheet(fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx", fields: [("WALK_INS", 3m)]);
        var sheet = workbook.Sheets[0];
        var rows = sheet.Rows.Select(r => r.RowNumber == 5
            ? r with { Cells = [r.Cells[0], r.Cells[1], new(date, date), r.Cells[3]] }
            : r).ToArray();
        var result = Parse(workbook with { Sheets = [sheet with { Rows = rows }] });

        Assert.Equal([code], ErrorCodes(result));
        Assert.Null(result.Document);
    }

    [Fact]
    public void A_bare_number_in_the_date_cell_is_not_a_date()
    {
        var result = Parse(Sheet(date: 46306m, fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx", fields: [("WALK_INS", 3m)]));

        Assert.Contains(DailyInputsCodes.DateInvalid, ErrorCodes(result));
    }

    [Fact]
    public void A_fractional_quantity_is_invalid()
    {
        var result = Parse(Sheet(fields: [("WALK_INS", 2.5m), ("OPENING_CASH", 100m)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal((DailyInputsCodes.ValueInvalid, 7, "WALK_INS"), (error.Code, error.RowNumber!.Value, error.FieldCode!));
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void A_negative_expense_fails_and_a_negative_cash_adjustment_is_accepted()
    {
        var negative = Parse(Sheet(fields: [("EXPENSES", -50m)]));
        Assert.Equal([DailyInputsCodes.ValueNegative], ErrorCodes(negative));
        Assert.Equal("EXPENSES", negative.Errors[0].FieldCode);

        var adjustment = Parse(Sheet(fields: [("CASH_ADJUSTMENT", -120.5m)]));
        Assert.True(adjustment.Succeeded);
        Assert.Equal(-120.5m, adjustment.Document!.Values.Single().NumericValue);
    }

    [Theory]
    [InlineData("1,500.50", 1500.50)]
    [InlineData("₹ 200", 200)]
    [InlineData("₹1,23,456", 123456)]
    [InlineData(" 75 ", 75)]
    public void Money_text_with_commas_spaces_or_the_rupee_sign_parses(string text, double expected)
    {
        var result = Parse(Sheet(fields: [("OPENING_CASH", text)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal((decimal)expected, result.Document!.Values.Single().NumericValue);
    }

    [Fact]
    public void Money_is_rounded_to_two_places_and_text_that_is_not_a_number_is_invalid()
    {
        var rounded = Parse(Sheet(fields: [("OPENING_CASH", 10.005m)]));
        Assert.Equal(10.01m, rounded.Document!.Values.Single().NumericValue);

        var words = Parse(Sheet(fields: [("OPENING_CASH", "two hundred")]));
        Assert.Equal([DailyInputsCodes.ValueInvalid], ErrorCodes(words));
    }

    [Fact]
    public void Text_over_the_limit_fails_and_at_the_limit_passes()
    {
        var atLimit = Parse(Sheet(fields: [("OPERATIONAL_REMARK", new string('x', DailyInputsTemplate.MaxTextLength))]));
        Assert.True(atLimit.Succeeded);

        var tooLong = Parse(Sheet(fields: [("OPERATIONAL_REMARK", new string('x', DailyInputsTemplate.MaxTextLength + 1))]));
        Assert.Equal([DailyInputsCodes.TextTooLong], ErrorCodes(tooLong));
    }

    [Fact]
    public void Filled_by_over_its_limit_fails()
    {
        var result = Parse(Sheet(filledBy: new string('n', DailyInputsTemplate.MaxFilledByLength + 1), fields: [("WALK_INS", 3m)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal((DailyInputsCodes.TextTooLong, 6), (error.Code, error.RowNumber!.Value));
    }

    [Fact]
    public void An_unknown_code_a_service_code_on_a_retail_sheet_and_a_repeated_code_each_fail_on_their_row()
    {
        var result = Parse(Sheet(fields: [("WALK_INS", 3m), ("SALES_TARGET", 1000m), ("SERVICE_CASH", 200m), ("WALK_INS", 4m)]));

        Assert.False(result.Succeeded);
        Assert.Equal(
            [(8, DailyInputsCodes.UnknownField), (9, DailyInputsCodes.FieldNotForSheet), (10, DailyInputsCodes.DuplicateField)],
            result.Errors.Select(e => (e.RowNumber!.Value, e.Code)));
    }

    [Fact]
    public void A_retail_code_on_a_service_sheet_fails()
    {
        var result = Parse(Sheet(kind: DailyInputsKind.Service, store: "WLMHW", fields: [("SERVICE_CASH", 200m), ("OPENING_CASH", 100m)]));

        Assert.Equal([DailyInputsCodes.FieldNotForSheet], ErrorCodes(result));
    }

    [Fact]
    public void A_service_sheet_parses_its_own_fields()
    {
        var result = Parse(Sheet(kind: DailyInputsKind.Service, store: "WLMHW",
            fields: [("SERVICE_CASH", 1200m), ("SERVICE_UPI", 800m), ("WCC_WALKIN", 6m), ("WDC_BILLS", 2m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal(DailyInputsKind.Service, result.Document!.Kind);
        Assert.Equal(["SERVICE_CASH", "SERVICE_UPI", "WCC_WALKIN", "WDC_BILLS"], result.Document.Values.Select(v => v.FieldCode));
    }

    [Theory]
    [InlineData("ETP-DAILY-INPUTS-2")]
    [InlineData("ETP-DAILY-INPUTS-")]
    public void An_unknown_template_version_fails_before_anything_else(string version)
    {
        var result = Parse(Sheet(version: version, fields: [("WALK_INS", 3m)]));

        Assert.Equal([DailyInputsCodes.TemplateVersion], ErrorCodes(result));
        Assert.Null(result.Document);
    }

    [Fact]
    public void An_invalid_kind_and_a_missing_store_fail()
    {
        var workbook = Sheet(store: "  ", fileName: "x.xlsx", fields: [("WALK_INS", 3m)]);
        var sheet = workbook.Sheets[0];
        var rows = sheet.Rows.Select(r => r.RowNumber == 3
            ? r with { Cells = [r.Cells[0], r.Cells[1], new("STOCK", "STOCK"), r.Cells[3]] }
            : r).ToArray();

        var result = Parse(workbook with { Sheets = [sheet with { Rows = rows }] });

        Assert.Equal([DailyInputsCodes.KindInvalid, DailyInputsCodes.StoreMissing], ErrorCodes(result));
        Assert.Null(result.Document);
    }

    [Fact]
    public void The_store_code_is_upper_cased()
    {
        var result = Parse(Sheet(store: "hemw", fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx", fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded, string.Join(", ", ErrorCodes(result)));
        Assert.Equal("HEMW", result.Document!.StoreCode);
    }

    [Theory]
    [InlineData("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-10.xlsx")] // yesterday's file re-saved without changing the date
    [InlineData("ETP-Daily-Inputs-WLMHW-RETAIL-2026-10-11.xlsx")] // other store
    [InlineData("ETP-Daily-Inputs-HEMW-SERVICE-2026-10-11.xlsx")] // other sheet type
    [InlineData("ETP-Daily-Inputs-HEMW-RETAIL-2026-13-45.xlsx")]  // not a date
    public void A_file_name_that_disagrees_with_the_sheet_fails(string fileName)
    {
        var result = Parse(Sheet(fileName: fileName, fields: [("WALK_INS", 3m)]));

        var error = Assert.Single(result.Errors);
        Assert.Equal(DailyInputsCodes.NameMismatch, error.Code);
        Assert.Null(error.RowNumber);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx")]
    [InlineData("etp-daily-inputs-hemw-retail-2026-10-11.xlsx")]
    [InlineData("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11 (1).xlsx")]
    public void A_matching_file_name_passes_with_no_note(string fileName)
    {
        var result = Parse(Sheet(fileName: fileName, fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Notes);
    }

    [Theory]
    [InlineData("Daily inputs Helios.xlsx")]
    [InlineData("ETP-Daily-Inputs-HEMW-RETAIL-yyyy-MM-dd.xlsx")]
    public void An_unusual_file_name_is_an_information_note_only(string fileName)
    {
        var result = Parse(Sheet(fileName: fileName, fields: [("WALK_INS", 3m)]));

        Assert.True(result.Succeeded);
        var note = Assert.Single(result.Notes);
        Assert.Equal(DailyInputsCodes.NameUnusual, note.Code);
    }

    [Fact]
    public void A_value_with_no_field_code_fails()
    {
        var result = Parse(Sheet(fields: [("WALK_INS", 3m), ("", 99m)]));

        Assert.Equal([DailyInputsCodes.UnknownField], ErrorCodes(result));
        Assert.Equal(8, result.Errors[0].RowNumber);
    }

    [Fact]
    public void A_sheet_that_is_not_the_template_fails_on_its_version()
    {
        var result = Parse(Sheet(sheetName: "Sheet1", fields: [("WALK_INS", 3m)]));

        Assert.Equal([DailyInputsCodes.TemplateVersion], ErrorCodes(result));
    }

    [Fact]
    public void Every_issue_message_is_the_catalogue_template_and_kept_as_written()
    {
        var result = Parse(Sheet(date: new DateTime(2026, 10, 12), filledBy: new string('n', 101),
            fields: [("WALK_INS", 2.5m), ("EXPENSES", -1m), ("SERVICE_CASH", 1m), ("NOPE", 1m), ("WALK_INS", 1m),
                ("OPERATIONAL_REMARK", new string('x', 1001))]));

        Assert.True(result.Errors.Count >= 6);
        Assert.All(result.Errors.Concat(result.Notes), issue =>
        {
            Assert.True(ImportDiagnosticCatalogue.IsKnown(issue.Code), issue.Code);
            Assert.Equal(ImportDiagnosticCatalogue.Template(issue.Code), issue.Message);
            Assert.Equal(issue.Message, ImportDiagnosticCatalogue.SafeMessage(issue.Code, issue.Message));
        });
    }
}
