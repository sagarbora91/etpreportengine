using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.10.0, migration 0051 (decision 27, Sagar, 10 Oct 2026, extends D22): only the Owner writes
/// dbo.staff_sales_targets. A Store Manager or Viewer is refused by SQL Server (229), not only by
/// the screen and the service; the Owner (db_owner + etp_owner, as configure_application_role
/// makes it) still saves, and the 0010 trigger still writes the history row.
/// </summary>
public sealed class StaffTargetsOwnerOnlyTests
{
    // The MERGE of OperationalCompletionRepository.SaveStaffTargetAsync, with literals.
    private const string Merge = """
        MERGE dbo.staff_sales_targets WITH(HOLDLOCK) AS target
        USING (SELECT 'PERMTEST' store_code,N'CRO1' cro_number,CONVERT(date,'20261001') period_start,CONVERT(date,'20261031') period_end) AS source
          ON target.store_code=source.store_code AND target.cro_number=source.cro_number AND target.target_month=source.period_start
        WHEN MATCHED THEN UPDATE SET target_sales=target.target_sales+1,modified_by=N'test',modified_utc=SYSUTCDATETIME(),change_reason=N'Permission test'
        WHEN NOT MATCHED THEN INSERT(store_code,cro_number,period_start,period_end,target_sales,entered_by,modified_by,change_reason)
          VALUES(source.store_code,source.cro_number,source.period_start,source.period_end,1000,N'test',N'test',N'Permission test');
        """;

    private static string As(string user, string sql) =>
        $"EXECUTE AS USER='{user}'; BEGIN TRY {sql}; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;";

    [Fact]
    public async Task Only_the_owner_can_write_staff_targets_and_the_history_is_still_written()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER targets_owner WITHOUT LOGIN; ALTER ROLE db_owner ADD MEMBER targets_owner; ALTER ROLE etp_owner ADD MEMBER targets_owner;
                CREATE USER targets_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER targets_manager;
                CREATE USER targets_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER targets_viewer;
                """);

            await database.ExecuteAsync(As("targets_owner", Merge));
            await database.ExecuteAsync(As("targets_owner", Merge));
            Assert.Equal(1001m, Convert.ToDecimal(await database.ExecuteAsync("SELECT target_sales FROM dbo.staff_sales_targets WHERE store_code='PERMTEST'")));
            Assert.Equal(2, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.staff_sales_target_history WHERE store_code='PERMTEST'")));

            foreach (var user in new[] { "targets_manager", "targets_viewer" })
            {
                foreach (var sql in new[]
                         {
                             Merge,
                             "UPDATE dbo.staff_sales_targets SET target_sales=1 WHERE store_code='PERMTEST'",
                             "DELETE dbo.staff_sales_targets WHERE store_code='PERMTEST'",
                         })
                {
                    var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(As(user, sql)));
                    Assert.Equal(229, denied.Number);
                }
                // Reading is unchanged.
                Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(As(user, "SELECT COUNT(*) FROM dbo.staff_sales_targets WHERE store_code='PERMTEST'"))));
            }

            Assert.Equal(1001m, Convert.ToDecimal(await database.ExecuteAsync("SELECT target_sales FROM dbo.staff_sales_targets WHERE store_code='PERMTEST'")));
            Assert.Equal(2, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.staff_sales_target_history WHERE store_code='PERMTEST'")));
            Assert.Equal("DENY", await database.ExecuteAsync("""
                SELECT MIN(state_desc) FROM sys.database_permissions
                WHERE major_id=OBJECT_ID('dbo.staff_sales_targets') AND grantee_principal_id=DATABASE_PRINCIPAL_ID('etp_store_manager')
                """));
        }
        finally { await database.DisposeAsync(); }
    }
}
