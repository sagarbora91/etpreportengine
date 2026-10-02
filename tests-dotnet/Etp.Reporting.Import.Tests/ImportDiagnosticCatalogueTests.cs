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
