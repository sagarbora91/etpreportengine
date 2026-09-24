using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// P4-14. configure_application_role ended an active user's provisioning with REVOKE CONNECT,
/// which removes the CONNECT that CREATE USER grants, so every Store Manager and Viewer added
/// through Settings > Users was locked out of the database. Migration 0022 then ran every
/// existing user through it, which is how NT AUTHORITY\SYSTEM - the account the scheduled
/// Automated Operations and Daily Backup tasks run as - lost access on the shop PC.
///
/// Every earlier role test impersonated a database user from inside the database, which
/// never re-checks CONNECT. These enter the database the way a real login does: from master,
/// as the login.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class ApplicationRoleConnectTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Principal = @"NT AUTHORITY\LOCAL SERVICE";

    // OWNER is deliberately absent. Owners were never locked out - db_owner implies CONNECT -
    // and provisioning one grants ALTER ANY LOGIN at server level, which must never be left on
    // a well-known account if this test is interrupted.
    [Theory]
    [InlineData("STORE_MANAGER")]
    [InlineData("VIEWER")]
    public async Task A_user_added_in_Settings_can_actually_enter_the_database(string role)
    {
        var created = await EnsureLoginAbsentBeforehandAsync();
        try
        {
            await ProvisionAsync(role, active: true);
            Assert.True(await CanEnterAsync(Principal), $"An active {role} could not enter the database.");
        }
        finally { await RemoveLoginIfCreatedAsync(created); }
    }

    [Fact]
    public async Task A_deactivated_user_is_kept_out_and_reactivating_lets_them_back_in()
    {
        var created = await EnsureLoginAbsentBeforehandAsync();
        try
        {
            await ProvisionAsync("STORE_MANAGER", active: true);
            Assert.True(await CanEnterAsync(Principal));

            await ProvisionAsync("STORE_MANAGER", active: false);
            Assert.False(await CanEnterAsync(Principal), "A deactivated user could still enter the database.");

            // GRANT replaces the DENY that deactivation left. The old REVOKE cleared the DENY
            // and the GRANT together, so reactivating never actually worked either.
            await ProvisionAsync("STORE_MANAGER", active: true);
            Assert.True(await CanEnterAsync(Principal), "A reactivated user was still locked out.");
        }
        finally { await RemoveLoginIfCreatedAsync(created); }
    }

    [Fact]
    public async Task The_automation_account_can_enter_a_database_migrated_past_0022()
    {
        // The exact lockout on the shop PC: 0012 gives SYSTEM CONNECT, 0022's re-provisioning
        // cursor took it away, and 0032 has to give it back. A fresh fixture applies all
        // three in order, so this is that path, not a simulation of it.
        Assert.Equal("GRANT", await ScalarAsync<string>(
            $"USE [{database.Name}]; SELECT ISNULL((SELECT TOP(1) p.state_desc FROM sys.database_permissions p " +
            "JOIN sys.database_principals dp ON dp.principal_id=p.grantee_principal_id " +
            "WHERE dp.sid=SUSER_SID(N'NT AUTHORITY\\SYSTEM') AND p.permission_name='CONNECT'),'NONE');"));
        Assert.True(await CanEnterAsync(@"NT AUTHORITY\SYSTEM"), "The automation account cannot enter the database.");
    }

    [Fact]
    public async Task The_0032_repair_steps_over_the_account_running_it()
    {
        // An Owner who is not a SQL administrator migrates as a db_owner member, and the old
        // procedure may have revoked that account's CONNECT too. SQL does not grant to
        // yourself: it skips the statement with a warning, which in an upgrade log reads as
        // if the repair had failed. The repair leaves that account out instead.
        var created = await EnsureLoginAbsentBeforehandAsync();
        try
        {
            await ProvisionAsync("STORE_MANAGER", active: true);
            await database.ExecuteAsync(
                $"DECLARE @u sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(N'{Principal}')); " +
                "DECLARE @s nvarchar(max)=N'REVOKE CONNECT FROM '+QUOTENAME(@u)+N'; ALTER ROLE db_owner ADD MEMBER '+QUOTENAME(@u)+N';'; EXEC(@s);");

            // What the exclusion avoids.
            Assert.Contains("yourself", await MessagesAsync($"EXECUTE AS USER=N'{Principal}'; GRANT CONNECT TO [{Principal}]; REVERT;"), StringComparison.Ordinal);

            // The shipped repair, run as that account, completes without it.
            var migration = await File.ReadAllTextAsync(Path.Combine(database.MigrationDirectory, "0032_active_users_can_connect.sql"));
            var repair = migration[migration.IndexOf("DECLARE @repair", StringComparison.Ordinal)..];
            Assert.DoesNotContain("yourself", await MessagesAsync($"EXECUTE AS USER=N'{Principal}'; {repair} REVERT;"), StringComparison.Ordinal);
        }
        finally { await RemoveLoginIfCreatedAsync(created); }
    }

    private async Task<string> MessagesAsync(string sql)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        var messages = new System.Text.StringBuilder();
        connection.InfoMessage += (_, e) => messages.AppendLine(e.Message);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        return messages.ToString();
    }

    // ---------------------------------------------------------------- the product path

    private async Task ProvisionAsync(string role, bool active)
    {
        // What Phase2OperationsRepository.UpsertUserAsync runs for Settings > Users.
        await database.ExecuteAsync($"EXEC dbo.configure_application_role N'{Principal}','{role}',{(active ? 1 : 0)};");
        await database.ExecuteAsync($"""
            MERGE dbo.application_users AS target
            USING(SELECT N'{Principal}' windows_identity) source ON target.windows_identity=source.windows_identity
            WHEN MATCHED THEN UPDATE SET role_code='{role}',is_active={(active ? 1 : 0)}
            WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
              VALUES(N'{Principal}',N'Connect test','{role}',{(active ? 1 : 0)},SUSER_SNAME(),N'P4-14 connect test');
            """);
    }

    private async Task<bool> CanEnterAsync(string login)
    {
        // Enter the way a connection does: as the login, from outside the database.
        var result = await ScalarAsync<string>(
            $"EXECUTE AS LOGIN=N'{login}'; " +
            $"BEGIN TRY EXEC(N'USE [{database.Name}]; SELECT ''IN'''); END TRY BEGIN CATCH SELECT 'OUT'; END CATCH; REVERT;");
        return result == "IN";
    }

    // ---------------------------------------------------------------- plumbing

    // Unpooled: a session a severe error kills must not be handed to the next caller.
    private string MasterConnectionString => new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task<bool> EnsureLoginAbsentBeforehandAsync() =>
        await ScalarAsync<int>($"SELECT CASE WHEN SUSER_ID(N'{Principal}') IS NULL THEN 1 ELSE 0 END") == 1;

    private async Task RemoveLoginIfCreatedAsync(bool createdByThisTest)
    {
        // Only undo what this test created; a login that already existed is left alone.
        if (!createdByThisTest) return;
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF SUSER_ID(N'{Principal}') IS NOT NULL DROP LOGIN [{Principal}];", connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Both classes create and drop the same well-known Windows login on the SQL instance, so
/// they must never run at the same time: one would drop the login the other is using.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerPrincipalTests
{
    public const string Name = "Server principals";
}
