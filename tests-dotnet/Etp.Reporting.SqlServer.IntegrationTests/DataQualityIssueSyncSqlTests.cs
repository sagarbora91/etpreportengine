using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// R-WLMHW-10 (report audit, 3 Oct 2026): Open items read dbo.data_quality_issues, which was synced only when an
// Owner or Store Manager opened the screen. After the corrected imports the grid still said 10,935 unmatched
// enrichment rows against 274 live. A committed import now syncs the saved issues itself.
public sealed class DataQualityIssueSyncSqlTests
{
    [Fact]
    public async Task A_committed_import_brings_the_saved_issues_up_to_the_live_checks_without_a_refresh()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var productisation = new ProductisationRepository(database.ConnectionString);
            // The saved row as an earlier sync left it, before the imports that changed the count.
            await productisation.SyncDataQualityIssuesAsync(
                [new("FAIL", "Staff", "UNMATCHED_ENRICHMENT", 10935, null, "R003/R013 enrichment rows could not be matched uniquely to canonical sales.")]);
            await database.ExecuteAsync("UPDATE dbo.data_quality_issues SET modified_utc=DATEADD(day,-2,SYSUTCDATETIME())");
            var before = await productisation.LoadDataQualityIssuesSyncedUtcAsync();

            var persistence = new SqlServerImportPersistenceUseCase(database.ConnectionString);
            foreach (var family in new[] { "R025", "R013" })
            {
                var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), family + "_*.xlsx").Single();
                var sample = await new OpenXmlWorkbookReader().ReadAsync(path);
                var sheet = sample.Sheets[0];
                var workbook = sample with
                {
                    Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    Sheets = [sheet with { Rows = [Row(sheet, family, 2, "100000001", "2001", "Sample Staff One")] }]
                };
                var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
                var result = await persistence.PersistAsync(new(accepted, new(2026, 8, 25), "HEMW", "Data-quality sync SQL test"));
                Assert.Equal("Imported", result.Status);
            }

            // No dashboard load and no refresh: the grid's saved row must already match the live summary.
            var live = await new Phase2OperationsRepository(database.ConnectionString).LoadDataQualitySummaryAsync();
            var unmatched = live.SingleOrDefault(row => row.Code == "UNMATCHED_ENRICHMENT")?.Count ?? 0;
            Assert.NotEqual(10935, unmatched);
            var summary = (string)(await database.ExecuteAsync(
                "SELECT safe_summary FROM dbo.data_quality_issues WHERE issue_key=N'COMPUTED/Staff/UNMATCHED_ENRICHMENT'"))!;
            Assert.EndsWith($"({unmatched} current)", summary, StringComparison.Ordinal);
            Assert.Equal(unmatched == 0 ? "PASS" : "FAIL", await database.ExecuteAsync(
                "SELECT technical_control_status FROM dbo.data_quality_issues WHERE issue_key=N'COMPUTED/Staff/UNMATCHED_ENRICHMENT'"));
            foreach (var finding in live)
                Assert.EndsWith($"({finding.Count} current)", (string)(await database.ExecuteAsync(
                    $"SELECT safe_summary FROM dbo.data_quality_issues WHERE issue_key=N'COMPUTED/{finding.Area}/{finding.Code}'"))!, StringComparison.Ordinal);
            var after = await productisation.LoadDataQualityIssuesSyncedUtcAsync();
            Assert.True(after > before);
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    // Review of FIX-10: on a clean database every check passes, so a sync wrote no row and the as-of line
    // said "never" after it. The sync now stamps a marker row, which the grid does not show.
    [Fact]
    public async Task A_sync_that_finds_nothing_still_records_when_it_ran()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var productisation = new ProductisationRepository(database.ConnectionString);
            Assert.Null(await productisation.LoadDataQualityIssuesSyncedUtcAsync());

            await productisation.SyncDataQualityIssuesAsync([]);
            var first = await productisation.LoadDataQualityIssuesSyncedUtcAsync();
            Assert.NotNull(first);
            Assert.DoesNotContain(await productisation.LoadDataQualityIssuesAsync(), row => row.Category == "LIVE_CHECKS_SYNC");

            await database.ExecuteAsync("UPDATE dbo.data_quality_issues SET modified_utc=DATEADD(day,-2,modified_utc)");
            await productisation.SyncDataQualityIssuesAsync([]);
            Assert.True(await productisation.LoadDataQualityIssuesSyncedUtcAsync() > first!.Value.AddDays(-1));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    private static WorkbookRow Row(WorkbookSheet sheet, string family, int rowNumber, string invoice, string staffCode, string staffName)
    {
        var columns = EtpReportFamilyRegistry.Resolve(family).Columns;
        var cells = sheet.Rows[0].Cells.ToArray();
        void Set(string field, object value)
        {
            var column = columns.SingleOrDefault(column => column.CanonicalField == field);
            if (column is not null) cells[sheet.Headers.ToList().IndexOf(column.SourceHeader)] = new WorkbookCell(value);
        }
        Set("invoice_number", invoice);
        Set("product_code", "SAMPLE-ITEM");
        Set("transaction_date", new DateOnly(2026, 8, 25));
        Set("cro_number", staffCode);
        Set("cro_name", staffName);
        return new WorkbookRow(rowNumber, cells);
    }
}
