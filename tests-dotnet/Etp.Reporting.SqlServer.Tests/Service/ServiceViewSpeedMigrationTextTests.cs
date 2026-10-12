using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.11.4: migration 0061 restates six Service views for speed and nothing else. It keeps the role rule of 0056/0057 on
/// v_service_job, repeats the earlier grants unchanged (so ExpectedRolePermissions.json does not change), adds no index or
/// column, and removes the shapes that made the old texts slow. Behaviour (same rows as the old texts, timing guard) is
/// ServiceViewSpeedSqlTests in the SQL suite.
/// </summary>
public sealed partial class ServiceViewSpeedMigrationTextTests
{
    private const string MigrationName = "0061_service_view_speed.sql";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Migrations() => Path.Combine(RepositoryRoot(), "database", "migrations");

    private static string Script() => File.ReadAllText(Path.Combine(Migrations(), MigrationName)).Replace("\r\n", "\n");

    private static Dictionary<string, string> Views() =>
        ViewBlock().Matches(Script()).ToDictionary(match => match.Groups["name"].Value, match => match.Groups["body"].Value, StringComparer.Ordinal);

    [Fact]
    public void The_migration_is_the_only_0061_restates_six_views_and_has_no_transaction_GO_index_or_table_change()
    {
        Assert.Equal([MigrationName], Directory.GetFiles(Migrations(), "0061_*.sql").Select(Path.GetFileName));
        var script = Script();
        Assert.Equal(
            ["v_service_claims", "v_service_datelog_readings", "v_service_job", "v_service_money_changes", "v_service_parts", "v_service_s004_daily"],
            Views().Keys.Order(StringComparer.Ordinal));
        Assert.Equal(6, Regex.Matches(script, @"CREATE OR ALTER VIEW").Count);
        Assert.DoesNotMatch(new Regex(@"^\s*(BEGIN\s+TRAN|COMMIT|ROLLBACK|GO\s*$)", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
        Assert.DoesNotMatch(new Regex(@"\b(CREATE\s+(UNIQUE\s+)?(NONCLUSTERED\s+)?INDEX|ALTER\s+TABLE|DROP\s+)", RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void Grants_are_the_earlier_ones_repeated_for_each_restated_view_and_nothing_more()
    {
        var lines = Script().Split('\n').Where(line => line.StartsWith("GRANT", StringComparison.Ordinal) || line.StartsWith("DENY", StringComparison.Ordinal)
            || line.StartsWith("REVOKE", StringComparison.Ordinal)).ToList();
        Assert.Equal(12, lines.Count);
        foreach (var view in Views().Keys)
        {
            Assert.Contains($"GRANT SELECT ON dbo.{view} TO etp_viewer,etp_store_manager,etp_owner;", lines);
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.{view} TO etp_store_manager,etp_viewer;", lines);
        }
    }

    [Fact]
    public void The_job_view_keeps_the_customer_name_role_rule_and_reads_the_name_without_a_final_join()
    {
        var job = Views()["v_service_job"];
        Assert.Contains("CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1\n       THEN a.nm_customer_name END customer_name", job, StringComparison.Ordinal);
        Assert.Contains("MAX(u.nm_customer_name) nm_customer_name", job, StringComparison.Ordinal);
        Assert.Contains(",CONVERT(nvarchar(200),customer_name) FROM names", job, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN names", job, StringComparison.Ordinal);
        Assert.DoesNotContain("HASH", job, StringComparison.Ordinal);
    }

    [Fact]
    public void The_slow_shapes_are_gone()
    {
        var views = Views();
        // A: the DateLog rule no longer expands the 36-table reading windows view.
        Assert.DoesNotContain("v_service_reading_windows", views["v_service_datelog_readings"], StringComparison.Ordinal);
        // B, D, F: the winning readings are hash-joined to the landing rows.
        Assert.Contains("FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s004 r", views["v_service_s004_daily"], StringComparison.Ordinal);
        Assert.Contains("FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s008 s", views["v_service_parts"], StringComparison.Ordinal);
        Assert.DoesNotContain("v_service_readings", views["v_service_parts"], StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(views["v_service_claims"], "INNER HASH JOIN dbo.etp_landing_s0").Count);
        // C: no self-join of the DateLog readings and no correlated TOP (1).
        var money = views["v_service_money_changes"];
        Assert.Single(Regex.Matches(money, "v_service_datelog_readings"));
        Assert.DoesNotContain("OUTER APPLY", money, StringComparison.Ordinal);
        Assert.DoesNotContain("TOP (1)", money, StringComparison.Ordinal);
        Assert.Contains("MAX(CASE WHEN reading_rank=2 THEN import_file_id END) OVER(PARTITION BY report_code,business_date) second_import_file_id", money, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();
}
