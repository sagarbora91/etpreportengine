using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;
using Xunit.Abstractions;

namespace Etp.Reporting.Import.Tests;

public sealed class RealCorpusFactAttribute : FactAttribute
{
    public const string Root = @"C:\Codex\Reporting Manger\ETP Source Data";
    public RealCorpusFactAttribute()
    {
        if (!Directory.Exists(Root)) Skip = "Optional private ETP corpus is absent; sanitised per-family golden fixtures still run in CI.";
    }
}

public sealed class EtpCorpusGoldenTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Families => Enumerable.Range(1, 31).Select(i => new object[] { $"R{i:000}" })
        .Append(["SOR_AGEING"]);

    [Theory]
    [MemberData(nameof(Families))]
    public async Task Every_family_has_a_sanitised_parser_golden(string familyCode)
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), familyCode + "_*.xlsx").Single();
        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
        var inspection = new MatchedImportEnvelopeFactory().Inspect(snapshot);
        Assert.True(inspection.Accepted, string.Join("; ", inspection.Diagnostics.Select(d => $"{d.Code}:{d.ColumnName}")));
        var accepted = inspection.AcceptedImport!;
        var family = EtpReportFamilyRegistry.Resolve(accepted.Profile.ReportCode);
        Assert.Equal(familyCode, family.FamilyCode);
        Assert.Equal("HEMW", accepted.Scope.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), accepted.Scope.PeriodEnd);
        var row = Assert.Single(accepted.Staging.Rows);
        Assert.Equal(snapshot.Sheets[0].Headers.Count, row.Values.Count);
        Assert.DoesNotContain(accepted.Diagnostics, diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Blocker);
        if (familyCode == "R025")
        {
            Assert.Equal(118m, row.Values["source_net_amount"]);
            Assert.Equal(100m, row.Values["source_net_value"]);
            Assert.Equal(18m, row.Values["source_tax_amount"]);
            Assert.Equal("Sample Customer", row.Values["customer_name"]);
            Assert.Equal("9XXXXXX000", row.Values["customer_phone"]);
        }
        if (familyCode == "R020")
        {
            Assert.Equal("AIRPAY", row.Values["agencyname"]);
            Assert.Equal(118m, row.Values["paymenttype25"]);
        }
        if (familyCode == "R001") Assert.Equal(19.75m, row.Values["phonepe"]);
        if (familyCode == "R008") Assert.Equal(new DateOnly(2026, 8, 27), row.Values["bankedon"]);
        if (familyCode == "R022")
        {
            var projection = new R022PersistenceProjector().Project(accepted.Staging.Rows);
            Assert.Empty(projection.QuarantinedTenders);
            Assert.Equal(118m, Assert.Single(projection.ClassifiedTenders).SourceAmount);
        }
    }

    [Fact]
    public async Task Banking_date_accepts_numeric_and_date_cells_and_keeps_unbanked_zero_empty()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R008_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var data = workbook.Sheets[0];
        var column = data.Headers.ToList().IndexOf("BANKEDON");
        var values = new object[] { 20260827m, new DateTime(2026, 8, 27), new DateTime(2026, 8, 27).ToOADate(), 0m };
        var rows = values.Select((value, index) =>
        {
            var cells = data.Rows[0].Cells.ToArray(); cells[column] = new WorkbookCell(value);
            return new WorkbookRow(index + 2, cells);
        }).ToArray();
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook with { Sheets = [data with { Rows = rows }] });
        Assert.Equal(4, accepted.Staging.Rows.Count);
        Assert.All(accepted.Staging.Rows.Take(3), row => Assert.Equal(new DateOnly(2026, 8, 27), row.Values["bankedon"]));
        Assert.Null(accepted.Staging.Rows[3].Values["bankedon"]);
    }

    [RealCorpusFact]
    public async Task Whole_private_corpus_matches_all_profiles_with_Info_and_both_raw_date_layouts()
    {
        var folders = new[]
        {
            Path.Combine(RealCorpusFactAttribute.Root, "HEMW", "till 6 sep 26"),
            Path.Combine(RealCorpusFactAttribute.Root, "HEMW", "HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026"),
            Path.Combine(RealCorpusFactAttribute.Root, "WLMHW", "TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026")
        };
        foreach (var folder in folders)
        foreach (var path in Directory.GetFiles(folder, "*.xlsx").Where(path => !Path.GetFileName(path).StartsWith("00_")))
        {
            var inspection = new MatchedImportEnvelopeFactory().Inspect(await new OpenXmlWorkbookReader().ReadAsync(path));
            var errors = string.Join("; ", inspection.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Blocker)
                .Select(d => $"{d.Code} row {d.RowNumber} column {d.ColumnName}"));
            Assert.True(inspection.Accepted, Path.GetFileName(path) + ": " + errors);
            var accepted = inspection.AcceptedImport!;
            Assert.NotNull(accepted.Scope.StoreCode);
            Assert.NotNull(accepted.Scope.PeriodEnd);
            output.WriteLine($"{Path.GetFileName(path)} {accepted.Profile.ReportCode} rows={accepted.Staging.Rows.Count} {accepted.Scope.StoreCode} {accepted.Scope.PeriodStart}..{accepted.Scope.PeriodEnd}");
            if (path.Contains("till 6 sep 26") && Path.GetFileName(path).StartsWith("R025"))
            {
                Assert.Equal(790, accepted.Staging.Rows.Count);
                Assert.Equal(new DateOnly(2024, 9, 16), accepted.Scope.PeriodStart);
                Assert.Equal(new DateOnly(2026, 9, 6), accepted.Scope.PeriodEnd);
                Assert.Equal(759, accepted.Staging.Rows.Select(row =>
                    (Date: (DateOnly)row.Values["transaction_date"]!, Invoice: (string)row.Values["invoice_number"]!)).Distinct().Count());
            }
            if (path.Contains("till 6 sep 26") && Path.GetFileName(path).StartsWith("R030"))
            {
                var stock = new StockWorkbookParser().Parse(accepted);
                Assert.False(stock.HasBlockers);
                Assert.Equal(3955, stock.Movements.Count);
            }
        }
    }

    [Theory]
    [InlineData("SR")]
    [InlineData("BC")]
    public async Task Cro_returns_and_cancellations_are_negative_and_keep_staff_name(string transactionType)
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R013_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var data = workbook.Sheets[0];
        var cells = data.Rows[0].Cells.ToArray();
        cells[0] = new WorkbookCell(transactionType);
        var changed = workbook with { Sheets = [data with { Rows = [new(2, cells)] }] };
        var row = Assert.Single(new MatchedImportEnvelopeFactory().RequireAccepted(changed).Staging.Rows);
        Assert.Equal(-118m, row.Values["source_net_amount"]);
        Assert.Equal(-100m, row.Values["source_net_value"]);
        Assert.Equal(-1m, row.Values["source_quantity"]);
        Assert.Equal("Sample Staff", row.Values["cro_name"]);
    }

    [Fact]
    public async Task Unknown_sales_type_warns_and_skips_only_that_row()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var data = workbook.Sheets[0]; var bad = data.Rows[0].Cells.ToArray(); bad[0] = new("NEW_TYPE");
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook with
            { Sheets = [data with { Rows = [data.Rows[0], new(3, bad)] }] });
        Assert.Single(accepted.Staging.Rows);
        Assert.Contains(accepted.Diagnostics, d => d.Code == "UNKNOWN_SALES_TRANSACTION_TYPE" && d.Severity == ImportDiagnosticSeverity.Warning && d.RowNumber == 3);
    }

    [Theory]
    [InlineData("R001_AdvanceOrder_Collection.xlsx", "Data", "R001")]
    [InlineData("R022_Revenue_Report.xlsx", "Data", "R022")]
    [InlineData("export.xlsx", "AdvanceOrder Collection", "R001")]
    [InlineData("export.xlsx", "Revenue Report", "R022")]
    public void Shared_revenue_headers_are_disambiguated_by_file_or_sheet(string file, string sheet, string expected)
    {
        var workbook = new WorkbookSnapshot(file, 1, new string('a', 64),
            [new(sheet, 1, RetailSalesProfiles.R022Headers, []), new("Info", 1, ["informational"], [])]);
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        Assert.Equal(expected, accepted.Profile.ReportCode);
        Assert.Empty(accepted.Staging.Rows);
        Assert.Contains(accepted.Diagnostics, d => d.Code == "EMPTY_EXPORT");
    }

    [Fact]
    public void Explicit_empty_snapshot_metadata_succeeds_but_an_arbitrary_blank_workbook_does_not()
    {
        var info = new WorkbookSheet("Info", 1, ["Consolidation"],
            [new(2, [new("Family ID"), new("R011")]), new(3, [new("Status"), new("EMPTY — no export")]),
             new(4, [new("Snapshot HEMW 07-Sep-2026")])]);
        var workbook = new WorkbookSnapshot("R011_Closing_Stock.xlsx", 1, new string('a', 64), [new("Data", 1, [], []), info]);
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        Assert.Equal("CLOSING_STOCK", accepted.Profile.ReportCode);
        Assert.Equal("HEMW", accepted.Scope.StoreCode);
        Assert.Equal(new DateOnly(2026, 9, 7), accepted.Scope.PeriodEnd);
        Assert.Empty(accepted.Staging.Rows);
        Assert.False(new MatchedImportEnvelopeFactory().Inspect(workbook with { Sheets = [workbook.Sheets[0]] }).Accepted);
    }

    [Fact]
    public async Task Mixed_store_workbook_cannot_be_attributed_to_a_single_folder_store()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R020_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var data = workbook.Sheets[0]; var cells = data.Rows[0].Cells.ToArray(); cells[3] = new("WLMHW");
        var result = new MatchedImportEnvelopeFactory().Inspect(workbook with
            { Sheets = [data with { Rows = [data.Rows[0], new(3, cells)] }] });
        Assert.False(result.Accepted);
        Assert.Contains(result.Diagnostics, d => d.Code == "WORKBOOK_MULTIPLE_STORES");
    }

    [Fact]
    public async Task Corrupt_header_names_the_closest_family_without_exposing_customer_values()
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path);
        var data = workbook.Sheets[0]; var headers = data.Headers.ToArray(); headers[34] = "WRONG COLUMN";
        var result = new MatchedImportEnvelopeFactory().Inspect(workbook with { Sheets = [data with { Headers = headers }] });
        Assert.False(result.Accepted);
        Assert.Contains(result.Diagnostics, d => d.Code == "REQUIRED_COLUMN_MISSING" && d.ColumnName == "NETVALUE" && d.Message.Contains("R025"));
        Assert.All(result.Diagnostics, d =>
        {
            Assert.DoesNotContain("Sample Customer", d.Message);
            Assert.DoesNotContain("9XXXXXX000", d.Message);
            Assert.DoesNotContain(path, d.Message);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extra_data_cells_never_disappear_when_staging_or_collapsing_a_repeated_layout(bool repeated)
    {
        var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(path); var data = workbook.Sheets[0];
        var headers = repeated ? data.Headers.Concat(data.Headers).ToArray() : data.Headers;
        var cells = repeated ? data.Rows[0].Cells.Concat(data.Rows[0].Cells).ToArray() : data.Rows[0].Cells;
        var result = new MatchedImportEnvelopeFactory().Inspect(workbook with
            { Sheets = [new("Data", 1, headers, [new(2, [..cells, new("PRIVATE EXTRA VALUE")])])] });
        Assert.False(result.Accepted);
        Assert.Contains(result.Diagnostics, d => d.Code == "ROW_EXTRA_COLUMNS" && d.RowNumber == 2);
        Assert.All(result.Diagnostics, d => Assert.DoesNotContain("PRIVATE EXTRA VALUE", d.Message));
    }

    [RealCorpusFact]
    public async Task Real_sales_match_25_August_and_all_Helios_monthly_golden_money()
    {
        var root = RealCorpusFactAttribute.Root;
        foreach (var (store, expectedGross, expectedNet) in new[] { ("WLMHW", 34215m, 28995.76m), ("HEMW", 29290m, 24822.02m) })
        {
            var file = Directory.GetFiles(Path.Combine(root, store), "*SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", SearchOption.AllDirectories)
                .Single(path => path.Contains("ALL REPORT 01 JULY"));
            var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(file));
            var day = accepted.Staging.Rows.Where(row => (DateOnly)row.Values["transaction_date"]! == new DateOnly(2026, 8, 25)).ToArray();
            Assert.Equal(expectedGross, day.Sum(row => (decimal)row.Values["source_net_amount"]!));
            Assert.Equal(expectedNet, day.Sum(row => (decimal)row.Values["source_net_value"]!));
            var month = accepted.Staging.Rows.Where(row => row.Values["transaction_date"] is DateOnly d && d.Year == 2026 && d.Month == 8).ToArray();
            Assert.Equal(store == "WLMHW" ? 938197m : 774868.60m, decimal.Round(month.Sum(row => (decimal)row.Values["source_net_amount"]!), 2));
            Assert.Equal(store == "WLMHW" ? 182 : 38, month.Select(row => row.Values["invoice_number"]).Distinct().Count());
        }
        var history = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(
            Path.Combine(root, "HEMW", "till 6 sep 26", "R025_SDB_VariantwiseSales.xlsx")));
        var golden = await File.ReadAllLinesAsync(Path.Combine(root, "HEMW", "golden-monthly-HEMW-R025.csv"));
        foreach (var expected in golden.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Split(',')))
        {
            var rows = history.Staging.Rows.Where(row => row.Values["transaction_date"] is DateOnly d && d.Year == int.Parse(expected[1]) && d.Month == int.Parse(expected[2])).ToArray();
            decimal Money(int column) => decimal.Parse(expected[column], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(int.Parse(expected[3]), rows.Select(row => row.Values["invoice_number"]).Distinct().Count());
            Assert.Equal(Money(4), rows.Sum(row => (decimal)row.Values["source_quantity"]!));
            Assert.Equal(Money(5), decimal.Round(rows.Sum(row => (decimal)row.Values["source_net_amount"]!), 2));
            Assert.Equal(Money(6), decimal.Round(rows.Sum(row => (decimal)row.Values["source_net_value"]!), 2));
            Assert.Equal(Money(7), decimal.Round(rows.Sum(row => (decimal?)row.Values["source_tax_amount"] ?? 0), 2));
        }
    }
}
