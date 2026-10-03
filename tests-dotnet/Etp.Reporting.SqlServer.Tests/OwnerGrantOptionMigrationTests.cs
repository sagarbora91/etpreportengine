using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.3, migration 0043 (Sagar's decision, 2 October 2026: option b). Owners hold ALTER ANY
/// LOGIN WITH GRANT OPTION, so an Owner who opened ETP normally can add and change users;
/// demotion revokes it WITH CASCADE; the account a statement would grant to itself is never
/// sent that statement. These tests read the shipped migration; the SQL behaviour is covered
/// by OwnerGrantOptionTests in the SQL integration suite.
/// </summary>
public sealed class OwnerGrantOptionMigrationTests
{
    private const string MigrationName = "0043_owner_grant_option.sql";

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string ProcedureBody(string migration)
    {
        const string start = "EXEC(N'CREATE OR ALTER PROCEDURE dbo.configure_application_role";
        var from = migration.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "The migration does not redefine dbo.configure_application_role.");
        var to = migration.IndexOf("\nEND');", from, StringComparison.Ordinal);
        Assert.True(to > from, "The procedure definition is not closed.");
        return migration[from..to];
    }

    private static string Upgrade(string migration)
    {
        var from = migration.IndexOf("\nEND');", StringComparison.Ordinal);
        Assert.True(from >= 0);
        return migration[from..];
    }

    private static int At(string text, string fragment, int after = -1)
    {
        var index = text.IndexOf(fragment, after + 1, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{fragment}' is missing{(after >= 0 ? " after position " + after : "")}.");
        return index;
    }

    private static int Count(string text, string fragment) => text.Split(fragment).Length - 1;

    [Fact]
    public void The_migration_ships_after_0042_says_it_needs_it_and_is_not_an_edit_of_an_applied_one()
    {
        var names = Directory.GetFiles(MigrationsDirectory(), "*.sql").Select(Path.GetFileName).ToArray();
        Assert.Contains(MigrationName, names);
        Assert.True(string.CompareOrdinal(MigrationName, "0042_deactivate_unresolvable_accounts.sql") > 0);
        var migration = Read(MigrationName);
        Assert.Contains("REQUIRES 0042_deactivate_unresolvable_accounts", migration, StringComparison.Ordinal);
        // The runner applies a migration as one batch inside one transaction: no GO separators.
        Assert.DoesNotContain("\nGO\n", migration, StringComparison.OrdinalIgnoreCase);
        // 0042's own changes are carried, so the merge of both branches ends with both.
        var body = ProcedureBody(migration);
        Assert.Contains("THROW 51230,''Keep at least one active Owner. Add another Owner before changing this account.'',1;", body, StringComparison.Ordinal);
        Assert.Contains("THROW 51471,@notFound,1;", body, StringComparison.Ordinal);
        Assert.Contains("IF @principal IS NULL AND @active=0", body, StringComparison.Ordinal);
        Assert.Contains("IF @loginExists=0 AND @active=1", body, StringComparison.Ordinal);
    }

    [Fact]
    public void An_active_owner_is_granted_alter_any_login_with_grant_option_and_nobody_else_is_granted_it()
    {
        var body = ProcedureBody(Read(MigrationName));
        var ownerBranch = At(body, "IF @role=''OWNER'' AND @active=1\n  BEGIN");
        var grant = At(body, "GRANT ALTER ANY LOGIN TO ''+QUOTENAME(@identity)+N'' WITH GRANT OPTION;''", ownerBranch);
        var otherwise = At(body, "ELSE IF @self=0", ownerBranch);
        Assert.True(grant < otherwise);
        // Every GRANT of the server permission in the procedure carries the grant option: the
        // Owner's own, and the re-grant to Owners a cascade took it from.
        Assert.Equal(2, Count(body, "GRANT ALTER ANY LOGIN TO "));
        Assert.Equal(2, Count(body, " WITH GRANT OPTION;"));
        // The plain grant 0022-0042 made is gone.
        Assert.DoesNotContain("THEN N''GRANT ALTER ANY LOGIN TO '' ELSE", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Anyone_who_is_not_an_active_owner_has_it_revoked_with_cascade_and_the_revoke_is_checked()
    {
        var body = ProcedureBody(Read(MigrationName));
        Assert.Equal(1, Count(body, "REVOKE ALTER ANY LOGIN"));
        var revoke = At(body, "REVOKE ALTER ANY LOGIN FROM ''+QUOTENAME(@identity)+N'' CASCADE;''");
        Assert.True(At(body, "ELSE IF @self=0") < revoke);
        // Server permissions are granted and revoked only with master as the current database.
        Assert.Equal(3, Count(body, "N''USE [master]; "));
        // A revoke SQL Server did not complete is refused, never left as a non-owner able to
        // manage logins.
        var kept = At(body, "THROW 51473,", revoke);
        Assert.True(At(body, "IF EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=@grantee AND permission_name=N''ALTER ANY LOGIN'')", revoke) < kept);
        Assert.Contains("Start ETP with Run as administrator and save again.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Owners_a_cascade_takes_the_right_from_get_it_back_and_losing_your_own_is_refused()
    {
        var body = ProcedureBody(Read(MigrationName));
        var remembered = At(body, "INSERT @owners(principal_id,login_name,login_sid,had_grant_option)");
        var revoke = At(body, "REVOKE ALTER ANY LOGIN FROM");
        var ownLoss = At(body, "THROW 51474,", revoke);
        var regrant = At(body, "SELECT @regrant+=N''GRANT ALTER ANY LOGIN TO ''+QUOTENAME(o.login_name)+N'' WITH GRANT OPTION;'' FROM @owners o", ownLoss);
        Assert.True(remembered < revoke);
        // Every remaining active Owner with a login, other than the account being changed, is
        // remembered with whether it held the grant option before the REVOKE.
        var owners = body[remembered..revoke];
        Assert.Contains("u.role_code=''OWNER'' AND u.is_active=1", owners, StringComparison.Ordinal);
        Assert.Contains("sp.principal_id<>@grantee", owners, StringComparison.Ordinal);
        Assert.Contains("p.state=''W'') THEN 1 ELSE 0 END", owners, StringComparison.Ordinal);
        // The executing account is refused only if the cascade took a grant option it had,
        // and it is never in the re-grant, which covers every remaining Owner lacking it.
        Assert.Contains("WHERE o.login_sid=SUSER_SID() AND o.had_grant_option=1", body[revoke..ownLoss], StringComparison.Ordinal);
        var regrantSource = body[regrant..At(body, "IF LEN(@regrant)>0", regrant)];
        Assert.Contains("WHERE o.login_sid<>SUSER_SID()", regrantSource, StringComparison.Ordinal);
        Assert.DoesNotContain("had_grant_option", regrantSource, StringComparison.Ordinal);
        Assert.True(regrant < At(body, "EXEC(@regrant);", regrant));
    }

    [Fact]
    public void Deactivating_by_name_takes_only_a_truly_orphaned_database_user()
    {
        // Security review 1.9.3, F2: a renamed local account keeps its SID, so a database user
        // still named after the old name can belong to a live login. The name fallback must
        // never pick it.
        var body = ProcedureBody(Read(MigrationName));
        var fallback = At(body, "IF @principal IS NULL AND @active=0");
        var next = At(body, "IF @principal IS NULL AND @active=1", fallback);
        var lookup = body[fallback..next];
        Assert.Contains("WHERE dp.name=@identity AND dp.type IN (''U'',''G'',''S'')", lookup, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS(SELECT 1 FROM sys.server_principals sp WHERE sp.sid=dp.sid)", lookup, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS(SELECT 1 FROM dbo.application_users u WHERE u.is_active=1 AND u.windows_identity<>@identity AND SUSER_SID(u.windows_identity)=dp.sid)", lookup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_account_making_the_change_is_never_sent_a_grant_or_revoke_on_itself()
    {
        var body = ProcedureBody(Read(MigrationName));
        // SQL Server applies its "cannot grant ... to yourself" rule (4627) by SID: it skipped
        // the GRANT on Workpc for a login created in the same session.
        var self = At(body, "DECLARE @self bit=CASE WHEN SUSER_SID(@identity)=SUSER_SID() THEN 1 ELSE 0 END;");
        Assert.True(self < At(body, "IF @self=0\n    BEGIN\n      SET @serverPermission=N''USE [master]; GRANT"));
        Assert.True(self < At(body, "ELSE IF @self=0"));
        // An Owner left without the grant option is reported, not silently skipped.
        Assert.Contains("PRINT N''ETP: ''+@identity+N'' is an Owner without ALTER ANY LOGIN WITH GRANT OPTION", body, StringComparison.Ordinal);
        Assert.Contains("docs\\OPERATIONS.md, Owners and SQL Server logins", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_owner_guard_counts_only_owners_that_can_still_sign_in()
    {
        // Security review 1.9.3, F4: after a restore the old PC's Owner rows are still active.
        // Counting them let the only usable Owner demote or deactivate itself.
        var body = ProcedureBody(Read(MigrationName));
        var guard = At(body, "THROW 51230,");
        var others = body[At(body, "AND NOT EXISTS(SELECT 1 FROM dbo.application_users WITH(UPDLOCK,HOLDLOCK)")..guard];
        Assert.Contains("WHERE role_code=''OWNER'' AND is_active=1 AND windows_identity<>@identity", others, StringComparison.Ordinal);
        Assert.Contains("AND (SUSER_ID(windows_identity) IS NOT NULL OR SUSER_SID(windows_identity) IS NOT NULL))", others, StringComparison.Ordinal);
    }

    [Fact]
    public void Taking_away_your_own_owner_access_is_refused_before_anything_changes()
    {
        var body = ProcedureBody(Read(MigrationName));
        var guard = At(body, "THROW 51472,''You cannot take away your own Owner access. Ask another Owner to change your account.'',1;");
        Assert.True(At(body, "IF (@active=0 OR @role<>''OWNER'') AND SUSER_SID(@identity)=SUSER_SID()") < guard);
        Assert.True(At(body, "THROW 51312,''Owner permission is required to manage access.''") < guard);
        Assert.True(At(body, "THROW 51230,") < guard);
        Assert.True(guard < At(body, "CREATE LOGIN"));
        Assert.True(guard < At(body, "EXEC(@membership);"));
        Assert.True(guard < At(body, "EXEC(@serverPermission);"));
    }

    [Fact]
    public void The_role_and_permission_block_is_byte_for_byte_the_one_0036_and_0042_installed()
    {
        // Only the server-level permission changed; what an active or inactive user is given
        // or denied inside the database must not drift.
        static string Block(string body)
        {
            var from = body.IndexOf("  DECLARE @membership nvarchar(max)=N'''';", StringComparison.Ordinal);
            var to = body.IndexOf("  EXEC(@membership);", StringComparison.Ordinal);
            Assert.True(from >= 0 && to > from, "The memberships block could not be found.");
            return body[from..to];
        }
        Assert.Equal(Block(ProcedureBody(Read("0036_retire_unused_master_values.sql"))), Block(ProcedureBody(Read(MigrationName))));
    }

    [Fact]
    public void Existing_owners_are_upgraded_by_an_account_that_can_grant_and_never_the_account_running_it()
    {
        var upgrade = Upgrade(Read(MigrationName));
        var capable = At(upgrade, "IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)=1");
        Assert.Contains("HAS_PERMS_BY_NAME(NULL,NULL,N'CONTROL SERVER')", upgrade, StringComparison.Ordinal);
        Assert.Contains("p.permission_name=N'ALTER ANY LOGIN' AND p.state='W')", upgrade, StringComparison.Ordinal);
        var grants = At(upgrade, "SELECT @ownerGrants+=N'GRANT ALTER ANY LOGIN TO '+QUOTENAME(sp.name)+N' WITH GRANT OPTION;'", capable);
        var source = upgrade[grants..At(upgrade, "IF LEN(@ownerGrants)>0", grants)];
        Assert.Contains("u.role_code='OWNER' AND u.is_active=1", source, StringComparison.Ordinal);
        Assert.Contains("sp.sid<>SUSER_SID()", source, StringComparison.Ordinal);
        Assert.Contains("p.state='W'", source, StringComparison.Ordinal);
        Assert.Contains("SET @ownerGrants=N'USE [master]; '+@ownerGrants;", upgrade, StringComparison.Ordinal);
        // Non-owners are not touched by the upgrade.
        Assert.DoesNotContain("REVOKE", upgrade, StringComparison.Ordinal);
        Assert.Equal(1, Count(upgrade, "GRANT ALTER ANY LOGIN TO "));
    }

    [Fact]
    public void The_grant_probe_answers_yes_for_exactly_the_state_0043_grants()
    {
        // be118f3's probe reads state 'W' (GRANT_WITH_GRANT_OPTION), which is what WITH GRANT
        // OPTION records: an Owner provisioned by 0043 sees no "Run as administrator" warning.
        Assert.Contains("p.permission_name=N'ALTER ANY LOGIN' AND p.state='W'", Phase2OperationsRepository.UserAccessGrantProbeSql, StringComparison.Ordinal);
        Assert.Contains("WITH GRANT OPTION", ProcedureBody(Read(MigrationName)), StringComparison.Ordinal);
    }
}
