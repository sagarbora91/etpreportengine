using Etp.Reporting.Application.Accounting;
using Etp.Reporting.Infrastructure.SqlServer.Tally;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Plan task 1: the Owner's Tally company service against a real database. Stores come from the
// 0011 seed; each test uses its own short codes so the shared fixture stays independent.
public sealed class TallyProfileServiceSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task A_test_company_is_saved_with_its_stores_audited_and_loaded_back()
    {
        await database.ExecuteAsync("INSERT dbo.stores(store_code,store_name) VALUES('TPSAVE',N'Synthetic Tally store')");
        var service = new SqlServerTallyProfileService(database.ConnectionString);
        var audits = Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='ConfigurationChange' AND safe_detail=N'Tally company settings changed'"));

        var id = await service.SaveAsync(TallyProfile.NewTest("tpsave", "TEST - ETP Golden", new[] { "tpsave" }), "First test company");

        var saved = Assert.Single(await service.LoadAsync(), profile => profile.Id == id);
        Assert.Equal("TPSAVE", saved.ProfileCode);
        Assert.Equal("TEST - ETP Golden", saved.CompanyName);
        Assert.Equal("TEST", saved.Environment);
        Assert.Equal(new[] { "TPSAVE" }, saved.StoreCodes);
        Assert.Equal("Cash Sales", saved.SinglePartyLedger);
        Assert.Null(saved.ProductionEnabledUtc);
        Assert.False(string.IsNullOrWhiteSpace(saved.ModifiedBy));
        Assert.Equal("First test company", await database.ExecuteAsync($"SELECT change_reason FROM dbo.tally_profiles WHERE tally_profile_id={id}"));
        Assert.Equal(audits + 1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='ConfigurationChange' AND safe_detail=N'Tally company settings changed'")));
    }

    [Fact]
    public async Task Changing_a_company_to_live_books_never_enables_them()
    {
        await database.ExecuteAsync("INSERT dbo.stores(store_code,store_name) VALUES('TPLIVE',N'Synthetic live store')");
        var service = new SqlServerTallyProfileService(database.ConnectionString);
        var id = await service.SaveAsync(TallyProfile.NewTest("TPLIVE", "TEST - Live candidate", new[] { "TPLIVE" }), "Created as test");

        await service.SaveAsync(TallyProfile.NewTest("TPLIVE", "Saagar Live Books", new[] { "TPLIVE" }) with { Id = id, Environment = "PRODUCTION" }, "Prepared for later approval");

        var live = Assert.Single(await service.LoadAsync(), profile => profile.Id == id);
        Assert.Equal("PRODUCTION", live.Environment);
        Assert.Null(live.ProductionEnabledUtc);
        Assert.Equal("PRODUCTION", await database.ExecuteAsync($"SELECT environment FROM dbo.tally_profile_stores WHERE tally_profile_id={id}"));
    }

    [Fact]
    public async Task A_store_already_linked_to_another_test_company_is_refused_and_nothing_changes()
    {
        await database.ExecuteAsync("INSERT dbo.stores(store_code,store_name) VALUES('TPDUP',N'Synthetic shared store')");
        var service = new SqlServerTallyProfileService(database.ConnectionString);
        await service.SaveAsync(TallyProfile.NewTest("TPDUPA", "TEST - First", new[] { "TPDUP" }), "First owner of the store");
        var before = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.tally_profiles");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(TallyProfile.NewTest("TPDUPB", "TEST - Second", new[] { "TPDUP" }), "Second company"));

        Assert.Contains("already linked to another test Tally company", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.tally_profiles"));
    }

    [Fact]
    public async Task Unknown_stores_and_reused_short_codes_are_refused_in_plain_words()
    {
        var service = new SqlServerTallyProfileService(database.ConnectionString);
        var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(TallyProfile.NewTest("TPNOSTORE", "TEST - Unknown store", new[] { "NOSUCHSTORE" }), "Unknown store"));
        Assert.Contains("not in Settings", unknown.Message, StringComparison.Ordinal);

        await service.SaveAsync(TallyProfile.NewTest("TPCODE", "TEST - Code", Array.Empty<string>()), "First use of the code");
        var reused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(TallyProfile.NewTest("TPCODE", "TEST - Another", Array.Empty<string>()), "Reuse"));
        Assert.Contains("already uses the short code TPCODE", reused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_remote_Tally_address_is_refused_before_the_database_is_touched()
    {
        var service = new SqlServerTallyProfileService(database.ConnectionString);
        var before = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.tally_profiles");
        var error = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(
            TallyProfile.NewTest("TPREMOTE", "TEST - Remote", Array.Empty<string>()) with { EndpointUrl = "http://192.168.1.20:9000/" }, "Remote"));
        Assert.Equal(TallyProfileRules.OnlyThisPc, error.Message);
        Assert.Equal(before, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.tally_profiles"));
    }
}
