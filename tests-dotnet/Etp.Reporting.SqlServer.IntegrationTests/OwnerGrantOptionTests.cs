using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.3, migration 0043 (Sagar's decision, 2 October 2026: option b). Owners hold ALTER ANY
/// LOGIN WITH GRANT OPTION, so an Owner who opened ETP without "Run as administrator" can add
/// and change users; demotion revokes it WITH CASCADE and gives it back to the Owners who
/// remain. Also the deactivate-by-name guard from security review 1.9.3, F2.
///
/// An unelevated Owner is a login that is not a SQL administrator, entered with EXECUTE AS
/// LOGIN. These tests give server-level rights to well-known Windows accounts, which must never
/// outlive a test - not even one that is killed - so each runs on one connection inside one
/// transaction that is always rolled back: SQL Server rolls it back by itself if the process
/// dies. Each impersonated step runs under a savepoint, so a refused step is undone the way
/// UpsertUserAsync's rollback undoes it, and REVERT always runs.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class OwnerGrantOptionTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string OwnerA = @"NT AUTHORITY\LOCAL SERVICE";
    private const string OwnerB = @"NT AUTHORITY\NETWORK SERVICE";

    [Fact]
    public async Task An_unelevated_owner_adds_a_store_manager_and_another_owner_and_the_probe_says_allowed()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        Assert.Equal("W", await session.StateAsync(OwnerA));
        Assert.True(await session.ProbeAsAsync(OwnerA), "be118f3's probe still asks an Owner holding the grant option for Run as administrator.");

        Assert.Equal(0, await session.ProvisionAsAsync(OwnerA, OwnerB, "STORE_MANAGER", active: true));
        Assert.Equal(1, Convert.ToInt32(await session.ScalarAsync($"SELECT CASE WHEN SUSER_ID(N'{OwnerB}') IS NULL THEN 0 ELSE 1 END")));
        Assert.Equal("-", await session.StateAsync(OwnerB));
        Assert.False(await session.ProbeAsAsync(OwnerB));

        Assert.Equal(0, await session.ProvisionAsAsync(OwnerA, OwnerB, "OWNER", active: true));
        Assert.Equal("W", await session.StateAsync(OwnerB));
        Assert.True(await session.ProbeAsAsync(OwnerB));

        // Back to Store Manager: the grant option goes, and nobody is told "yourself".
        Assert.Equal(0, await session.ProvisionAsAsync(OwnerA, OwnerB, "STORE_MANAGER", active: true));
        Assert.Equal("-", await session.StateAsync(OwnerB));
        Assert.Equal("W", await session.StateAsync(OwnerA));
        Assert.DoesNotContain("yourself", session.Messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_a_made_owner_b_and_when_a_is_demoted_b_keeps_the_right()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        Assert.Equal(0, await session.ProvisionAsAsync(OwnerA, OwnerB, "OWNER", active: true));
        Assert.Equal("W", await session.StateAsync(OwnerB));

        // The REVOKE ... CASCADE from A takes back what A granted B; the procedure gives it
        // back to B, a remaining active Owner, in the same change.
        await session.ProvisionAsync(OwnerA, "VIEWER", active: true);
        Assert.Equal("-", await session.StateAsync(OwnerA));
        Assert.Equal("W", await session.StateAsync(OwnerB));
        Assert.True(await session.ProbeAsAsync(OwnerB));

        // And B, unelevated, can still change users.
        Assert.Equal(0, await session.ProvisionAsAsync(OwnerB, OwnerA, "STORE_MANAGER", active: true));
        Assert.Equal("-", await session.StateAsync(OwnerA));
    }

    [Fact]
    public async Task Deactivating_an_owner_revokes_with_cascade_instead_of_failing_with_4611()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        // Before 0043 a plain REVOKE of a grantable permission failed with 4611.
        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        await session.ProvisionAsync(OwnerB, "OWNER", active: true);
        await session.ProvisionAsync(OwnerA, "OWNER", active: false);
        Assert.Equal("-", await session.StateAsync(OwnerA));
        Assert.Equal("W", await session.StateAsync(OwnerB));
        Assert.Equal("DENY", await session.ScalarAsync(
            $"SELECT p.state_desc FROM sys.database_permissions p JOIN sys.database_principals u ON u.principal_id=p.grantee_principal_id WHERE u.sid=SUSER_SID(N'{OwnerA}') AND p.permission_name='CONNECT'"));
    }

    [Fact]
    public async Task An_owner_cannot_take_away_their_own_owner_access()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        await session.ProvisionAsync(OwnerB, "OWNER", active: true);
        Assert.Equal(51472, await session.ProvisionAsAsync(OwnerA, OwnerA, "VIEWER", active: true));
        Assert.Equal(51472, await session.ProvisionAsAsync(OwnerA, OwnerA, "OWNER", active: false));
        Assert.Equal("W", await session.StateAsync(OwnerA));
        Assert.Equal(1, Convert.ToInt32(await session.ScalarAsync(
            $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id=m.role_principal_id JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE r.name=N'etp_owner' AND u.sid=SUSER_SID(N'{OwnerA}')")));
    }

    [Fact]
    public async Task An_owner_whose_right_came_from_the_owner_being_demoted_is_refused_and_nothing_changes()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        Assert.Equal(0, await session.ProvisionAsAsync(OwnerA, OwnerB, "OWNER", active: true));

        // B's grant option came from A. Either SQL Server lets B's REVOKE take A's right, and
        // B's own goes with it (51474), or it leaves A's in place because B did not grant it
        // (51473). Both are refusals; neither may leave A demoted with the right, or B without it.
        var error = await session.ProvisionAsAsync(OwnerB, OwnerA, "VIEWER", active: true);
        Assert.True(error != 0, "B demoted the Owner its own right came from.");
        Assert.True(error is 51473 or 51474, $"Refused, but with SQL error {error} rather than 51473 or 51474: record which, and map it in DescribeUserAccessFailure.");
        Assert.Equal("W", await session.StateAsync(OwnerA));
        Assert.Equal("W", await session.StateAsync(OwnerB));
    }

    [Fact]
    public async Task The_migration_upgrade_gives_an_existing_owner_the_grant_option_and_leaves_others_alone()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);

        // A 1.9.2 install: the Owner holds plain ALTER ANY LOGIN. A Store Manager holding it
        // too would be an anomaly the upgrade must not turn into a grant option.
        await session.ProvisionAsync(OwnerA, "OWNER", active: true);
        await session.ProvisionAsync(OwnerB, "STORE_MANAGER", active: true);
        await session.ScalarAsync($"EXEC(N'USE [master]; REVOKE ALTER ANY LOGIN FROM [{OwnerA}] CASCADE; GRANT ALTER ANY LOGIN TO [{OwnerA}]; GRANT ALTER ANY LOGIN TO [{OwnerB}];');");
        Assert.Equal("G", await session.StateAsync(OwnerA));
        Assert.False(await session.ProbeAsAsync(OwnerA));

        var migration = await File.ReadAllTextAsync(Path.Combine(database.MigrationDirectory, "0043_owner_grant_option.sql"));
        const string end = "\nEND');";
        var upgrade = migration.Replace("\r\n", "\n")[(migration.Replace("\r\n", "\n").IndexOf(end, StringComparison.Ordinal) + end.Length)..];
        session.ClearMessages();
        await session.ScalarAsync(upgrade);

        Assert.Equal("W", await session.StateAsync(OwnerA));
        Assert.True(await session.ProbeAsAsync(OwnerA));
        Assert.Equal("G", await session.StateAsync(OwnerB));
        // The account running the migration is an Owner of the fixture database (0011); it is
        // skipped rather than sent a GRANT SQL Server would answer with "yourself".
        Assert.DoesNotContain("yourself", session.Messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_account_running_the_procedure_is_not_sent_a_grant_on_itself()
    {
        await using var session = await Session.OpenAsync(database.ConnectionString);
        var self = (string)(await session.ScalarAsync("SELECT SUSER_SNAME()"))!;
        session.ClearMessages();
        await session.ScalarAsync($"EXEC dbo.configure_application_role N'{self.Replace("'", "''")}','OWNER',1;");
        Assert.DoesNotContain("yourself", session.Messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivating_by_name_leaves_alone_a_database_user_that_belongs_to_a_live_account()
    {
        // Security review 1.9.3, F2: WORKPC\Shop renamed to WORKPC\Sagar keeps its SID, and the
        // database user can keep the old name while it maps to the live login.
        // SQL Server refuses a backslash name on a database user unless Windows resolves it to that
        // user's own SID, so the stale names here have none (RetiredAccountDeactivationTests explains
        // the fixture). Each user is a Windows user (type U) whose name Windows cannot resolve.
        await using var session = await Session.OpenAsync(database.ConnectionString);
        await session.RequireAbsentAsync(OwnerA, OwnerB);
        var renamed = StaleUserName();
        var orphaned = StaleUserName();
        var dropped = StaleUserName();
        await session.ScalarAsync($"""
            CREATE LOGIN [{OwnerA}] FROM WINDOWS;
            CREATE USER [{renamed}] FOR LOGIN [{OwnerA}];
            ALTER ROLE etp_viewer ADD MEMBER [{renamed}];
            CREATE LOGIN [{OwnerB}] FROM WINDOWS;
            CREATE USER [{orphaned}] FOR LOGIN [{OwnerB}];
            DROP LOGIN [{OwnerB}];
            ALTER ROLE etp_viewer ADD MEMBER [{orphaned}];
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
            VALUES(N'{renamed}',N'Old name','VIEWER',1,SUSER_SNAME(),N'Integration test'),
                  (N'{orphaned}',N'Retired PC','VIEWER',1,SUSER_SNAME(),N'Integration test');
            """);

        await session.ScalarAsync($"EXEC dbo.configure_application_role N'{renamed}','VIEWER',0;");
        Assert.Equal(1, await session.ViewerMembershipAsync(renamed));
        Assert.Equal("NONE", await session.ConnectStateAsync(renamed));

        // A truly orphaned user is still reached by name, as 0042 intended.
        await session.ScalarAsync($"EXEC dbo.configure_application_role N'{orphaned}','VIEWER',0;");
        Assert.Equal(0, await session.ViewerMembershipAsync(orphaned));
        Assert.Equal("DENY", await session.ConnectStateAsync(orphaned));

        // A user whose login is gone but whose SID an active application user still resolves
        // to (a renamed account whose login was never re-created) is left alone too.
        await session.ScalarAsync($"""
            DROP USER [{renamed}];
            CREATE USER [{dropped}] FOR LOGIN [{OwnerA}];
            ALTER ROLE etp_viewer ADD MEMBER [{dropped}];
            DROP LOGIN [{OwnerA}];
            INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
            VALUES(N'{dropped}',N'Old name','VIEWER',1,SUSER_SNAME(),N'Integration test'),
                  (N'{OwnerA}',N'Live account','VIEWER',1,SUSER_SNAME(),N'Integration test');
            """);
        await session.ScalarAsync($"EXEC dbo.configure_application_role N'{dropped}','VIEWER',0;");
        Assert.Equal(1, await session.ViewerMembershipAsync(dropped));
        Assert.Equal("NONE", await session.ConnectStateAsync(dropped));
    }

    private static string StaleUserName() => $"ETPGONE{Guid.NewGuid():N}"[..19];

    // ---------------------------------------------------------------- plumbing

    private sealed class Session : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private readonly SqlTransaction transaction;
        private readonly StringBuilder messages = new();

        private Session(SqlConnection connection, SqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public string Messages => messages.ToString();

        public void ClearMessages() => messages.Clear();

        public static async Task<Session> OpenAsync(string connectionString)
        {
            // Unpooled: the session ends with this test, impersonation and all.
            var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync();
            var session = new Session(connection, connection.BeginTransaction());
            connection.InfoMessage += (_, e) => session.messages.AppendLine(e.Message);
            return session;
        }

        public async Task<object?> ScalarAsync(string sql)
        {
            await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
            return await command.ExecuteScalarAsync();
        }

        public async Task RequireAbsentAsync(params string[] logins)
        {
            // A login that already exists may hold rights these tests do not expect.
            foreach (var login in logins)
                Assert.True(Convert.ToInt32(await ScalarAsync($"SELECT CASE WHEN SUSER_ID(N'{login}') IS NULL THEN 1 ELSE 0 END")) == 1,
                    $"{login} already has a login on this SQL Server instance; these tests need it absent.");
        }

        // What Phase2OperationsRepository.UpsertUserAsync runs, as the account running the tests
        // (a SQL administrator).
        public async Task ProvisionAsync(string identity, string role, bool active) =>
            await ScalarAsync(Save(identity, role, active));

        // The same, as an unelevated login. Returns the SQL error number, 0 when it succeeded.
        public async Task<int> ProvisionAsAsync(string login, string identity, string role, bool active) =>
            Convert.ToInt32(await ScalarAsync($"""
                DECLARE @error int=0;
                EXECUTE AS LOGIN=N'{login}';
                BEGIN TRY
                  SAVE TRANSACTION etp_owner_grant_step;
                  {Save(identity, role, active)}
                END TRY
                BEGIN CATCH
                  SET @error=ERROR_NUMBER();
                  IF XACT_STATE()=1 ROLLBACK TRANSACTION etp_owner_grant_step;
                END CATCH;
                REVERT;
                SELECT @error;
                """));

        public async Task<bool> ProbeAsAsync(string login) =>
            Convert.ToBoolean(await ScalarAsync($"""
                EXECUTE AS LOGIN=N'{login}';
                {Phase2OperationsRepository.UserAccessGrantProbeSql}
                REVERT;
                """));

        // 'W' grant option, 'G' plain, 'D' denied, '-' none.
        public async Task<string> StateAsync(string login) => (string)(await ScalarAsync($"""
            SELECT ISNULL((SELECT TOP(1) CONVERT(varchar(1),p.state) FROM sys.server_permissions p
              WHERE p.class=100 AND p.grantee_principal_id=SUSER_ID(N'{login}') AND p.permission_name=N'ALTER ANY LOGIN'
              ORDER BY CASE p.state WHEN 'W' THEN 0 WHEN 'G' THEN 1 ELSE 2 END),'-');
            """))!;

        public async Task<int> ViewerMembershipAsync(string user) => Convert.ToInt32(await ScalarAsync(
            $"SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id=m.role_principal_id JOIN sys.database_principals u ON u.principal_id=m.member_principal_id WHERE r.name=N'etp_viewer' AND u.name=N'{user}'"));

        public async Task<string> ConnectStateAsync(string user) => (string)(await ScalarAsync(
            $"SELECT ISNULL((SELECT TOP(1) p.state_desc FROM sys.database_permissions p JOIN sys.database_principals u ON u.principal_id=p.grantee_principal_id WHERE u.name=N'{user}' AND p.permission_name='CONNECT' AND p.state='D'),'NONE')"))!;

        private static string Save(string identity, string role, bool active) => $"""
            EXEC dbo.configure_application_role N'{identity}','{role}',{(active ? 1 : 0)};
            MERGE dbo.application_users WITH(HOLDLOCK) AS target
            USING(SELECT N'{identity}' windows_identity) source ON target.windows_identity=source.windows_identity
            WHEN MATCHED THEN UPDATE SET role_code='{role}',is_active={(active ? 1 : 0)},modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Owner grant option test'
            WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
              VALUES(N'{identity}',N'Owner grant option test','{role}',{(active ? 1 : 0)},SUSER_SNAME(),N'Owner grant option test');
            """;

        public async ValueTask DisposeAsync()
        {
            try { await transaction.RollbackAsync(); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
