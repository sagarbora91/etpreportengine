using System.Text.RegularExpressions;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.TestSupport.Service;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// Service interim fixtures are synthetic (rule 11). Every cell of every fixture workbook is scanned: a run of ten or
/// more digits (a phone, account or loyalty number) outside the allowed synthetic job and bill patterns fails, and so
/// does an e-mail outside example.invalid. The fixture folders and row counts must match the generated expectations.
/// </summary>
public sealed partial class FixturePrivacyTests
{
    // Synthetic job and bill numbers; neither has a long digit run today, but they are the only allowed exceptions.
    [GeneratedRegex(@"\b(?:JOAW330SYN|BIAW330SYN)\d{4}\b")]
    private static partial Regex AllowedPattern();

    [GeneratedRegex(@"\d{10,}")]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+)")]
    private static partial Regex Email();

    private static IEnumerable<string> FixtureFiles() =>
        Directory.GetFiles(ServiceFixtures.Root, "*.xlsx", SearchOption.AllDirectories).Order(StringComparer.Ordinal);

    [Fact]
    public async Task No_fixture_cell_holds_a_long_digit_run_or_a_real_e_mail()
    {
        var files = FixtureFiles().ToArray();
        Assert.NotEmpty(files);
        var reader = new OpenXmlWorkbookReader();
        var problems = new List<string>();
        foreach (var file in files)
        {
            var snapshot = await reader.ReadAsync(file);
            foreach (var sheet in snapshot.Sheets)
            {
                var texts = sheet.Headers.Select(header => (Row: sheet.HeaderRowNumber, Text: (string?)header))
                    .Concat(sheet.Rows.SelectMany(row => row.Cells.Select(cell => (Row: row.RowNumber, Text: CellText(cell)))));
                foreach (var (row, text) in texts)
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    var stripped = AllowedPattern().Replace(text, string.Empty);
                    // Report where, never the value itself.
                    if (DigitRun().IsMatch(stripped))
                        problems.Add($"{Path.GetRelativePath(ServiceFixtures.Root, file)} {sheet.Name} row {row}: a run of 10+ digits");
                    foreach (Match email in Email().Matches(text))
                        if (!email.Groups[1].Value.Equals("example.invalid", StringComparison.OrdinalIgnoreCase))
                            problems.Add($"{Path.GetRelativePath(ServiceFixtures.Root, file)} {sheet.Name} row {row}: an e-mail outside example.invalid");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_fixture_folders_hold_the_files_the_expectations_name()
    {
        foreach (var (folder, rows) in new[] { (ServiceFixtures.Week1Folder, ServiceFixtures.Week1Rows), (ServiceFixtures.Week2Folder, ServiceFixtures.Week2Rows) })
        {
            var names = Directory.GetFiles(folder, "*.xlsx").Select(Path.GetFileName).ToArray();
            Assert.Equal(41, names.Length);
            Assert.Contains(ServiceFixtures.ControlFileName, names);
            Assert.Equal([.. Enumerable.Range(1, 40).Select(n => $"S{n:000}")], rows.Keys.Order(StringComparer.Ordinal));
            Assert.All(rows.Keys, code => Assert.Single(names, name => name!.StartsWith(code + "_", StringComparison.Ordinal)));
            Assert.Equal(0, rows["S038"]);
            Assert.All(rows, pair => Assert.True(pair.Key == "S038" || pair.Value is >= 3 and <= 12, $"{pair.Key} has {pair.Value} rows."));
        }

        Assert.Equal(ServiceInterimFamilies.Importable.Order(StringComparer.Ordinal), ServiceFixtures.ExpectedImported);
        Assert.Equal(6, ServiceFixtures.ExpectedNotNeeded.Count);
        Assert.Equal(["S002_JobReportBooking.xlsx", "S009_PendingRepair.xlsx"],
            Directory.GetFiles(ServiceFixtures.UndatedFolder, "*.xlsx").Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["S009_PendingRepair.xlsx"], Directory.GetFiles(ServiceFixtures.SameDateChangedFolder, "*.xlsx").Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("S009", 5, 6)]
    [InlineData("S015", 4, 3)]
    [InlineData("S017", 3, 4)]
    [InlineData("S018", 5, 7)]
    [InlineData("S003", 4, 5)]
    [InlineData("S004", 9, 12)]
    public async Task The_week_files_have_the_expected_data_rows(string code, int week1, int week2)
    {
        Assert.Equal(week1, ServiceFixtures.Week1Rows[code]);
        Assert.Equal(week2, ServiceFixtures.Week2Rows[code]);
        var reader = new OpenXmlWorkbookReader();
        var one = await reader.ReadAsync(ServiceFixtures.FileOf(ServiceFixtures.Week1Folder, code));
        var two = await reader.ReadAsync(ServiceFixtures.FileOf(ServiceFixtures.Week2Folder, code));
        Assert.Equal(week1, one.Sheets.Single(sheet => sheet.Name == "Data").Rows.Count);
        Assert.Equal(week2, two.Sheets.Single(sheet => sheet.Name == "Data").Rows.Count);
    }

    private static string? CellText(WorkbookCell cell) => cell.DisplayText ?? Convert.ToString(cell.Value, System.Globalization.CultureInfo.InvariantCulture);
}
