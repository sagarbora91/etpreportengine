using System.Reflection;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>The whitelist of messages an attempt may keep for each code (spec 11.1).</summary>
public sealed class ImportDiagnosticCatalogueTests
{
    [Fact]
    public void Every_import_engine_code_has_a_template()
    {
        var codes = new[] { typeof(ImportCodes), typeof(ImportCodes.Contract) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!).Distinct().ToArray();
        Assert.True(codes.Length > 50);
        Assert.All(codes, code => Assert.True(ImportDiagnosticCatalogue.IsKnown(code), code));
    }

    [Fact]
    public void A_message_the_code_writes_is_kept_and_any_other_becomes_the_template()
    {
        const string written = "Unrecognised transaction type; this row was skipped.";
        Assert.Equal(written, ImportDiagnosticCatalogue.SafeMessage("UNKNOWN_SALES_TRANSACTION_TYPE", written));
        Assert.Equal(written, ImportDiagnosticCatalogue.SafeMessage("UNKNOWN_SALES_TRANSACTION_TYPE", "Customer Synthetic Zeta 9876500999"));
        // Another code's message is not this code's message.
        Assert.Equal(ImportDiagnosticCatalogue.Template("VALUE_REQUIRED"), ImportDiagnosticCatalogue.SafeMessage("VALUE_REQUIRED", written));
        Assert.Equal(ImportDiagnosticCatalogue.GenericMessage, ImportDiagnosticCatalogue.SafeMessage("SOMETHING_NEW", written));
        Assert.Equal(ImportDiagnosticCatalogue.GenericMessage, ImportDiagnosticCatalogue.SafeMessage(null, null));
    }

    // IF-027: the folder import's Not-needed reasons are catalogued Information codes, so the one row an attempt keeps
    // is stored with its own text and never as a blocker.
    [Theory]
    [InlineData(ImportCodes.ControlWorkbookNotNeeded, "Consolidation control workbook; report workbooks are imported separately.")]
    [InlineData(ImportCodes.ReportFamilyNotNeeded, "This ETP report type is not needed by the reporting engine; the other workbooks are processed.")]
    [InlineData(ImportCodes.NotAnEtpExport, "This CSV file is not an ETP export; it was skipped and the other files are processed.")]
    public void Not_needed_codes_are_information_with_their_own_message(string code, string message)
    {
        Assert.True(ImportDiagnosticCatalogue.IsKnown(code));
        Assert.Equal(ImportIssueSeverity.Information, ImportCodes.DefaultSeverity(code));
        Assert.Equal(message, ImportDiagnosticCatalogue.Template(code));
        Assert.Equal(message, ImportDiagnosticCatalogue.SafeMessage(code, message));
    }

    // 1.12.1 review M-3: the re-dated retire is said with its import number, dates and lost days; History keeps that text
    // (dates and numbers only) and nothing else under the code.
    [Fact]
    public void Redated_retired_message_names_the_import_the_dates_and_the_day_that_lost_its_reading_and_History_keeps_it()
    {
        var plain = ImportDiagnosticCatalogue.RedatedRetiredMessage(10092, new(2026, 10, 2), new(2026, 10, 2), new(2026, 10, 3), new(2026, 10, 3), [], 0, []);
        Assert.Equal("Import 10092 for 02 Oct 2026 was retired (same file, re-dated to 03 Oct 2026).", plain);
        var lost = ImportDiagnosticCatalogue.RedatedRetiredMessage(26, new(2026, 9, 29), new(2026, 9, 29), new(2026, 9, 7), new(2026, 9, 7),
            [new(2026, 9, 29)], 1234, [new(2026, 9, 29)]);
        Assert.Equal("Import 26 for 29 Sept 2026 was retired (same file, re-dated to 07 Sept 2026). 29 Sept 2026 now has no stock reading. "
            + "1,234 BinWise rows that stood in for missing Closing Stock rows on 29 Sept 2026 were archived with it.", lost);
        var many = ImportDiagnosticCatalogue.RedatedRetiredMessage(5, new(2026, 9, 1), new(2026, 9, 7), new(2026, 9, 8), new(2026, 9, 14),
            Enumerable.Range(1, 7).Select(day => new DateOnly(2026, 9, day)).ToArray(), 1, [new(2026, 9, 2)]);
        Assert.Contains("01 Sept 2026 to 07 Sept 2026", many, StringComparison.Ordinal);
        Assert.Contains("05 Sept 2026 and 2 more days now have no stock reading.", many, StringComparison.Ordinal);
        Assert.Contains("1 BinWise row that stood in for missing Closing Stock rows on 02 Sept 2026 was archived with it.", many, StringComparison.Ordinal);

        foreach (var message in new[] { plain, lost, many })
        {
            Assert.True(message.Length <= 500);
            Assert.Equal(message, ImportDiagnosticCatalogue.SafeMessage(ImportCodes.ImportRedatedRetired, message));
        }
        // Anything else under the code (or the dated form under another code) becomes the template.
        var template = ImportDiagnosticCatalogue.Template(ImportCodes.ImportRedatedRetired);
        Assert.Equal(template, ImportDiagnosticCatalogue.SafeMessage(ImportCodes.ImportRedatedRetired, plain + " Synthetic Customer Zeta"));
        Assert.Equal(template, ImportDiagnosticCatalogue.SafeMessage(ImportCodes.ImportRedatedRetired, @"Import 1 for C:\Shop\file.xlsx was retired (same file, re-dated to 03 Oct 2026)."));
        Assert.NotEqual(plain, ImportDiagnosticCatalogue.SafeMessage(ImportCodes.StockRowRepeated, plain));
        Assert.Equal(ImportIssueSeverity.Warning, ImportCodes.DefaultSeverity(ImportCodes.ImportRedatedRetired));
    }

    [Fact]
    public void Templates_hold_no_values()
    {
        Assert.All(ImportDiagnosticCatalogue.Codes, code =>
        {
            var template = ImportDiagnosticCatalogue.Template(code);
            Assert.False(string.IsNullOrWhiteSpace(template));
            Assert.True(template.Length <= 500, code);
            Assert.DoesNotMatch(@"\d{4,}|[\\/:]{2}|\{", template);
        });
    }

    [Fact]
    public void Stager_skips_are_issues_with_row_numbers_and_their_messages_are_whitelisted()
    {
        var headers = RetailSalesProfiles.R025Headers;
        WorkbookRow Row(int number, string type, string quantity, bool extra = false)
        {
            var values = new Dictionary<string, object?>
            {
                ["TRANS_TYPE"] = type, ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = $"10000000{number}",
                ["INVDATE"] = 20260825, ["QTY"] = quantity, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
            };
            var cells = headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToList();
            if (extra) cells.Add(new WorkbookCell("Synthetic Customer Zeta"));
            return new(number, cells);
        }
        var workbook = new WorkbookSnapshot("R025_synthetic.xlsx", 1, new string('c', 64), [new("SDB VariantwiseSales", 1, headers,
            [Row(2, "INV", "1"), Row(3, "XX", "1"), Row(4, "XX", "1"), Row(5, "INV", "1", extra: true), Row(6, "INV", "Synthetic Customer Zeta")])]);

        var issues = new MatchedImportEnvelopeFactory().Inspect(workbook).Diagnostics.Select(diagnostic => diagnostic.ToImportIssue()).ToArray();

        Assert.Equal(new int?[] { 3, 4 }, issues.Where(issue => issue.Code == "UNKNOWN_SALES_TRANSACTION_TYPE").Select(issue => issue.SourceRow));
        Assert.Equal(5, Assert.Single(issues, issue => issue.Code == "ROW_EXTRA_COLUMNS").SourceRow);
        Assert.Equal(6, Assert.Single(issues, issue => issue.Code == "VALUE_INVALID").SourceRow);
        Assert.All(issues, issue =>
        {
            Assert.Equal(issue.Message, ImportDiagnosticCatalogue.SafeMessage(issue.Code, issue.Message));
            Assert.DoesNotContain("Synthetic Customer", issue.Message);
            Assert.Equal(1, issue.Occurrences);
        });
    }

    [Fact]
    public void The_stock_parser_sheet_count_refusal_keeps_its_own_message()
    {
        var empty = new WorkbookSnapshot("stock_synthetic.xlsx", 1, new string('c', 64), []);

        var issue = Assert.Single(new StockWorkbookParser().Parse(empty).Diagnostics).ToImportIssue();

        Assert.Equal("WORKBOOK_SHEET_COUNT", issue.Code);
        Assert.True(ImportDiagnosticCatalogue.IsKnown(issue.Code));
        Assert.Equal(issue.Message, ImportDiagnosticCatalogue.SafeMessage(issue.Code, issue.Message));
    }
}
