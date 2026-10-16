using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1, VM acceptance finding 1 (16 Oct 2026): the nightly health check runs as the automation account, and DB-07
/// "Database objects match this release" was Skipped NOT_PERMITTED there, because it read the journal, OBJECT_DEFINITION
/// and sys.sql_expression_dependencies itself. DB-07 now calls diag.load_schema_object_state (runs as diag_catalog_reader).
/// Cross-layer: the C# probe on a connection that impersonates the automation account (etp_store_manager + etp_automation,
/// as on Workpc) answers OK, the same as for db_owner, and the reader still holds only VIEW DEFINITION.
/// </summary>
public sealed class SchemaObjectStateSqlTests
{
    private const string Owner = "diag_so_owner";
    private const string Manager = "diag_so_manager";
    private const string Viewer = "diag_so_viewer";
    private const string Automation = "diag_so_automation";

    [SqlFact]
    public async Task DB07_runs_OK_as_the_automation_account_through_diag_load_schema_object_state()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await DiagSql.EnsureUsersAsync(database, Owner, Manager, Viewer, Automation);
            var files = await new DirectorySchemaObjectSource(Path.Combine(AppContext.BaseDirectory, "database", "objects")).DiscoverAsync();
            Assert.False(files.IsEmpty);

            // The fixture's bootstrap applied the same object files, so DB-07 must be OK, for db_owner and the automation account.
            var asOwner = await DatabaseProbes.ObjectsAsync(database.ConnectionString, database.MigrationDirectory, CancellationToken.None);
            Assert.Equal((HealthCheckId.Db07, HealthOutcome.Pass, HealthCheckTexts.Ok), (asOwner.CheckId, asOwner.Outcome, asOwner.FindingCode));

            var asAutomation = await AsUserAsync(database, Automation, connection => DatabaseProbes.ObjectsAsync(connection, files, CancellationToken.None));
            Assert.Equal((HealthCheckId.Db07, HealthOutcome.Pass, HealthCheckTexts.Ok), (asAutomation.CheckId, asAutomation.Outcome, asAutomation.FindingCode));
            Assert.Equal(asOwner.Explanation, asAutomation.Explanation);
            Assert.Equal(asOwner.MetricValue, asAutomation.MetricValue);

            // The cause in 1.12.0: the catalog query DB-07 used is refused to the automation account (only db_owner may read
            // sys.sql_expression_dependencies), which the probe turned into Skipped NOT_PERMITTED.
            var refused = await Assert.ThrowsAsync<SqlException>(() => AsUserAsync(database, Automation, async connection =>
            {
                await using var command = new SqlCommand(SchemaObjectSql.LiveObjectsSql, connection) { CommandTimeout = 120 };
                return await SchemaObjectSql.ReadLiveAsync(command, CancellationToken.None);
            }));
            Assert.True(DatabaseProbeSql.IsNotPermitted(refused), refused.Message);

            // Store Manager and Viewer get no EXECUTE (Owner and automation only, as diag.load_role_permissions).
            foreach (var user in new[] { Manager, Viewer })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => DiagSql.QueryAsync(database, user, "EXEC diag.load_schema_object_state"));
                Assert.Equal(229, denied.Number);
            }

            // The reader the procedure runs as still holds only VIEW DEFINITION (no new right for the journal or the catalog).
            Assert.Equal("VIEW DEFINITION", await database.ExecuteAsync(
                "SELECT STRING_AGG(permission_name,',') FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID('diag_catalog_reader') AND permission_name<>'CONNECT'"));
        }
        finally
        {
            await database.DisposeAsync();
        }
    }

    /// <summary>
    /// <paramref name="run"/> on a connection that impersonates <paramref name="user"/>. Never pooled: EXECUTE AS USER is not
    /// reverted on a pooled connection (SQL error 18059 in the next test, 1.11.0 gate; see DiagSql.QueryAsync).
    /// </summary>
    private static async Task<T> AsUserAsync<T>(SqlDatabaseFixture database, string user, Func<SqlConnection, Task<T>> run)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using (var impersonate = new SqlCommand($"EXECUTE AS USER='{user}';", connection))
            await impersonate.ExecuteNonQueryAsync();
        return await run(connection);
    }
}
