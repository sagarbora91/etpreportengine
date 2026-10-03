using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Service interim lane L6 (decision 15, 3 Oct 2026; design section 6, review M5). Migration 0048
/// section A seeds the Service Centre store AW330 inactive under the SERVICE business unit, with
/// trigger trg_stores_service_unit_inactive (51900) refusing to make it active. With AW330 present,
/// and with Service imports recorded under AW330, every Retail store list stays WLMHW and HEMW.
/// All rows here are synthetic.
/// </summary>
public sealed class RetailIsolationSqlTests
{
    private static readonly string[] RetailStores = ["WLMHW", "HEMW"];
    private static readonly DateOnly Day = new(2026, 9, 28);

    [Fact]
    public async Task Service_store_is_seeded_inactive_under_the_service_unit_and_is_never_an_active_code()
    {
        await using var db = new IsolationDatabase(); await db.InitializeAsync();
        var catalog = new StoreCatalogRepository(db.Fixture.ConnectionString);

        var service = Assert.Single(await catalog.LoadAsync(), store => store.Code == "AW330");
        Assert.False(service.IsActive);
        Assert.True(service.IsServiceCentre);
        Assert.Equal("SERVICE", service.BusinessUnitCode);
        Assert.Equal(RetailStores, await catalog.ActiveCodesAsync());
        Assert.All((await catalog.LoadAsync()).Where(store => store.Code != "AW330"), store => Assert.Null(store.BusinessUnitCode));

        var master = Assert.Single(await new Phase2OperationsRepository(db.Fixture.ConnectionString).LoadMasterValuesAsync("STORE"), row => row.Code == "AW330");
        Assert.Equal("SERVICE", master.BusinessUnitCode);
        Assert.False(master.IsActive);
    }

    [Fact]
    public async Task Latest_combined_date_ignores_the_service_store_and_its_imports()
    {
        await using var db = new IsolationDatabase(); await db.InitializeAsync();
        var repository = new Phase2OperationsRepository(db.Fixture.ConnectionString);
        foreach (var store in RetailStores) await db.AddFileAsync("R025", store, Day);
        Assert.Equal(Day, await repository.LoadLatestCombinedBusinessDateAsync(CancellationToken.None));

        // Service files for the same day and for a later day change nothing: AW330 is not an
        // active store, so it is neither required nor counted.
        await db.AddFileAsync("S009", "AW330", Day);
        await db.AddFileAsync("S002", "AW330", Day.AddDays(1));
        await db.AddFileAsync("S004", "AW330", Day.AddDays(2));
        Assert.Equal(Day, await repository.LoadLatestCombinedBusinessDateAsync(CancellationToken.None));

        // A later day still needs every Retail store, and only those.
        await db.AddFileAsync("R025", "WLMHW", Day.AddDays(1));
        Assert.Equal(Day, await repository.LoadLatestCombinedBusinessDateAsync(CancellationToken.None));
        await db.AddFileAsync("R025", "HEMW", Day.AddDays(1));
        Assert.Equal(Day.AddDays(1), await repository.LoadLatestCombinedBusinessDateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Combined_pack_dsr_evening_and_service_sales_lists_hold_the_retail_stores_only()
    {
        await using var db = new IsolationDatabase(); await db.InitializeAsync();
        await db.AddFileAsync("S009", "AW330", Day);
        await db.AddFileAsync("S004", "AW330", Day);
        var reports = new OperationalReportRepository(db.Fixture.ConnectionString);

        var dsr = await reports.LoadDsrAsync(Day);
        Assert.Equal(["WLMHW", "HEMW", "COMBINED"], dsr.Select(row => row.Store).Distinct());

        var document = await reports.LoadDailySalesReportDocumentAsync(Day);
        Assert.Equal(RetailStores, document.Stores.Select(store => store.StoreCode));
        Assert.Equal(["WLMHW", "HEMW", "COMBINED"], document.EveningSheets.Select(sheet => sheet.StoreCode));

        // The service-sales "all stores" list (manual SERVICE_CASH/CARD/UPI, unchanged by the interim).
        var serviceSales = await reports.LoadServiceSalesAsync(Day);
        Assert.Equal(RetailStores, serviceSales.Select(row => row.StoreCode).Distinct());

        var pack = await new DailyReportingPackService(db.Fixture.ConnectionString).GenerateCombinedAsync(Day, "Synthetic isolation test");
        Assert.EndsWith("Titan World + Helios", pack.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("AW330", pack.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("Service Centre", pack.Title, StringComparison.Ordinal);
        Assert.All(pack.Tables, table => Assert.DoesNotContain("AW330", table.Name, StringComparison.Ordinal));
        var controls = pack.Tables.Single(table => table.Name == "Combined Control Summary");
        Assert.All(controls.Data.Rows, row => Assert.Contains((string)row[0]!, RetailStores));
        Assert.Equal(RetailStores, controls.Data.Rows.Select(row => (string)row[0]!).Distinct());
    }

    [Fact]
    public async Task Activating_the_service_store_is_refused_with_51900_and_its_unit_is_kept()
    {
        await using var db = new IsolationDatabase(); await db.InitializeAsync();
        var repository = new Phase2OperationsRepository(db.Fixture.ConnectionString);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.UpsertMasterValueAsync("STORE", "AW330", "Service Centre AW330", "APPROVED", true, "Synthetic activation attempt"));
        Assert.Equal(ServiceCentreStores.ActivationRefusedMessage, refused.Message);
        Assert.Equal(51900, Assert.IsType<SqlException>(refused.InnerException).Number);
        Assert.Equal(0, Convert.ToInt32(await db.Fixture.ExecuteAsync("SELECT CONVERT(int,is_active) FROM dbo.stores WHERE store_code='AW330'")));

        // Direct SQL is refused too, for one row, for every row, and for a new Service store.
        Assert.Equal(51900, (await Assert.ThrowsAsync<SqlException>(() =>
            db.Fixture.ExecuteAsync("UPDATE dbo.stores SET is_active=1 WHERE store_code='AW330';"))).Number);
        Assert.Equal(51900, (await Assert.ThrowsAsync<SqlException>(() =>
            db.Fixture.ExecuteAsync("UPDATE dbo.stores SET is_active=1;"))).Number);
        Assert.Equal(51900, (await Assert.ThrowsAsync<SqlException>(() => db.Fixture.ExecuteAsync("""
            INSERT dbo.stores(store_code,store_name,is_active,business_unit_id)
            SELECT 'SVCSYN',N'Synthetic service store',1,business_unit_id FROM dbo.business_units WHERE business_unit_code='SERVICE';
            """))).Number);
        Assert.Equal(RetailStores, await new StoreCatalogRepository(db.Fixture.ConnectionString).ActiveCodesAsync());

        // An inactive save (a rename) is allowed and never clears the business unit.
        await repository.UpsertMasterValueAsync("STORE", "AW330", "Service Centre AW330 renamed", "APPROVED", false, "Synthetic rename");
        Assert.Equal("SERVICE", await db.Fixture.ExecuteAsync("""
            SELECT u.business_unit_code FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id WHERE s.store_code='AW330';
            """));
        Assert.Equal(0, Convert.ToInt32(await db.Fixture.ExecuteAsync("SELECT CONVERT(int,is_active) FROM dbo.stores WHERE store_code='AW330'")));

        // Retail shops are untouched by the trigger: one can still be switched off and on.
        await repository.UpsertMasterValueAsync("STORE", "HEMW", "Helios", "APPROVED", false, "Synthetic retail toggle");
        await repository.UpsertMasterValueAsync("STORE", "HEMW", "Helios", "APPROVED", true, "Synthetic retail toggle");
        Assert.Equal(RetailStores, await new StoreCatalogRepository(db.Fixture.ConnectionString).ActiveCodesAsync());
    }

    [Fact]
    public async Task Import_history_all_stores_lists_service_rows_and_a_retail_store_scope_does_not()
    {
        await using var db = new IsolationDatabase(); await db.InitializeAsync();
        await db.AddFileAsync("S009", "AW330", Day);
        await db.AddFileAsync("R025", "WLMHW", Day);
        var history = new SqlServerImportHistoryQuery(db.Fixture.ConnectionString);

        var all = await history.LoadAsync(new ImportHistoryScope(Day, Day));
        Assert.Contains(all, entry => entry.Result.StoreCode == "AW330" && entry.Result.ReportCode == "S009");
        Assert.Contains(all, entry => entry.Result.StoreCode == "WLMHW");

        var retail = await history.LoadAsync(new ImportHistoryScope(Day, Day, "WLMHW"));
        Assert.DoesNotContain(retail, entry => entry.Result.StoreCode == "AW330");
        Assert.Contains(retail, entry => entry.Result.StoreCode == "WLMHW");
    }

    private sealed class IsolationDatabase : IAsyncDisposable
    {
        public SqlDatabaseFixture Fixture { get; } = new();
        public Task InitializeAsync() => Fixture.InitializeAsync();
        public async ValueTask DisposeAsync() => await Fixture.DisposeAsync();

        // Report codes, stores and dates come from this synthetic fixture, never external input.
        public Task<object?> AddFileAsync(string report, string store, DateOnly date) => Fixture.ExecuteAsync($"""
            DECLARE @batch uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,period_start,period_end,started_utc)
            VALUES(@batch,'Completed','{date:yyyyMMdd}','{date:yyyyMMdd}',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end)
            VALUES(@batch,'synthetic-{report}-{store}-{date:yyyyMMdd}.xlsx',LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256','{report}-{store}-{date:yyyyMMdd}'),2)),1,
              '{report}','{store}','{date:yyyyMMdd}','{date:yyyyMMdd}','{date:yyyyMMdd}');
            """);
    }
}
