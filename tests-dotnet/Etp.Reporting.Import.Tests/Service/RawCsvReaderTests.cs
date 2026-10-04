using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>Lane L9: the CSV reader for raw ETP Service exports (synthetic text only).</summary>
public sealed class RawCsvReaderTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "EtpRawCsv-" + Guid.NewGuid().ToString("N"));

    public RawCsvReaderTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static WorkbookSheet Sheet(string text, string name = "JOB REPORT 06.10.2026 TO 09.10.2026.csv") =>
        Assert.Single(CsvWorkbookReader.Materialize(name, Encoding.UTF8.GetBytes(text)).Sheets);

    private static string?[] Values(WorkbookRow row) => row.Cells.Select(cell => cell.Value as string).ToArray();

    [Fact]
    public void Quoted_fields_keep_commas_quotes_and_line_breaks()
    {
        var sheet = Sheet("\"Job\",\"Note\",\"Amount\"\r\n\"JOAW330SYN0101\",\"Sample, with comma\",\"10.00\"\r\n" +
                          "\"JOAW330SYN0102\",\"He said \"\"ok\"\"\",\"20.00\"\r\n\"JOAW330SYN0103\",\"two\nlines\",\"30.00\"\r\n");

        Assert.Equal(["Job", "Note", "Amount"], sheet.Headers);
        Assert.Equal(1, sheet.HeaderRowNumber);
        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal(new string?[] { "JOAW330SYN0101", "Sample, with comma", "10.00" }, Values(sheet.Rows[0]));
        Assert.Equal(new string?[] { "JOAW330SYN0102", "He said \"ok\"", "20.00" }, Values(sheet.Rows[1]));
        Assert.Equal(new string?[] { "JOAW330SYN0103", "two\nlines", "30.00" }, Values(sheet.Rows[2]));
        Assert.Equal(new[] { 2, 3, 4 }, sheet.Rows.Select(row => row.RowNumber));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void Every_line_ending_ends_a_record_and_an_unterminated_last_line_still_counts(string newline)
    {
        var sheet = Sheet($"A,B{newline}1,2{newline}3,4");

        Assert.Equal(["A", "B"], sheet.Headers);
        Assert.Equal(new[] { new string?[] { "1", "2" }, new string?[] { "3", "4" } }, sheet.Rows.Select(Values));
    }

    [Fact]
    public void Unquoted_fields_and_empty_cells_read_as_a_workbook_does()
    {
        var sheet = Sheet("A,B,C\n1,,3\n,,\n\n4,5,6\n");

        // An empty cell is a missing value, as an absent XLSX cell is; an empty record is no data row.
        Assert.Equal(new[] { new WorkbookCell("1", "1"), new WorkbookCell(null), new WorkbookCell("3", "3") }, sheet.Rows[0].Cells);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(5, sheet.Rows[1].RowNumber);
    }

    [Fact]
    public void Empty_trailing_columns_are_trimmed_from_the_header_and_every_row()
    {
        var sheet = Sheet("\"A\",\"B\",\"\",\"\"\n\"1\",\"2\",\"\",\"\"\n\"3\",\"\",\"\",\"\"\n");

        Assert.Equal(["A", "B"], sheet.Headers);
        Assert.Equal(2, sheet.Rows[0].Cells.Count);
        Assert.Single(sheet.Rows[1].Cells);
    }

    [Fact]
    public void Header_is_the_first_record_with_a_value_and_header_text_is_trimmed()
    {
        var sheet = Sheet("\n,,\n\" Job \",\"Amount \"\n\"JOAW330SYN0101\",\"1\"\n");

        Assert.Equal(3, sheet.HeaderRowNumber);
        Assert.Equal(["Job", "Amount"], sheet.Headers);
        Assert.Equal(4, Assert.Single(sheet.Rows).RowNumber);
    }

    [Fact]
    public void Utf8_with_and_without_a_bom_give_the_same_text()
    {
        const string text = "\"Brand\",\"Note\"\r\n\"Sample\",\"Café – €10\"\r\n";
        var plain = CsvWorkbookReader.Materialize("a.csv", Encoding.UTF8.GetBytes(text));
        var bom = CsvWorkbookReader.Materialize("a.csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]);

        Assert.Equal(["Brand", "Note"], bom.Sheets[0].Headers);
        Assert.Equal(Values(plain.Sheets[0].Rows[0]), Values(bom.Sheets[0].Rows[0]));
        Assert.Equal("Café – €10", Values(bom.Sheets[0].Rows[0])[1]);
        Assert.NotEqual(plain.Sha256, bom.Sha256);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_are_read_as_windows_1252()
    {
        // "Café, Main – €5" in Windows-1252: é = E9, – = 96, € = 80. None is valid UTF-8 on its own.
        var bytes = Encoding.ASCII.GetBytes("\"Name\",\"Amount\"\r\n\"Caf\u0001, Main \u0002 \u00035\",\"5.00\"\r\n");
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = bytes[index] switch { 1 => 0xE9, 2 => 0x96, 3 => 0x80, var other => other };

        var sheet = Assert.Single(CsvWorkbookReader.Materialize("legacy.csv", bytes).Sheets);

        Assert.Equal(new string?[] { "Café, Main – €5", "5.00" }, Values(Assert.Single(sheet.Rows)));
    }

    [Fact]
    public void Utf16_with_a_bom_is_read()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("A,B\r\né,2\r\n")).ToArray();

        Assert.Equal(new string?[] { "é", "2" }, Values(Assert.Single(CsvWorkbookReader.Materialize("u16.csv", bytes).Sheets[0].Rows)));
    }

    [Fact]
    public void A_text_that_ends_inside_quotes_is_refused_without_its_content()
    {
        var error = Assert.Throws<InvalidDataException>(() => Sheet("\"A\",\"B\"\n\"Sample Customer 01\",\"unterminated\n"));

        Assert.DoesNotContain("Sample", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unterminated\n", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_has_one_empty_sheet()
    {
        var sheet = Assert.Single(CsvWorkbookReader.Materialize("empty.csv", []).Sheets);

        Assert.Empty(sheet.Headers);
        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public async Task ReadAsync_names_the_sheet_after_the_file_and_keeps_the_bytes_as_evidence()
    {
        var path = Path.Combine(folder, "R R RWR 30.09.2026 TO 03.10.2026 .csv");
        var bytes = Encoding.UTF8.GetBytes("\"JobOrderNumber\",\"SpareValue\"\n\"JOAW330SYN0101\",\"10.00\"\n");
        await File.WriteAllBytesAsync(path, bytes);

        var snapshot = await new CsvWorkbookReader().ReadAsync(path);

        Assert.Equal("R R RWR 30.09.2026 TO 03.10.2026 .csv", snapshot.FileName);
        Assert.Equal("R R RWR 30.09.2026 TO 03.10.2026", Assert.Single(snapshot.Sheets).Name);
        Assert.Equal(Path.GetFullPath(path), snapshot.SourcePath);
        Assert.Equal(bytes.Length, snapshot.FileSizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), snapshot.Sha256);
        Assert.Equal(bytes, snapshot.EvidenceBytes.ToArray());
    }

    [Fact]
    public async Task ReadAsync_reads_a_file_another_program_holds_open_for_writing()
    {
        var path = Path.Combine(folder, "PENDING REPORT 09.10.2026.csv");
        await File.WriteAllTextAsync(path, "A,B\n1,2\n");
        await using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var snapshot = await new CsvWorkbookReader().ReadAsync(path);

        Assert.Single(snapshot.Sheets[0].Rows);
    }

    [Theory]
    [InlineData("JOB REPORT 06.10.2026 TO 09.10.2026.csv", true)]
    [InlineData("JOB REPORT.CSV", true)]
    [InlineData("R025_SDB_VariantwiseSales.xlsx", false)]
    [InlineData("pack.zip", false)]
    public void Source_reader_chooses_by_extension(string name, bool csv)
    {
        Assert.Equal(csv, SourceFileReader.IsCsv(name));
    }

    [Fact]
    public async Task Source_reader_reads_csv_with_the_csv_reader_and_xlsx_with_the_workbook_reader()
    {
        var csvPath = Path.Combine(folder, "a.csv");
        await File.WriteAllTextAsync(csvPath, "A,B\n1,2\n");
        var xlsxPath = Path.Combine(folder, "a.xlsx");
        AuditFixtureWorkbooks.WriteWorkbook(xlsxPath, [("Data", [["A", "B"], [1m, 2m]])]);
        var reader = new SourceFileReader();

        Assert.Equal("a", Assert.Single((await reader.ReadAsync(csvPath)).Sheets).Name);
        Assert.Equal("Data", Assert.Single((await reader.ReadAsync(xlsxPath)).Sheets).Name);
    }

    [Fact]
    public async Task A_csv_stages_the_same_values_as_the_equivalent_xlsx()
    {
        // R025 is the shape here because it is catalogued on every branch; the S families stage through the same code.
        var headers = RetailSalesProfiles.R025Headers;
        var profile = ApprovedImportProfileRegistry.All.Single(candidate => candidate.ReportCode == "R025");
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = 20260825m, ["QTY"] = 1m, ["NETAMOUNT"] = 118.5m, ["NETVALUE"] = 100.42m, ["TAX"] = 18.08m
        };
        var xlsxPath = Path.Combine(folder, "R025_sales.xlsx");
        AuditFixtureWorkbooks.WriteWorkbook(xlsxPath,
            [("SDB VariantwiseSales", [headers.Cast<object?>().ToArray(), headers.Select(header => values.GetValueOrDefault(header)).ToArray()])]);
        var csvPath = Path.Combine(folder, "R025_sales.csv");
        await File.WriteAllTextAsync(csvPath, string.Join("\r\n",
            string.Join(",", headers.Select(Quote)),
            string.Join(",", headers.Select(header => Quote(values.GetValueOrDefault(header) is decimal number
                ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : values.GetValueOrDefault(header) as string ?? "")))) + "\r\n");

        var stager = new ImportRowStager();
        var fromXlsx = stager.Stage((await new OpenXmlWorkbookReader().ReadAsync(xlsxPath)).Sheets[0], profile);
        var fromCsv = stager.Stage((await new CsvWorkbookReader().ReadAsync(csvPath)).Sheets[0], profile);

        Assert.True(fromCsv.CanPersist, string.Join(" ", fromCsv.Diagnostics.Select(diagnostic => diagnostic.Code)));
        var expected = Assert.Single(fromXlsx.Rows).Values;
        var actual = Assert.Single(fromCsv.Rows).Values;
        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), actual.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [Fact]
    public async Task The_windows_1252_fixture_keeps_its_quoted_comma_and_accent()
    {
        var path = Path.Combine(RawServiceFixtures.Folder, "RENVENUE REPORT 06.10.2026 TO 09.10.2026.csv");

        var sheet = Assert.Single((await new SourceFileReader().ReadAsync(path)).Sheets);

        var scName = sheet.Headers.ToList().IndexOf("SC Name");
        Assert.True(scName >= 0, "the S003 raw header has SC Name");
        Assert.Equal("Sample Café, Main Road", sheet.Rows[0].Cells[scName].Value);
        Assert.Equal(4, sheet.Rows.Count);
    }
}

/// <summary>The synthetic raw Service pack under tests-dotnet/fixtures/service-interim/raw (scripts/service-centre/generate_raw_fixtures.py).</summary>
internal static class RawServiceFixtures
{
    public static string Folder { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "service-interim", "raw");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests-dotnet/fixtures/service-interim/raw was not found above the test output folder.");
    }

    public static string Path(string fileName) => System.IO.Path.Combine(Folder, fileName);
}
