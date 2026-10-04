using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// Lane L9: a raw export with title rows above its header (EMPOWERMENT REPORT: header on row 12) still matches its
/// family. R025's header is used because it is catalogued on every branch; the S families match the same way.
/// </summary>
public sealed class RawTitleRowTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EtpRawTitle-" + Guid.NewGuid().ToString("N"));

    public RawTitleRowTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static object?[] SalesRow(string invoice) => RetailSalesProfiles.R025Headers.Select(header => header switch
    {
        "TRANS_TYPE" => "INV", "STORE CODE" => "HEMW", "ITEMNUMBER" => "TEST-001", "INVNUMBER" => invoice,
        "INVDATE" => 20260825m, "QTY" => 1m, "NETAMOUNT" => 118m, "NETVALUE" => 100m, "TAX" => (object?)18m, _ => null
    }).ToArray();

    // The real export's shape: nine title and filter rows (some key/value pairs), two blank rows, then the header on row 12.
    private static IReadOnlyList<IReadOnlyList<object?>> TitledGrid(params object?[][] data) =>
    [
        ["Report Name", "Sample Report"], ["Service Centre", "AW330"], ["Brand"], ["Region"], ["Territory"], ["Location"],
        ["Status"], ["From Date", "06-10-2026"], ["To Date", "09-10-2026"], [], [],
        RetailSalesProfiles.R025Headers.Cast<object?>().ToArray(), .. data
    ];

    [Fact]
    public async Task A_header_below_title_rows_matches_and_only_the_rows_below_it_are_data()
    {
        var path = Path.Combine(folder, "SAMPLE REPORT 06.10.2026 TO 09.10.2026.xlsx");
        AuditFixtureWorkbooks.WriteWorkbook(path, [("SampleReport", TitledGrid(SalesRow("100000001"), SalesRow("100000002")))]);
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);

        var result = new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All);

        Assert.Equal("R025", result.Profile?.ReportCode);
        Assert.True(result.CanImport, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Code)));
        Assert.Equal(12, result.Sheet!.HeaderRowNumber);
        Assert.Equal(RetailSalesProfiles.R025Headers, result.Sheet.Headers);
        Assert.Equal(new[] { 13, 14 }, result.Sheet.Rows.Select(row => row.RowNumber));
        var note = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == ImportPreflight.HeaderBelowTitleRows);
        Assert.Equal(ImportDiagnosticSeverity.Information, note.Severity);
        Assert.Equal(12, note.RowNumber);
    }

    [Fact]
    public async Task A_header_below_title_rows_in_a_csv_matches_too()
    {
        var path = Path.Combine(folder, "SAMPLE REPORT 06.10.2026 TO 09.10.2026.csv");
        var lines = new List<string> { "\"Sample Report\"", "\"From Date\",\"06-10-2026\"", "",
            string.Join(",", RetailSalesProfiles.R025Headers.Select(header => $"\"{header}\"")),
            string.Join(",", SalesRow("100000001").Select(value => $"\"{value}\"")) };
        await File.WriteAllLinesAsync(path, lines);

        var result = new ImportPreflight().Inspect(await new CsvWorkbookReader().ReadAsync(path), ApprovedImportProfileRegistry.All);

        Assert.Equal("R025", result.Profile?.ReportCode);
        Assert.Equal(4, result.Sheet!.HeaderRowNumber);
        Assert.Single(result.Sheet.Rows);
    }

    [Fact]
    public async Task A_sheet_whose_own_header_matches_is_left_as_it_is()
    {
        var path = Path.Combine(folder, "R025_sales.xlsx");
        AuditFixtureWorkbooks.WriteWorkbook(path,
            [("SDB VariantwiseSales", [RetailSalesProfiles.R025Headers.Cast<object?>().ToArray(), SalesRow("100000001")])]);

        var result = new ImportPreflight().Inspect(await new OpenXmlWorkbookReader().ReadAsync(path), ApprovedImportProfileRegistry.All);

        Assert.Equal("R025", result.Profile?.ReportCode);
        Assert.Equal(1, result.Sheet!.HeaderRowNumber);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == ImportPreflight.HeaderBelowTitleRows);
    }

    [Fact]
    public async Task Title_rows_over_an_unknown_header_stay_an_unknown_layout()
    {
        var path = Path.Combine(folder, "TATA REPORT 03.09.2026 TO 03.10.2026 .xlsx");
        AuditFixtureWorkbooks.WriteWorkbook(path,
            [("TATReport", [["Report Name", "Sample"], [], ["Synthetic A", "Synthetic B", "Synthetic C"], ["1", "2", "3"]])]);

        var result = new ImportPreflight().Inspect(await new OpenXmlWorkbookReader().ReadAsync(path), ApprovedImportProfileRegistry.All);

        Assert.Null(result.Profile);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "LAYOUT_UNKNOWN");
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == ImportPreflight.HeaderBelowTitleRows);
    }

    [Fact]
    public void A_header_deeper_than_the_search_depth_is_not_looked_for()
    {
        var titles = Enumerable.Range(2, 31).Select(number => new WorkbookRow(number, [new WorkbookCell("Title", "Title")]));
        var header = new WorkbookRow(33, RetailSalesProfiles.R025Headers.Select(text => new WorkbookCell(text, text)).ToArray());
        var sheet = new WorkbookSheet("Deep", 1, ["Sample Report"], [.. titles, header]);
        var workbook = new WorkbookSnapshot("deep.xlsx", 1, new string('d', 64), [sheet]);

        var result = new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All);

        Assert.Null(result.Profile);
    }
}
