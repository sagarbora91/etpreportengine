namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.9.3, migration 0042. After the move to Workpc (2 October 2026) the old PC's accounts could
/// not be deactivated in Settings &gt; Users: dbo.configure_application_role began with
/// CREATE LOGIN ... FROM WINDOWS, which fails for an account of a machine that no longer exists.
/// These tests read the shipped migration; the SQL behaviour itself is covered by
/// RetiredAccountDeactivationTests in the SQL integration suite.
/// </summary>
public sealed class RetiredAccountDeactivationMigrationTests
{
    private const string MigrationName = "0042_deactivate_unresolvable_accounts.sql";

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

    private static int At(string text, string fragment)
    {
        var index = text.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{fragment}' is missing.");
        return index;
    }

    [Fact]
    public void The_migration_ships_and_only_0043_redefines_the_procedure_after_it()
    {
        var names = Directory.GetFiles(MigrationsDirectory(), "*.sql").Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Contains(MigrationName, names);
        var later = names.Where(x => string.CompareOrdinal(x, "0037") > 0 && string.CompareOrdinal(x, MigrationName) != 0)
            .Where(x => Read(x!).Contains("PROCEDURE dbo.configure_application_role", StringComparison.Ordinal)).ToArray();
        // 0043 (Owner grant option) redefines it on top of 0042 and carries 0042's changes; OwnerGrantOptionMigrationTests
        // pins that. Any other later redefinition must carry them too, so it has to be added here on purpose.
        Assert.Equal(["0043_owner_grant_option.sql"], later.Select(x => x!));
        var latest = ProcedureBody(Read(later[^1]!));
        Assert.True(At(latest, "IF @loginExists=0 AND @active=1") < At(latest, "CREATE LOGIN ''+QUOTENAME(@identity)+N'' FROM WINDOWS"));
        Assert.Contains("IF @principal IS NULL AND @active=0", latest, StringComparison.Ordinal);
    }

    [Fact]
    public void A_login_is_created_only_to_give_access_never_to_take_it_away()
    {
        var body = ProcedureBody(Read(MigrationName));
        var guard = At(body, "IF @loginExists=0 AND @active=1");
        var create = At(body, "CREATE LOGIN ''+QUOTENAME(@identity)+N'' FROM WINDOWS");
        Assert.True(guard < create);
        Assert.Equal(1, body.Split("CREATE LOGIN").Length - 1);
        // A database user is likewise created only when access is being given.
        Assert.True(At(body, "IF @principal IS NULL AND @active=1") < At(body, "CREATE USER ''"));
    }

    [Fact]
    public void An_account_windows_cannot_resolve_is_found_by_name_only_when_deactivating()
    {
        var body = ProcedureBody(Read(MigrationName));
        var bySid = At(body, "WHERE sid=SUSER_SID(@identity)");
        var byName = At(body, "IF @principal IS NULL AND @active=0");
        Assert.True(bySid < byName);
        Assert.Contains("WHERE name=@identity AND type IN (''U'',''G'',''S'')", body, StringComparison.Ordinal);
        // The memberships block runs for any principal that was found, by SID or by name.
        Assert.Contains("IF @principal IS NOT NULL AND @principal<>N''dbo''", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_server_permission_is_changed_only_for_a_login_that_exists()
    {
        var body = ProcedureBody(Read(MigrationName));
        Assert.True(At(body, "IF @loginExists=1") < At(body, "ALTER ANY LOGIN TO"));
        Assert.True(At(body, "SET @loginExists=1;") < At(body, "IF @loginExists=1"));
    }

    [Fact]
    public void The_last_active_owner_is_refused_before_any_permission_changes()
    {
        var body = ProcedureBody(Read(MigrationName));
        var guard = At(body, "THROW 51230,''Keep at least one active Owner. Add another Owner before changing this account.'',1;");
        Assert.True(At(body, "IF (@active=0 OR @role<>''OWNER'')") < guard);
        Assert.Contains("windows_identity<>@identity", body, StringComparison.Ordinal);
        Assert.True(guard < At(body, "CREATE LOGIN"));
        Assert.True(guard < At(body, "EXEC(@membership);"));
        Assert.True(guard < At(body, "EXEC(@serverPermission);"));
        // The Owner check still comes first: a non-Owner learns nothing about who the Owners are.
        Assert.True(At(body, "THROW 51312,''Owner permission is required to manage access.''") < guard);
    }

    [Fact]
    public void An_unknown_windows_account_gets_an_actionable_refusal_when_access_is_given()
    {
        var body = ProcedureBody(Read(MigrationName));
        Assert.True(At(body, "IF ERROR_NUMBER()=15401") < At(body, "THROW 51471,@notFound,1;"));
        Assert.Contains("it can only be deactivated", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_role_and_permission_block_is_byte_for_byte_the_one_0036_installed()
    {
        // Only how the principal is found and when logins are touched changed; what an active
        // or inactive user is given or denied must not drift.
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
    public void Deactivation_still_denies_connect_and_drops_every_role()
    {
        var body = ProcedureBody(Read(MigrationName));
        Assert.Contains("CASE WHEN @active=1 THEN N''GRANT CONNECT TO '' ELSE N''DENY CONNECT TO '' END", body, StringComparison.Ordinal);
        foreach (var role in new[] { "etp_owner", "etp_store_manager", "etp_viewer", "etp_automation", "db_owner", "db_datawriter", "db_datareader", "db_backupoperator" })
            Assert.Contains($"ALTER ROLE {role} DROP MEMBER", body, StringComparison.Ordinal);
    }
}
