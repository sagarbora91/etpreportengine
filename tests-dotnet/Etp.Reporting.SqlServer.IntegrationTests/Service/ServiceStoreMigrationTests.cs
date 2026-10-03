using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Service interim (S-2, decision 15), migration 0048 section A_SERVICE_STORE (lane L0), applied to a real database:
/// the Service Centre is store AW330 of a new SERVICE business unit and is inactive; the Retail stores keep their NULL
/// business_unit_id; trg_stores_service_unit_inactive refuses an active SERVICE store (51900) and a move out of the
/// SERVICE unit (51904); a second application of the script changes nothing.
/// </summary>
public sealed class ServiceStoreMigrationTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string State = """
        SELECT CONCAT(
          (SELECT COUNT(*) FROM dbo.business_units WHERE business_unit_code='SERVICE'), '|',
          (SELECT COUNT(*) FROM dbo.stores WHERE store_code='AW330'), '|',
          (SELECT CONCAT(CAST(s.is_active AS int), ':', b.business_unit_code) FROM dbo.stores s
             JOIN dbo.business_units b ON b.business_unit_id=s.business_unit_id WHERE s.store_code='AW330'), '|',
          (SELECT COUNT(*) FROM dbo.stores WHERE store_code IN('WLMHW','HEMW') AND business_unit_id IS NULL), '|',
          (SELECT COUNT(*) FROM sys.triggers WHERE name='trg_stores_service_unit_inactive' AND parent_id=OBJECT_ID(N'dbo.stores') AND is_disabled=0))
        """;

    private const string Expected = "1|1|0:SERVICE|2|1";

    [Fact]
    public async Task The_migration_adds_an_inactive_AW330_in_the_SERVICE_unit_and_leaves_the_Retail_stores_alone()
    {
        Assert.Equal(Expected, await database.ExecuteAsync(State));
        Assert.Equal(1, await database.ExecuteAsync("SELECT CAST(is_active AS int) FROM dbo.business_units WHERE business_unit_code='SERVICE'"));
    }

    [Fact]
    public async Task A_second_application_of_0048_is_a_no_op()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(database.MigrationDirectory, "0048_service_centre_interim.sql"));
        await database.ExecuteAsync(script);
        await database.ExecuteAsync(script);
        Assert.Equal(Expected, await database.ExecuteAsync(State));
    }

    [Theory]
    [InlineData("UPDATE dbo.stores SET is_active=1 WHERE store_code='AW330'")]
    [InlineData("INSERT dbo.stores(store_code,store_name,business_unit_id,is_active) SELECT 'SVCTEST',N'Synthetic service store',business_unit_id,1 FROM dbo.business_units WHERE business_unit_code='SERVICE'")]
    [InlineData("UPDATE dbo.stores SET business_unit_id=(SELECT business_unit_id FROM dbo.business_units WHERE business_unit_code='SERVICE') WHERE store_code='WLMHW'")]
    public async Task An_active_store_in_the_SERVICE_unit_is_refused_with_51900(string sql)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
        Assert.Contains(error.Errors.Cast<SqlError>(), e => e.Number == 51900);
        Assert.Contains("cannot be made an active shop store", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stores WHERE store_code='SVCTEST'"));
        Assert.Equal(Expected, await database.ExecuteAsync(State));
    }

    [Theory]
    [InlineData("UPDATE dbo.stores SET business_unit_id=NULL WHERE store_code='AW330'")]
    [InlineData("""
        IF NOT EXISTS(SELECT 1 FROM dbo.business_units WHERE business_unit_code='SVCOTHER')
          INSERT dbo.business_units(business_unit_code,business_unit_name) VALUES('SVCOTHER',N'Synthetic other unit');
        UPDATE dbo.stores SET business_unit_id=(SELECT business_unit_id FROM dbo.business_units WHERE business_unit_code='SVCOTHER') WHERE store_code='AW330';
        """)]
    public async Task Moving_a_SERVICE_store_out_of_the_unit_is_refused_with_51904_even_while_it_stays_inactive(string sql)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(sql));
        Assert.Contains(error.Errors.Cast<SqlError>(), e => e.Number == 51904);
        Assert.DoesNotContain(error.Errors.Cast<SqlError>(), e => e.Number == 51900);
        Assert.Contains("cannot be moved out of the Service Centre business unit", error.Message, StringComparison.Ordinal);
        Assert.Equal(Expected, await database.ExecuteAsync(State));
    }

    [Fact]
    public async Task Retail_stores_and_an_inactive_SERVICE_store_are_not_refused()
    {
        var retail = await database.ExecuteAsync("SELECT CAST(is_active AS int) FROM dbo.stores WHERE store_code='WLMHW'");
        await database.ExecuteAsync("UPDATE dbo.stores SET is_active=is_active, store_name=store_name WHERE store_code IN('WLMHW','HEMW')");
        await database.ExecuteAsync("UPDATE dbo.stores SET store_name=store_name WHERE store_code='AW330'");
        Assert.Equal(retail, await database.ExecuteAsync("SELECT CAST(is_active AS int) FROM dbo.stores WHERE store_code='WLMHW'"));
        Assert.Equal(Expected, await database.ExecuteAsync(State));
    }
}
