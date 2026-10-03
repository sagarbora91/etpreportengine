using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.3, migration 0042. On Workpc (2 October 2026) the old PC's accounts could not be
/// deactivated in Settings &gt; Users: dbo.configure_application_role ran CREATE LOGIN ... FROM
/// WINDOWS first, and Windows cannot resolve an account of a machine that no longer exists.
///
/// After a restore onto a new PC, the old PC's account is a Windows database user (type U)
/// that holds the old machine's SID, which no server login has, under a name Windows cannot
/// resolve (TFRROWJLTULT009\Sagar). SQL Server will not create that from T-SQL: CREATE USER
/// with a backslash name resolves the name in Windows ("Windows NT user or group ... not
/// found"), even WITHOUT LOGIN, and a user mapped to a login may not take a backslash name
/// that is not its account's ("... contains invalid characters"); ALTER USER ... WITH NAME has
/// the same rule. So the fixture builds the same principal minus the backslash: a database
/// user created for a transient Windows login of a well-known account (NT AUTHORITY\LOCAL
/// SERVICE), under a name Windows cannot resolve, and the login is dropped in the same
/// transaction. What is left is type U, a Windows SID with no server login, and
/// SUSER_SID(name) IS NULL - the state 0042's deactivate-by-name path is for. The one thing
/// it cannot copy is that the old SID itself does not resolve; S-1-5-19 does.
/// The calls are the ones UpsertUserAsync makes.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class RetiredAccountDeactivationTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    // Its login exists only inside CreateRetiredUserAsync's transaction, holding no permission.
    private const string Donor = @"NT AUTHORITY\LOCAL SERVICE";

    [Fact]
    public async Task An_account_of_a_retired_pc_can_be_deactivated_with_its_audit_trail()
    {
        var identity = OrphanedWindowsUserName();
        try
        {
            await CreateRetiredUserAsync(identity, "STORE_MANAGER", "Retired PC account", "etp_store_manager");
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
        try
        {
            await database.ExecuteAsync($"""
                INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
                VALUES(N'{identity}',N'Retired viewer','VIEWER',1,SUSER_SNAME(),N'Integration test');
                """);
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
        // The fixture's only Owner that can sign in is the account running the tests (0011). The
        // count is the guard's own (0043): an active Owner row of an account with neither a login
        // nor a Windows account - a retired PC's - does not count, so one left behind by another
        // test cannot make this precondition fail.
        const string usableOwners = "SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1 AND (SUSER_ID(windows_identity) IS NOT NULL OR SUSER_SID(windows_identity) IS NOT NULL)";
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(usableOwners)));
        var owner = (string)(await database.ExecuteAsync(
            "SELECT windows_identity FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1 AND (SUSER_ID(windows_identity) IS NOT NULL OR SUSER_SID(windows_identity) IS NOT NULL)"))!;
        var literal = owner.Replace("'", "''");
        var roles = $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE u.sid=SUSER_SID(N'{literal}')";
        var rolesBefore = Convert.ToInt32(await database.ExecuteAsync(roles));
        foreach (var call in new[] { $"N'{literal}','OWNER',0", $"N'{literal}','VIEWER',1" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"EXEC dbo.configure_application_role {call};"));
            Assert.Equal(51230, error.Number);
        }
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(usableOwners)));
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(
            $"SELECT COUNT(*) FROM dbo.application_users WHERE windows_identity=N'{literal}' AND role_code='OWNER' AND is_active=1")));
        Assert.Equal(rolesBefore, Convert.ToInt32(await database.ExecuteAsync(roles)));
    }

    [Fact]
    public async Task A_retired_owner_can_be_deactivated_while_another_owner_remains()
    {
        var identity = OrphanedWindowsUserName();
        try
        {
            await CreateRetiredUserAsync(identity, "OWNER", "Retired owner", "etp_owner", "db_owner");
            await SaveAsync(identity, "OWNER", active: false, "Old PC retired; integration test");
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync(
                $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE u.name=N'{identity}'")));
            Assert.Equal("DENY", await database.ExecuteAsync(
                $"SELECT p.state_desc FROM sys.database_permissions p JOIN sys.database_principals u ON u.principal_id=p.grantee_principal_id WHERE u.name=N'{identity}' AND p.permission_name='CONNECT'"));
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1")));
        }
        finally { await RemoveAsync(identity); }
    }

    // ---------------------------------------------------------------- plumbing

    // An account of a PC that is gone, for the paths that never create a database user.
    private static string RetiredIdentity() => $"ETPGONE{Guid.NewGuid():N}"[..15] + @"\Sagar";

    // The name of a retired PC's database user, without the backslash SQL Server refuses (see the summary).
    private static string OrphanedWindowsUserName() => $"ETPGONE{Guid.NewGuid():N}"[..19];

    private async Task CreateRetiredUserAsync(string identity, string role, string displayName, params string[] databaseRoles)
    {
        // One transaction: the donor login never outlives this batch, and a refused step leaves
        // neither the user nor an active application_users row behind (XACT_ABORT).
        var memberships = string.Concat(databaseRoles.Select(r => $"ALTER ROLE {r} ADD MEMBER [{identity}];\n"));
        await database.ExecuteAsync($"""
            SET XACT_ABORT ON;
            IF SUSER_ID(N'{Donor}') IS NOT NULL THROW 51999,'{Donor.Replace("'", "''")} already has a login on this SQL Server instance; these tests need it absent.',1;
            BEGIN TRANSACTION;
            CREATE LOGIN [{Donor}] FROM WINDOWS;
            CREATE USER [{identity}] FOR LOGIN [{Donor}];
            {memberships}
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
            VALUES(N'{identity}',N'{displayName}','{role}',1,SUSER_SNAME(),N'Integration test');
            DROP LOGIN [{Donor}];
            COMMIT TRANSACTION;
            """);

        // The state a restore leaves: a Windows user, no login has its SID, and Windows cannot resolve its name.
        Assert.Equal("U", await database.ExecuteAsync($"SELECT type FROM sys.database_principals WHERE name=N'{identity}'"));
        Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync(
            $"SELECT COUNT(*) FROM sys.server_principals sp JOIN sys.database_principals dp ON dp.sid=sp.sid WHERE dp.name=N'{identity}'")));
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync(
            $"SELECT CASE WHEN SUSER_SID(N'{identity}') IS NULL AND SUSER_ID(N'{identity}') IS NULL THEN 1 ELSE 0 END")));
    }

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
