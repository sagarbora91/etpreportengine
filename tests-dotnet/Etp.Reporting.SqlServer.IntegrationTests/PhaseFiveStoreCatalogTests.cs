using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveStoreCatalogTests
{
    [Fact]
    public async Task Newly_configured_store_appears_in_dsr_and_combined_manual_totals_and_retired_masters_are_absent()
    {
        await using var db=new StoreDatabase();await db.InitializeAsync();
        var repository=new Phase2OperationsRepository(db.Fixture.ConnectionString);
        await repository.UpsertMasterValueAsync("STORE","EAST","East branch","APPROVED",true,"New shop");
        await db.Fixture.ExecuteAsync("UPDATE dbo.stores SET is_active=CASE WHEN store_code='EAST' THEN 1 ELSE 0 END;");
        var catalog=new StoreCatalogRepository(db.Fixture.ConnectionString);
        Assert.Equal(["EAST"],await catalog.ActiveCodesAsync());
        var day=new DateOnly(2026,8,25);
        await new DataTruthMasterRepository(db.Fixture.ConnectionString).SaveMonthlyTargetAsync(new("EAST",new(2026,8,1),3100m));
        var document=await new OperationalReportRepository(db.Fixture.ConnectionString).LoadDailySalesReportDocumentAsync(day);
        var store=Assert.Single(document.Stores);
        Assert.Equal("EAST",store.StoreCode);Assert.Equal("East branch",store.DisplayName);
        Assert.Equal(["EAST","COMBINED"],document.EveningSheets.Select(x=>x.StoreCode));
        Assert.Equal(3100m,document.EveningSheets.Single(x=>x.StoreCode=="COMBINED").StoreTarget);
        Assert.Equal(100m,document.EveningSheets.Single(x=>x.StoreCode=="EAST").DayTarget);
        Assert.Equal(DBNull.Value,await db.Fixture.ExecuteAsync("SELECT OBJECT_ID(N'dbo.controlled_master_values',N'U')"));
        await Assert.ThrowsAsync<ArgumentException>(()=>repository.UpsertMasterValueAsync("TENDER","UNUSED","Unused","APPROVED",true,"Not used"));
    }

    private sealed class StoreDatabase:IAsyncDisposable
    {
        public SqlDatabaseFixture Fixture {get;}=new();
        public Task InitializeAsync()=>Fixture.InitializeAsync();
        public async ValueTask DisposeAsync()=>await Fixture.DisposeAsync();
    }
}
