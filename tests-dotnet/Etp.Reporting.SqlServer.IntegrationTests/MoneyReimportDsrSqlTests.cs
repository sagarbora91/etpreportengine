using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane M1, MONEY-TEST-GAPS #12: the import tests check SUM(sales_lines) or row counts after a re-import, never the
/// DSR. Here the real importer (SqlServerImportPersistenceUseCase, as PhaseOneImportSqlTests drives it) loads R025 files
/// built from the synthetic sample workbook, and the DSR, Sales summary, Customer-wise and Management Trend are read after
/// a re-import of the same file and after an overlapping wider file. Money must not change.
/// <para>
/// The sample workbook's store is HEMW and the importer takes the store from the rows, so this class keeps its own fresh
/// test database (nothing else in it). Every row is synthetic ("SAMPLE", "Sample Customer"); no customer data.
/// Lane P6 owns the supersede / re-dating code (IF-028): it is called here only through the importer, and the NOTE flags
/// this test to P6.
/// </para>
/// </summary>
public sealed class MoneyReimportDsrSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Store = "HEMW"; // the sample workbook's store column

    [SqlFact]
    public async Task Dsr_money_is_unchanged_by_a_reimport_and_an_overlapping_window()
    {
        var sample = await new OpenXmlWorkbookReader().ReadAsync(
            Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single());
        var aug20 = new DateOnly(2026, 8, 20); var aug25 = new DateOnly(2026, 8, 25); var aug26 = new DateOnly(2026, 8, 26);
        var first = Row(sample, 2, "M1000001", aug20, 1, 1180m);
        var second = Row(sample, 3, "M1000002", aug25, 3, 354m);
        var importer = new SqlServerImportPersistenceUseCase(database.ConnectionString);

        // File A: 20 Aug INV 1,180 (1 unit) and 25 Aug INV 354 (3 units).
        Assert.Equal(2, (await SaveAsync(importer, Workbook(sample, [first, second]))).PersistedRows);
        // DSR 25 Aug: MTD VALUE 1,180 + 354 = 1,534, VOL 1 + 3 = 4, INVOICE 2; FTD 354 / 3 / 1.
        await AssertDsrAsync(aug25, "MTD", 1534m, 4m, 2);
        await AssertDsrAsync(aug25, "FTD", 354m, 3m, 1);

        // File A again under a new SHA-256 (the same export saved twice): nothing new, nothing counted twice.
        var again = await SaveAsync(importer, Workbook(sample, [first, second]));
        Assert.Equal(("Duplicate content", 0), (again.Status, again.PersistedRows));
        await AssertDsrAsync(aug25, "MTD", 1534m, 4m, 2);

        // File B, 20-26 Aug: the same two rows plus 26 Aug INV 500 (1 unit). It is a superset of A: one new row.
        var third = Row(sample, 4, "M1000003", aug26, 1, 500m);
        var wider = await SaveAsync(importer, Workbook(sample, [first, second, third]));
        Assert.Equal((1, 2), (wider.PersistedRows, wider.AlreadyPresentRows));
        Assert.Equal(3, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines")));
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE report_code='R025' AND is_superseded=0")));

        // 25 Aug is unchanged by the overlap; 26 Aug: MTD 1,534 + 500 = 2,034, VOL 5, INVOICE 3; FTD 500 / 1 / 1.
        await AssertDsrAsync(aug25, "MTD", 1534m, 4m, 2);
        await AssertDsrAsync(aug26, "MTD", 2034m, 5m, 3);
        await AssertDsrAsync(aug26, "FTD", 500m, 1m, 1);

        // The other reports over 1-26 Aug give the same store total: 2,034, 5 units, 3 invoices.
        var scope = new ReportingQueryScope(new(2026, 8, 1), aug26, [Store]);
        var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(database.ConnectionString),
            RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var summary = Assert.Single((await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Store)).Rows);
        Assert.Equal((2034m, 5m, 3), (summary.SourceSignedNetAmount, summary.SourceSignedQuantity, summary.Invoices));

        var customer = await new OperationalReportRepository(database.ConnectionString).LoadInvoiceSummaryAsync(scope);
        Assert.Equal(3, customer.Count);
        Assert.Equal(2034m, customer.Sum(x => x.NetValue));

        // Management Trend reads only lines of current files: the lines moved to file B when it replaced A.
        var trend = (await new Phase2OperationsRepository(database.ConnectionString).LoadManagementTrendAsync(new(2026, 8, 1), aug26))
            .Where(x => x.StoreCode == Store).ToArray();
        Assert.Equal((2034m, 5m, 3), (trend.Sum(x => x.NetSales), trend.Sum(x => x.Units), trend.Sum(x => x.Invoices)));
    }

    private async Task AssertDsrAsync(DateOnly day, string period, decimal value, decimal units, int invoices)
    {
        var row = (await new OperationalReportRepository(database.ConnectionString).LoadDsrAsync(day, [Store])).Single(x => x.Period == period && x.Store == Store);
        Assert.Equal(value, row.TySales);
        Assert.Equal(units, row.TyUnits);
        Assert.Equal(invoices, row.TyInvoices);
    }

    private static WorkbookRow Row(WorkbookSnapshot sample, int row, string invoice, DateOnly date, decimal quantity, decimal gross)
    {
        var sheet = sample.Sheets[0]; var cells = sheet.Rows[0].Cells.ToArray(); var headers = sheet.Headers.ToList();
        void Set(string header, object value)
        {
            var index = headers.FindIndex(x => x == header);
            if (index < 0) throw new InvalidOperationException($"The R025 sample workbook has no {header} column.");
            cells[index] = new(value);
        }
        Set("TRANS_TYPE", "INV"); Set("INVNUMBER", invoice); Set("INVDATE", date); Set("QTY", quantity); Set("NETAMOUNT", gross);
        return new(row, cells);
    }

    private static WorkbookSnapshot Workbook(WorkbookSnapshot sample, WorkbookRow[] rows) => sample with
    { Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), Sheets = [sample.Sheets[0] with { Rows = rows }] };

    private static Task<Etp.Reporting.Application.Imports.ImportPersistenceResult> SaveAsync(SqlServerImportPersistenceUseCase importer, WorkbookSnapshot workbook)
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        return importer.PersistAsync(new(accepted, accepted.Scope.PeriodEnd!.Value, accepted.Scope.StoreCode!, "M1 money re-import test"));
    }
}
