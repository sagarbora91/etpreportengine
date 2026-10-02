using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.3, migration 0042. On Workpc (2 October 2026) the old PC's accounts could not be
/// deactivated in Settings &gt; Users: dbo.configure_application_role ran CREATE LOGIN ... FROM
/// WINDOWS first, and Windows cannot resolve an account of a machine that no longer exists.
///
/// A retired machine's account is stood in for by a database user with that name and no login
/// (CREATE USER ... WITHOUT LOGIN): SQL Server cannot resolve it to a Windows SID, which is the
/// situation after a restore onto a new PC. The calls are the ones UpsertUserAsync makes.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class RetiredAccountDeactivationTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task An_account_of_a_retired_pc_can_be_deactivated_with_its_audit_trail()
    {
        var identity = RetiredIdentity();
        await CreateRetiredStoreManagerAsync(identity);
        try
        {
            await SaveAsync(identity, "STORE_MANAGER", active: false, "Old PC retired; integration test");

            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.server_principals WHERE name=N'{identity}'")));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync(
                $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE u.name=N'{identity}'")));
            Assert.Equal("DENY", await database.ExecuteAsync(
                $"SELECT p.state_desc FROM sys.database_permissions p JOIN sys.database_principals u ON u.principal_id=p.grantee_principal_id WHERE u.name=N'{identity}' AND p.permission_name='CONNECT'"));
            Assert.False((bool)(await database.ExecuteAsync($"SELECT is_active FROM dbo.application_users WHERE windows_identity=N'{identity}'"))!);
            Assert.Equal("Old PC retired; integration test", await database.ExecuteAsync(
                $"SELECT TOP(1) change_reason FROM dbo.application_user_history WHERE windows_identity=N'{identity}' ORDER BY application_user_history_id DESC"));
        }
        finally { await RemoveAsync(identity); }
    }

    [Fact]
    public async Task An_account_with_no_database_user_and_no_login_is_deactivated_without_creating_either()
    {
        var identity = RetiredIdentity();
        await database.ExecuteAsync($"""
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
            VALUES(N'{identity}',N'Retired viewer','VIEWER',1,SUSER_SNAME(),N'Integration test');
            """);
        try
        {
            await SaveAsync(identity, "VIEWER", active: false, "Old PC retired; integration test");
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.server_principals WHERE name=N'{identity}'")));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.database_principals WHERE name=N'{identity}'")));
            Assert.False((bool)(await database.ExecuteAsync($"SELECT is_active FROM dbo.application_users WHERE windows_identity=N'{identity}'"))!);
        }
        finally { await RemoveAsync(identity); }
    }

    [Fact]
    public async Task Giving_access_to_an_account_windows_cannot_find_is_refused_with_what_to_do()
    {
        var identity = RetiredIdentity();
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"EXEC dbo.configure_application_role N'{identity}','VIEWER',1;"));
        Assert.Contains(error.Errors.Cast<SqlError>(), e => e.Number == 51471);
        Assert.Contains("can only be deactivated", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM sys.server_principals WHERE name=N'{identity}'")));
    }

    [Fact]
    public async Task The_procedure_refuses_to_strip_the_last_active_owner_before_changing_anything()
    {
        // The fixture's only active Owner is the account running the tests.
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1")));
        var owner = (string)(await database.ExecuteAsync("SELECT windows_identity FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1"))!;
        var literal = owner.Replace("'", "''");
        foreach (var call in new[] { $"N'{literal}','OWNER',0", $"N'{literal}','VIEWER',1" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"EXEC dbo.configure_application_role {call};"));
            Assert.Equal(51230, error.Number);
        }
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1")));
    }

    [Fact]
    public async Task A_retired_owner_can_be_deactivated_while_another_owner_remains()
    {
        var identity = RetiredIdentity();
        await database.ExecuteAsync($"""
            CREATE USER [{identity}] WITHOUT LOGIN;
            ALTER ROLE etp_owner ADD MEMBER [{identity}];
            ALTER ROLE db_owner ADD MEMBER [{identity}];
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
            VALUES(N'{identity}',N'Retired owner','OWNER',1,SUSER_SNAME(),N'Integration test');
            """);
        try
        {
            await SaveAsync(identity, "OWNER", active: false, "Old PC retired; integration test");
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync(
                $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE u.name=N'{identity}'")));
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1")));
        }
        finally { await RemoveAsync(identity); }
    }

    // ---------------------------------------------------------------- plumbing

    private static string RetiredIdentity() => $"ETPGONE{Guid.NewGuid():N}"[..15] + @"\Sagar";

    private async Task CreateRetiredStoreManagerAsync(string identity) => await database.ExecuteAsync($"""
        CREATE USER [{identity}] WITHOUT LOGIN;
        ALTER ROLE etp_store_manager ADD MEMBER [{identity}];
        INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
        VALUES(N'{identity}',N'Retired PC account','STORE_MANAGER',1,SUSER_SNAME(),N'Integration test');
        """);

    private async Task SaveAsync(string identity, string role, bool active, string reason)
    {
        // What Phase2OperationsRepository.UpsertUserAsync runs, in one transaction.
        await database.ExecuteAsync($"""
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            EXEC dbo.configure_application_role N'{identity}','{role}',{(active ? 1 : 0)};
            MERGE dbo.application_users WITH(HOLDLOCK) AS target
            USING(SELECT N'{identity}' windows_identity) source ON target.windows_identity=source.windows_identity
            WHEN MATCHED THEN UPDATE SET role_code='{role}',is_active={(active ? 1 : 0)},modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'{reason}';
            COMMIT TRANSACTION;
            """);
    }

    private async Task RemoveAsync(string identity) => await database.ExecuteAsync($"""
        IF EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=N'{identity}' AND is_active=1)
            UPDATE dbo.application_users SET is_active=0,change_reason=N'Integration test cleanup' WHERE windows_identity=N'{identity}';
        IF DATABASE_PRINCIPAL_ID(N'{identity}') IS NOT NULL DROP USER [{identity}];
        """);
}
