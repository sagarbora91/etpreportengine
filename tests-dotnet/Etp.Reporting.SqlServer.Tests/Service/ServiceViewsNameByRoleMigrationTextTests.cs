using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.11.1, lane name-privacy (decision 37 follow-up): migration 0057 restates the four older Service read views that return a
/// customer_name column (v_service_status_view_rows and v_service_job_status_current from 0048, v_service_status_view_facts
/// and v_service_pending_current from 0050) so the name follows 0056's role rule: Owner, Store Manager and db_owner see it,
/// a Viewer reads NULL. Each view is its latest earlier text with only the customer name expression wrapped in that rule; the
/// grants are repeated unchanged. The SQL behaviour per role is ServiceViewsNameByRoleSqlTests (SQL suite).
/// </summary>
public sealed partial class ServiceViewsNameByRoleMigrationTextTests
{
    private const string MigrationName = "0057_service_views_name_by_role.sql";
    private const string SectionName = "E_SERVICE_NAME_PRIVACY";

    // The role rule 0056 gives v_service_job, character for character (inside EXEC(N'...'), so the quotes are doubled).
    private const string Rule =
        "IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1";

    private const string StatusName = "CONVERT(nvarchar(200),customername)";

    // Per view: the migration its previous text comes from, the name expression there, what 0057 puts in its place, and
    // how many times (the two status-view unions have ten branches each).
    private static readonly (string View, string Source, string Old, string New, int Count)[] Restated =
    [
        ("v_service_status_view_rows", "0048_service_centre_interim.sql", StatusName, Masked(StatusName), 10),
        ("v_service_job_status_current", "0048_service_centre_interim.sql", "d.customer_name,", Masked("d.customer_name") + " customer_name,", 1),
        ("v_service_status_view_facts", "0050_service_centre_ui.sql", StatusName, Masked(StatusName), 10),
        ("v_service_pending_current", "0050_service_centre_ui.sql", "l.customer_name,", Masked("l.customer_name") + " customer_name,", 1),
    ];

    private static string Masked(string expression) => $"CASE WHEN {Rule} THEN {expression} END";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string MigrationsDirectory() => Path.Combine(RepositoryRoot(), "database", "migrations");

    private static string Read(string name) => File.ReadAllText(Path.Combine(MigrationsDirectory(), name)).Replace("\r\n", "\n");

    private static string Section(string script)
    {
        var beginMarker = $"-- >>> {SectionName} begin\n";
        var begin = script.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = script.IndexOf($"-- <<< {SectionName} end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, $"Section {SectionName} is missing or its markers are out of order.");
        return script[(begin + beginMarker.Length)..end];
    }

    private static string View(string script, string name)
    {
        var blocks = ViewBlock().Matches(script).Where(match => match.Groups["name"].Value == name).ToList();
        Assert.Single(blocks);
        return blocks[0].Groups["body"].Value;
    }

    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    // The latest definition of every view in the migrations up to and including `last`, by file order.
    private static Dictionary<string, (string File, string Body)> LatestViews(string last)
    {
        var latest = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(MigrationsDirectory(), "*.sql").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);
            if (string.CompareOrdinal(file, last) > 0) break;
            var script = File.ReadAllText(path).Replace("\r\n", "\n");
            var blocks = AnyViewBlock().Matches(script);
            // Every CREATE VIEW of the file is one parsed block, so no view escapes the scan.
            Assert.True(Regex.Matches(script, @"CREATE (OR ALTER )?VIEW dbo\.\w+ AS").Count == blocks.Count, $"{file}: a view was not parsed.");
            foreach (Match block in blocks) latest[block.Groups["name"].Value] = (file, block.Groups["body"].Value);
        }
        return latest;
    }

    [Fact]
    public void The_migration_is_the_only_0057_and_holds_one_section_without_transactions_or_GO()
    {
        Assert.Equal([MigrationName], Directory.GetFiles(MigrationsDirectory(), "0057_*.sql").Select(Path.GetFileName));
        var script = Read(MigrationName);
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        Assert.Equal(["SET XACT_ABORT ON;"], Code(script.Replace(Section(script), "")));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_restates_only_the_four_views_and_repeats_their_grants_so_role_permissions_do_not_change()
    {
        var section = Section(Read(MigrationName));
        Assert.Equal(Restated.Select(view => view.View), ViewBlock().Matches(section).Select(match => match.Groups["name"].Value));
        var grants = Restated.SelectMany(view => new[]
        {
            $"GRANT SELECT ON dbo.{view.View} TO etp_viewer,etp_store_manager,etp_owner;",
            $"DENY INSERT,UPDATE,DELETE ON dbo.{view.View} TO etp_store_manager,etp_viewer;",
        }).ToList();
        Assert.Equal(grants, Code(ViewBlock().Replace(section, "")));
        // The same grants the source migrations issued.
        foreach (var view in Restated)
        {
            var source = Read(view.Source);
            Assert.Contains($"\nGRANT SELECT ON dbo.{view.View} TO etp_viewer,etp_store_manager,etp_owner;\n", source, StringComparison.Ordinal);
            Assert.Contains($"\nDENY INSERT,UPDATE,DELETE ON dbo.{view.View} TO etp_store_manager,etp_viewer;\n", source, StringComparison.Ordinal);
        }
        Assert.DoesNotMatch(new Regex(@"\b(DROP|TRUNCATE|DELETE\s+FROM|REVOKE|ALTER\s+ROLE|ALTER\s+TABLE)\b", RegexOptions.IgnoreCase), string.Join("\n", Code(section)));
        // No SELECT * from a table or view. 0050's v_service_pending_current (restated verbatim) does
        // "SELECT * FROM state_lists UNION ALL SELECT * FROM srn_lists": both are CTEs of the same view, which is allowed.
        foreach (Match star in new Regex(@"\bSELECT\s+(\w+\.)?\*\s+FROM\s+(?<target>[\w.\[\]]+)", RegexOptions.IgnoreCase).Matches(section))
        {
            var target = star.Groups["target"].Value;
            Assert.Matches(new Regex($@"[\s,]{Regex.Escape(target)}\s+AS\s*\(", RegexOptions.IgnoreCase), section);
        }
        // v_service_job stays 0056's.
        Assert.DoesNotMatch(new Regex(@"VIEW\s+dbo\.v_service_job\s+AS", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void Each_view_is_its_previous_text_with_only_the_customer_name_given_by_role()
    {
        var before = LatestViews("0056_service_job_customer_name.sql");
        var script = Read(MigrationName);
        foreach (var (view, source, old, replacement, count) in Restated)
        {
            // The text 0057 restates is still the latest one before it.
            Assert.Equal(source, before[view].File);
            var body = View(script, view);
            Assert.Equal(count, Regex.Matches(body, Regex.Escape(replacement)).Count);
            Assert.Equal(count, Regex.Matches(body, @"IS_MEMBER\(N''etp_owner''\)").Count);
            Assert.Equal(before[view].Body, body.Replace(replacement, old));
        }
    }

    [Fact]
    public void The_role_rule_is_0056s_and_never_names_the_viewer_or_a_contact_column()
    {
        // 0056's v_service_job carries the rule exactly once, before its name column.
        var job = View(Read("0056_service_job_customer_name.sql"), "v_service_job");
        Assert.Contains($"CASE WHEN {Rule}\n       THEN nm.customer_name END customer_name,", job, StringComparison.Ordinal);
        var section = Section(Read(MigrationName));
        var bodies = string.Join("\n", ViewBlock().Matches(section).Select(match => match.Groups["body"].Value));
        Assert.DoesNotContain("etp_viewer", bodies, StringComparison.Ordinal);
        // Every IS_MEMBER call belongs to a whole copy of the rule: 22 copies, three calls each.
        Assert.Equal(22, Regex.Matches(bodies, Regex.Escape(Rule)).Count);
        Assert.Equal(66, Regex.Matches(bodies, @"IS_MEMBER\(").Count);
        foreach (var word in new[] { "mobile", "phone", "landline", "mail", "address", "contact" })
            Assert.DoesNotContain(word, bodies, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void After_0057_every_view_that_selects_a_customer_name_gives_it_by_role()
    {
        // Decision 37: no view gives a Viewer a customer name. The scan covers every view of every migration, so a view added
        // later that exposes a name without the rule fails here.
        var latest = LatestViews(MigrationName);
        var named = latest.Where(view => Regex.IsMatch(view.Value.Body, @"customer_?name|party_?name|cust_?name", RegexOptions.IgnoreCase))
            .Select(view => view.Key).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["v_service_job", "v_service_job_status_current", "v_service_pending_current", "v_service_status_view_facts", "v_service_status_view_rows"],
            named);
        foreach (var view in named) Assert.Contains(Rule, latest[view].Body, StringComparison.Ordinal);
        Assert.Equal("0056_service_job_customer_name.sql", latest["v_service_job"].File);
        Assert.All(Restated, view => Assert.Equal(MigrationName, latest[view.View].File));
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();

    [GeneratedRegex(@"EXEC\(N'CREATE (OR ALTER )?VIEW dbo\.(?<name>\w+) AS\s*\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex AnyViewBlock();
}
