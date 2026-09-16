using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class StaffImportNameSqlTests
{
    [Fact]
    public async Task R013_import_replaces_legacy_code_names_and_preserves_manually_edited_names()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            // Migration 0019 uses the CRO code when a legacy enrichment has no staff name.
            await database.ExecuteAsync("""
                INSERT dbo.staff(store_code,staff_code,staff_name,active,modified_by) VALUES
                ('HEMW',N'2001',N'2001',1,N'Legacy upgrade'),
                ('HEMW',N'2002',N'Manually Edited Name',0,N'Staff editor');
                """);
            var persistence = new SqlServerImportPersistenceUseCase(database.ConnectionString);
            foreach (var family in new[] { "R025", "R013" })
            {
                var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), family + "_*.xlsx").Single();
                var sample = await new OpenXmlWorkbookReader().ReadAsync(path);
                var sheet = sample.Sheets[0];
                var rows = new[]
                {
                    Row(sheet, family, 2, "100000001", "2001", "Sample Staff One"),
                    Row(sheet, family, 3, "100000002", "2002", "Source Staff Two")
                };
                var workbook = sample with
                {
                    Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    Sheets = [sheet with { Rows = rows }]
                };
                var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
                var result = await persistence.PersistAsync(new(accepted, new(2026, 8, 25), "HEMW", "Staff name SQL test"));
                Assert.Equal(2, result.PersistedRows);
            }

            Assert.Equal("Sample Staff One", await database.ExecuteAsync("SELECT staff_name FROM dbo.staff WHERE store_code='HEMW' AND staff_code=N'2001'"));
            Assert.Equal("Manually Edited Name", await database.ExecuteAsync("SELECT staff_name FROM dbo.staff WHERE store_code='HEMW' AND staff_code=N'2002'"));
            Assert.Equal(false, await database.ExecuteAsync("SELECT active FROM dbo.staff WHERE store_code='HEMW' AND staff_code=N'2002'"));
            Assert.Equal("Staff editor", await database.ExecuteAsync("SELECT modified_by FROM dbo.staff WHERE store_code='HEMW' AND staff_code=N'2002'"));
            Assert.Equal("Source Staff Two", await database.ExecuteAsync("SELECT staff_name FROM dbo.sales_line_enrichments WHERE enrichment_type='R013' AND source_cro_number=N'2002'"));

            var report = await new OperationalReportRepository(database.ConnectionString)
                .LoadStaffPerformanceAsync(new ReportingQueryScope(new(2026, 8, 25), new(2026, 8, 25), ["HEMW"]));
            Assert.Equal("Sample Staff One", Assert.Single(report.Rows, row => row.CroNumber == "2001").CroName);
            Assert.Equal("Manually Edited Name", Assert.Single(report.Rows, row => row.CroNumber == "2002").CroName);
            Assert.Equal(236m, report.AttributedSales);
            Assert.Equal(0m, report.Variance);
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
