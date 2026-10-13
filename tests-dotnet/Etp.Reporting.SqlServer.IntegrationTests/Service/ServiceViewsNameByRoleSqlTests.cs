using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.11.1, lane name-privacy (decision 37 follow-up): migration 0057 gives the customer name of the four older Service read
/// views by role, as 0056 does for v_service_job. Owner and Store Manager read the same names the database owner reads; a
/// Viewer reads every row but never a name. Users without login in each role, through EXECUTE AS, as
/// ServiceJobModelSqlTests does for v_service_job. Synthetic fixtures only ("Sample Customer NN"); each case builds its own
/// database.
/// </summary>
public sealed class ServiceViewsNameByRoleSqlTests
{
    [SqlTheory]
    [InlineData("v_service_status_view_rows")]
    [InlineData("v_service_job_status_current")]
    [InlineData("v_service_status_view_facts")]
    [InlineData("v_service_pending_current")]
    public async Task The_customer_name_is_shown_to_owner_and_store_manager_and_is_null_for_a_viewer(string view)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ImportAsync(database, FixtureFolder("week1"));
            await ImportAsync(database, FixtureFolder("week2"));
            await database.ExecuteAsync("""
                CREATE USER service_name_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER service_name_viewer;
                CREATE USER service_name_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER service_name_manager;
                CREATE USER service_name_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER service_name_owner;
                """);
            var rows = $"SELECT COUNT(*) FROM dbo.{view}";
            var named = $"SELECT COUNT(*) FROM dbo.{view} WHERE customer_name IS NOT NULL";
            // A stable digest of every name the view gives, so the roles that may see names are shown the very same ones.
            var names = $"SELECT CONVERT(varchar(20),COUNT(customer_name))+':'+CONVERT(varchar(20),CHECKSUM_AGG(CHECKSUM(customer_name))) FROM dbo.{view}";
            static string As(string user, string sql) => $"EXECUTE AS USER='{user}'; {sql}; REVERT;";

            // The database owner (the gate's connection, the Owner's own login) is a db_owner member and sees the names.
            var all = await IntAsync(database, rows);
            var withName = await IntAsync(database, named);
            Assert.True(withName > 0, $"The synthetic weeks give {view} customer names.");
            Assert.True(await IntAsync(database, $"SELECT COUNT(*) FROM dbo.{view} WHERE customer_name LIKE N'Sample Customer %'") > 0,
                $"{view} should carry the fixtures' synthetic customer names.");
            var digest = await TextAsync(database, names);

            foreach (var user in new[] { "service_name_owner", "service_name_manager" })
            {
                Assert.Equal(all, await IntAsync(database, As(user, rows)));
                Assert.Equal(withName, await IntAsync(database, As(user, named)));
                Assert.Equal(digest, await TextAsync(database, As(user, names)));
            }

            // A Viewer still reads every row, but never a name.
            Assert.Equal(all, await IntAsync(database, As("service_name_viewer", rows)));
            Assert.Equal(0, await IntAsync(database, As("service_name_viewer", named)));
            Assert.Null(await database.ExecuteAsync(As("service_name_viewer", $"SELECT MAX(customer_name) FROM dbo.{view}")) as string);
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task<FolderImportSummary> ImportAsync(SqlDatabaseFixture database, string folder) =>
        await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));

    private static string FixtureFolder(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests-dotnet", "fixtures", "service-interim", name);
    }

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql, 300));

    private static async Task<string?> TextAsync(SqlDatabaseFixture database, string sql) => (await database.ExecuteAsync(sql, 300))?.ToString();
}
