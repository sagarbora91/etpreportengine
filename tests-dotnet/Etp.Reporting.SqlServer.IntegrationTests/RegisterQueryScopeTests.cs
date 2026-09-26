using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class RegisterQueryScopeTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Courier_query_filters_in_SQL_before_limit_even_with_more_than_500_newer_inward_entries()
    {
        await database.ExecuteAsync("""
            EXEC dbo.save_register_entry @type='COURIER',@store='REG_SCOPE',@date='20260924',@number='COURIER-OLDER',@verification='DRAFT',@reason=N'Synthetic query regression';
            ;WITH numbers AS (SELECT 1 n UNION ALL SELECT n+1 FROM numbers WHERE n<501)
            INSERT dbo.register_entries(register_type,store_code,business_date,document_number,verification_status,created_by,modified_by,change_reason)
            SELECT 'INWARD','REG_SCOPE','20260925',CONCAT('INWARD-NEWER-',n),'DRAFT',SUSER_SNAME(),SUSER_SNAME(),N'Synthetic query regression'
            FROM numbers OPTION(MAXRECURSION 0);
            """);
        var service = new SqlServerDigitalRegisterService(database.ConnectionString);
        var unfiltered = await service.LoadAsync("REG_SCOPE");
        Assert.Equal(500, unfiltered.Count);
        Assert.DoesNotContain(unfiltered, entry => entry.RegisterType == "COURIER");
        var courier = Assert.Single(await service.LoadAsync("REG_SCOPE", registerType: "COURIER"));
        Assert.Equal("COURIER-OLDER", courier.DocumentNumber);
        Assert.Equal(500, (await service.LoadAsync("REG_SCOPE", registerType: "INWARD")).Count);
        Assert.Single(await service.LoadAsync("COURIER-OLDER", registerType: "COURIER"));
    }
}
