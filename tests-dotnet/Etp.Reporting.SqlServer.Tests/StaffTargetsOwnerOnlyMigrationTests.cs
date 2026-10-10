namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.10.0, migration 0051 (decision 27, Sagar, 10 Oct 2026, extends D22): staff targets are
/// Owner-only in SQL too. These tests read the shipped migrations; the SQL behaviour is covered
/// by StaffTargetsOwnerOnlyTests in the SQL integration suite.
/// </summary>
public sealed class StaffTargetsOwnerOnlyMigrationTests
{
    private const string MigrationName = "0051_staff_targets_owner_only.sql";

    private static string MigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "database", "migrations");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string[] Statements(string migration) => migration.Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal))
        .ToArray();

    [Fact]
    public void The_migration_takes_back_the_0022_grant_and_denies_writes_to_store_managers_and_viewers()
    {
        var names = Directory.GetFiles(MigrationsDirectory(), "*.sql").Select(Path.GetFileName).ToArray();
        Assert.Contains(MigrationName, names);
        var migration = Read(MigrationName);
        Assert.DoesNotContain("\nGO\n", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                "SET XACT_ABORT ON;",
                "REVOKE INSERT,UPDATE ON dbo.staff_sales_targets FROM etp_store_manager;",
                "DENY INSERT,UPDATE,DELETE ON dbo.staff_sales_targets TO etp_store_manager,etp_viewer;",
            ],
            Statements(migration));
        // The grant it takes back is the one 0022 shipped, which stays untouched.
        Assert.Contains("\nGRANT INSERT,UPDATE ON dbo.staff_sales_targets TO etp_store_manager;\n",
            Read("0022_least_privilege_audit.sql"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_owner_and_the_history_table_are_not_touched()
    {
        var statements = string.Join("\n", Statements(Read(MigrationName)));
        Assert.DoesNotContain("etp_owner", statements, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("db_owner", statements, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("staff_sales_target_history", statements, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRIGGER", statements, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_later_migration_gives_a_non_owner_role_write_access_to_staff_targets_again()
    {
        foreach (var path in Directory.GetFiles(MigrationsDirectory(), "*.sql")
                     .Where(path => string.CompareOrdinal(Path.GetFileName(path), MigrationName) > 0))
        {
            foreach (var statement in Statements(Read(Path.GetFileName(path)))
                         .Where(line => line.Contains("staff_sales_targets", StringComparison.OrdinalIgnoreCase)))
            {
                Assert.False(statement.StartsWith("GRANT", StringComparison.OrdinalIgnoreCase)
                    || statement.StartsWith("REVOKE", StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(path)} changes staff target permissions again: {statement}");
            }
        }
    }
}
