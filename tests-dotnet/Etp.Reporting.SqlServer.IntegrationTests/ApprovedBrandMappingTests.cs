using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class ApprovedBrandMappingTests(ITestOutputHelper output)
{
    private static readonly DateOnly Day = new(2026, 8, 25);
    private static DirectoryMigrationSource Migrations => new(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_brand_golden_survives_fresh_install_upgrade_and_reconnection(bool upgrade)
    {
        await using var db = new BrandDatabase();
        await new SqlServerDatabaseBootstrapper(db.ConnectionString,
            upgrade ? new BeforeApprovedBrands(Migrations) : Migrations).BootstrapAsync();
        await db.ExecuteAsync("""
            INSERT dbo.brand_rows(store_code,row_label,sort_order) VALUES('SYNTHETIC_OTHER_STORE',N'Keep me',1);
            INSERT dbo.brand_row_codes(store_code,source_brand,brand_row_id)
              SELECT store_code,N'KEEP',brand_row_id FROM dbo.brand_rows WHERE store_code='SYNTHETIC_OTHER_STORE';
            """);
        using var fixture = LoadGolden();
        await ImportGolden(db.ConnectionString, fixture.RootElement);
        if (upgrade)
        {
            var provisional = await new OperationalReportRepository(db.ConnectionString).LoadDailySalesReportDocumentAsync(Day);
            var helios = provisional.EveningSheets.Single(x => x.StoreCode == "HEMW");
            Assert.Equal(0m, helios.Rows.Single(x => x.Metric == "SEIKO").Ftd);
            Assert.Equal(helios.Rows.Single(x => x.Metric == "VALUE").Ftd,
                helios.Rows.Single(x => x.Metric == "Other / unmapped").Ftd);
        }
        var factsBefore = await db.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines");
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var historyBefore = await store.GetAppliedAsync();
        var applied = await new MigrationRunner(Migrations, store).RunAsync();
        if (upgrade) Assert.Contains("0027_approved_evening_brand_rows", applied);
        else Assert.Empty(applied);
        var historyAfter = await store.GetAppliedAsync();
        foreach (var migration in historyBefore)
            Assert.Equal(migration, Assert.Single(historyAfter, x => x.Id == migration.Id));
        Assert.Equal(factsBefore, await db.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines"));

        // A fresh repository and physical connection load persisted mappings, not an editor's
        // in-session state. No UI history or full application restart is claimed by this test.
        var reports = new OperationalReportRepository(db.ConnectionString);
        var document = await reports.LoadDailySalesReportDocumentAsync(Day);
        AssertGolden(document, fixture.RootElement.GetProperty("expected"));
        var retained = Assert.Single(await new EveningMasterRepository(db.ConnectionString).LoadBrandsAsync(),
            x => x.StoreCode == "SYNTHETIC_OTHER_STORE");
        Assert.Equal("Keep me", retained.Label);
        Assert.Equal("KEEP", retained.SourceCodes);
        Assert.Empty(await new MigrationRunner(Migrations, store).RunAsync());
    }

    [Fact]
    public async Task Approved_brand_excel_contains_the_same_rows_amounts_and_date_scope_as_the_Dsr()
    {
        await using var db = new BrandDatabase();
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, Migrations).BootstrapAsync();
        using var fixture = LoadGolden();
        await ImportGolden(db.ConnectionString, fixture.RootElement);
        var document = await new OperationalReportRepository(db.ConnectionString).LoadDailySalesReportDocumentAsync(Day);
        var path = Path.Combine(Path.GetTempPath(), $"EtpPhase0Test_ApprovedBrands_{Guid.NewGuid():N}.xlsx");
        const string scope = "Applied scope: Titan World + Helios; report date 25 Aug 2026";
        try
        {
            new OpenXmlReportExporter().Export(path,
                new("Daily Sales Report", Day, Day, "Passed", RetailReportingPolicy.Version, scope, DateTimeOffset.UtcNow),
                EveningReportTables.Dsr(document.EveningSheets));
            using var workbook = SpreadsheetDocument.Open(path, false);
            var rows = workbook.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToArray();
            Assert.Contains(rows, row => row.InnerText.Contains(scope, StringComparison.Ordinal));
            foreach (var sheet in document.EveningSheets.Where(x => x.StoreCode != "COMBINED"))
            {
                var expected = fixture.RootElement.GetProperty("expected").GetProperty(sheet.StoreCode);
                foreach (var value in expected.EnumerateObject())
                {
                    var cells = Assert.Single(rows, row =>
                    {
                        var content = row.Elements<Cell>().ToArray();
                        return content.Length > 2 && content[0].InnerText == sheet.StoreName && content[1].InnerText == value.Name;
                    }).Elements<Cell>().ToArray();
                    Assert.Equal(CellValues.Number, cells[2].DataType!.Value);
                    Assert.Equal(value.Value.GetDecimal(), decimal.Parse(cells[2].CellValue!.Text, CultureInfo.InvariantCulture));
                }
                var totalCells = Assert.Single(rows, row =>
                {
                    var content = row.Elements<Cell>().ToArray();
                    return content.Length > 2 && content[0].InnerText == sheet.StoreName && content[1].InnerText == "VALUE";
                }).Elements<Cell>().ToArray();
                Assert.Equal(expected.EnumerateObject().Sum(x => x.Value.GetDecimal()),
                    decimal.Parse(totalCells[2].CellValue!.Text, CultureInfo.InvariantCulture));
            }
        }
        finally { File.Delete(path); }
    }

    [PrivatePhaseOneCorpus]
    public async Task Approved_brands_match_real_July_to_August_source_totals_without_changing_facts()
    {
        await using var db = new BrandDatabase();
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, Migrations).BootstrapAsync();
        var folders = new[]
        {
            Path.Combine(PrivatePhaseOneCorpusAttribute.Root, "WLMHW", "TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026"),
            Path.Combine(PrivatePhaseOneCorpusAttribute.Root, "HEMW", "HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026")
        };
        // R025 is the authoritative DSR sales source; unrelated stock/tender/register
        // files do not affect this mapping and are covered by their existing goldens.
        var files = folders.Select(x => Directory.GetFiles(x, "*SDB-VariantwiseSales*.xlsx").Single()).ToArray();
        var imported = await new FolderImportService(new SqlServerImportPersistenceUseCase(db.ConnectionString))
            .RunFilesAsync(files, new("D4 disposable acceptance"));
        Assert.All(imported.Files, file => Assert.Contains(file.Status, new[] { "Imported", "empty export" }));
        var factsBefore = await db.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines");
        var document = await new OperationalReportRepository(db.ConnectionString).LoadDailySalesReportDocumentAsync(Day);
        // Independently summed from the raw workbook XML with decimal arithmetic,
        // counting each repeated column block once. The audit's display rounded four
        // rows to whole rupees; retain the source paise rather than alter any facts.
        var expected = new Dictionary<string, Dictionary<string, decimal>>
        {
            ["WLMHW"] = new() { ["TITAN"] = 1017619, ["Raga"] = 435267.25m, ["EDGE"] = 271585.50m,
                ["SONATA"] = 131058.50m, ["Fastrack"] = 141445, ["XYLYS"] = 10005 },
            ["HEMW"] = new() { ["SEIKO"] = 563500, ["FOSSIL"] = 445746, ["TOMMY HILFIGER"] = 181471,
                ["CERUTI"] = 98549, ["KENNETH COLE"] = 96788.10m, ["CITIZEN"] = 86700, ["POLICE"] = 62997, ["ANNE KLEIN"] = 53988 }
        };
        var expectedTotals = new Dictionary<string, (decimal Other, decimal Total)>
        {
            ["WLMHW"] = (136473.50m, 2143453.75m), ["HEMW"] = (46775m, 1636514.10m)
        };
        foreach (var (store, values) in expected)
        {
            var sheet = document.EveningSheets.Single(x => x.StoreCode == store);
            foreach (var (label, value) in values)
            {
                // The only loaded source period is 1 July–25 August, so YTD is exactly that corpus.
                Assert.Equal(value, sheet.Rows.Single(x => x.Metric == label).Ytd);
                Assert.True(value > 0);
                output.WriteLine($"{store} {label}: {value:0.00}");
            }
            var other = sheet.Rows.Single(x => x.Metric == "Other / unmapped").Ytd;
            var total = sheet.Rows.Single(x => x.Metric == "VALUE").Ytd;
            Assert.Equal(expectedTotals[store].Other, other);
            Assert.Equal(expectedTotals[store].Total, total);
            Assert.Equal(total, values.Values.Sum() + other);
            Assert.Equal(total, Convert.ToDecimal(await db.ExecuteAsync($"SELECT SUM(l.source_gross_amount) FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id WHERE i.store_code='{store}' AND i.transaction_date BETWEEN '20260701' AND '20260825' AND l.source_transaction_type IN('INV','SR','BC')")));
            output.WriteLine($"{store}: mapped {values.Values.Sum():0.00}; Other / unmapped {other:0.00}; store total {total:0.00}");
        }
        Assert.Equal(factsBefore, await db.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines"));
        output.WriteLine($"Imported {imported.Files.Count} private files into a disposable database; no private rows or exports saved to the repository.");
    }

    private static void AssertGolden(DailySalesReportDocument document, JsonElement expected)
    {
        foreach (var store in expected.EnumerateObject())
        {
            var sheet = document.EveningSheets.Single(x => x.StoreCode == store.Name);
            var brands = sheet.Rows.Where(x => x.Format == "currency" && x.Metric is not ("VALUE" or "AVPT" or "WCC SALES")).ToArray();
            Assert.Equal(store.Value.EnumerateObject().Select(x => x.Name), brands.Select(x => x.Metric));
            foreach (var row in brands)
            {
                var value = store.Value.GetProperty(row.Metric).GetDecimal();
                Assert.Equal(value, row.Ftd);
                Assert.Equal(value, row.Mtd);
                Assert.Equal(value, row.Ytd);
                Assert.True(row.Ftd > 0);
            }
            Assert.Equal(sheet.Rows.Single(x => x.Metric == "VALUE").Ftd, brands.Sum(x => x.Ftd));
        }
    }

    private static JsonDocument LoadGolden() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "evening-reports", "approved-brands.json")));

    private static async Task ImportGolden(string connectionString, JsonElement fixture)
    {
        var sample = await new OpenXmlWorkbookReader().ReadAsync(Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single());
        var sheet = sample.Sheets[0];
        foreach (var store in fixture.GetProperty("sales").EnumerateArray().GroupBy(x => x.GetProperty("store").GetString()!))
        {
            var rows = store.Select((sale, index) =>
            {
                var gross = sale.GetProperty("gross").GetDecimal();
                var replacements = new Dictionary<string, object>
                {
                    ["STORE CODE"] = store.Key, ["BRAND"] = "SYNTHETIC-CODE",
                    ["BRANDNAME"] = sale.GetProperty("brand").GetString()!, ["CLUSTER"] = sale.GetProperty("cluster").GetString()!,
                    ["TRANS_TYPE"] = sale.GetProperty("type").GetString()!, ["INVNUMBER"] = $"SYNTHETIC-{index + 1}",
                    ["ITEMNUMBER"] = $"SYNTHETIC-{index + 1}", ["INVDATE"] = Day, ["QTY"] = Math.Sign(gross),
                    ["NETAMOUNT"] = gross, ["NETVALUE"] = gross / 1.18m, ["TAX"] = gross - gross / 1.18m
                };
                return new WorkbookRow(index + 2, sheet.Rows[0].Cells.Select((cell, column) =>
                    replacements.TryGetValue(sheet.Headers[column], out var replacement) ? new WorkbookCell(replacement) : cell).ToArray());
            }).ToArray();
            var workbook = sample with { Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), Sheets = [sheet with { Rows = rows }] };
            var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
            var result = await new SqlServerImportPersistenceUseCase(connectionString)
                .PersistAsync(new(accepted, accepted.Scope.PeriodEnd!.Value, store.Key, "Synthetic approved brand golden"));
            Assert.Equal(rows.Length, result.PersistedRows);
        }
    }

    private sealed class BeforeApprovedBrands(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            (await source.DiscoverAsync(cancellationToken)).Where(x => string.CompareOrdinal(x.Id, "0027") < 0).ToArray();
    }

    private sealed class BrandDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_ApprovedBrands_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public BrandDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            return await command.ExecuteScalarAsync();
        }
        public async ValueTask DisposeAsync()
        {
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
