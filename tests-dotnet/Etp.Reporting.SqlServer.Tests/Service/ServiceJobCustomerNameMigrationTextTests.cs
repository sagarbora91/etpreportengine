using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.11.1, lane customer-name (decision 37, finding R-UI-21): migration 0056 restates v_service_job (0052 shipped and is
/// checksummed) so the Jobs list and the Job history header carry the customer NAME of the job's most recent reading, shown
/// to Owner and Store Manager and NULL for a Viewer. Nothing else in the view changes; no phone, e-mail or address column;
/// the grants are 0052's, repeated. The SQL behaviour is ServiceJobModelSqlTests (SQL suite).
/// </summary>
public sealed partial class ServiceJobCustomerNameMigrationTextTests
{
    private const string MigrationName = "0056_service_job_customer_name.sql";
    private const string SectionName = "E_SERVICE_CUSTOMER_NAME";

    // The 0052 text 0056 replaces, and what it puts there.
    private const string NameCtesStart = "\n), name_rows AS (\n";
    private const string FactsByJob = "\n), facts_by_job AS (\n";
    private const string OldColumn = "  COALESCE(a.sv_customer_name,a.st9_customer_name,a.srn_customer_name) customer_name,\n";
    private const string NewColumn =
        "  CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1\n" +
        "       THEN nm.customer_name END customer_name,\n";
    private const string NamesJoin = "LEFT JOIN names nm ON nm.job_order_number=a.job_order_number\n";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", name)).Replace("\r\n", "\n");

    private static string Section(string script)
    {
        var beginMarker = $"-- >>> {SectionName} begin\n";
        var begin = script.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = script.IndexOf($"-- <<< {SectionName} end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, $"Section {SectionName} is missing or its markers are out of order.");
        return script[(begin + beginMarker.Length)..end];
    }

    private static string JobView(string script)
    {
        var blocks = ViewBlock().Matches(script).Where(match => match.Groups["name"].Value == "v_service_job").ToList();
        Assert.Single(blocks);
        return blocks[0].Groups["body"].Value;
    }

    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [Fact]
    public void The_migration_is_the_only_0056_and_holds_one_section_without_transactions_or_GO()
    {
        var directory = Path.Combine(RepositoryRoot(), "database", "migrations");
        Assert.Equal([MigrationName], Directory.GetFiles(directory, "0056_*.sql").Select(Path.GetFileName));
        var script = Read(MigrationName);
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        Assert.Equal(["SET XACT_ABORT ON;"], Code(script.Replace(Section(script), "")));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_restates_only_v_service_job_and_repeats_0052s_grants_so_role_permissions_do_not_change()
    {
        var section = Section(Read(MigrationName));
        Assert.Equal(["v_service_job"], ViewBlock().Matches(section).Select(match => match.Groups["name"].Value));
        Assert.Equal(
            [
                "GRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;",
                "DENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;",
            ],
            Code(ViewBlock().Replace(section, "")));
        var section0052 = Read("0052_service_ui_fixes.sql");
        Assert.Contains("\nGRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;\n", section0052, StringComparison.Ordinal);
        Assert.Contains("\nDENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;\n", section0052, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(DROP|TRUNCATE|DELETE\s+FROM|REVOKE|ALTER\s+ROLE|ALTER\s+TABLE)\b", RegexOptions.IgnoreCase), string.Join("\n", Code(section)));
        Assert.DoesNotMatch(new Regex(@"\bSELECT\s+(\w+\.)?\*\s+FROM\b", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void The_job_view_is_0052s_text_except_the_name_ctes_the_role_rule_and_the_names_join()
    {
        var view = JobView(Read(MigrationName));
        Assert.Single(Regex.Matches(view, Regex.Escape(NameCtesStart)));
        Assert.Single(Regex.Matches(view, Regex.Escape(NewColumn)));
        Assert.Single(Regex.Matches(view, Regex.Escape("FROM agg a\nCROSS JOIN snap\n" + NamesJoin)));
        var start = view.IndexOf(NameCtesStart, StringComparison.Ordinal);
        var end = view.IndexOf(FactsByJob, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "name_rows / names must sit just before facts_by_job.");
        var reverted = (view[..start] + view[end..]).Replace(NewColumn, OldColumn).Replace(NamesJoin, "");
        Assert.Equal(JobView(Read("0052_service_ui_fixes.sql")), reverted);
    }

    [Fact]
    public void Every_landing_family_with_a_customer_name_feeds_the_name_and_the_most_recent_reading_wins()
    {
        var view = JobView(Read(MigrationName));
        var ctes = view[view.IndexOf(NameCtesStart, StringComparison.Ordinal)..view.IndexOf(FactsByJob, StringComparison.Ordinal)];
        // The ten status views (S014-S018, S031-S035) come through v_service_status_view_facts, the others from their tables.
        Assert.Contains("  SELECT report_code,import_file_id,job_order_number,customer_name FROM dbo.v_service_status_view_facts\n", ctes, StringComparison.Ordinal);
        var tables = Regex.Matches(ctes, @"FROM dbo\.etp_landing_(s\d{3})\b").Select(match => match.Groups[1].Value.ToUpperInvariant()).ToList();
        var statusViews = new[] { "S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035" };
        // Every 0048 landing table with a customer name column, read from the 0048 text.
        var landing = Read("0048_service_centre_interim.sql");
        var withName = Regex.Matches(landing, @"CREATE TABLE dbo\.\[etp_landing_(?<code>s\d{3})\](?<body>.*?)\n\);", RegexOptions.Singleline)
            .Where(match => Regex.IsMatch(match.Groups["body"].Value, @"\[(customername|customer_name)\]"))
            .Select(match => match.Groups["code"].Value.ToUpperInvariant())
            .ToList();
        Assert.Equal(withName.Order(StringComparer.Ordinal), tables.Concat(statusViews).Order(StringComparer.Ordinal));
        // Each branch is tagged with its own family code.
        foreach (var code in tables)
            Assert.Matches(new Regex($@"SELECT ''{code}'',import_file_id,NULLIF\(LTRIM\(RTRIM\(CONVERT\(nvarchar\(100\),\w+\)\)\),N''''\),CONVERT\(nvarchar\(200\),(customername|customer_name)\) FROM dbo\.etp_landing_{code.ToLowerInvariant()}\n"), ctes);
        // Live readings only, blank names skipped, latest snapshot then later import wins.
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY n.job_order_number ORDER BY l.snapshot_date DESC,n.import_file_id DESC,n.customer_name DESC) name_rank", ctes, StringComparison.Ordinal);
        Assert.Contains("NULLIF(LTRIM(RTRIM(customer_name)),N'''') customer_name FROM name_rows) n", ctes, StringComparison.Ordinal);
        Assert.Contains("JOIN live l ON l.import_file_id=n.import_file_id AND l.report_code=n.report_code", ctes, StringComparison.Ordinal);
        Assert.Contains("WHERE n.job_order_number IS NOT NULL AND n.customer_name IS NOT NULL", ctes, StringComparison.Ordinal);
        Assert.Contains(") x WHERE x.name_rank=1", ctes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_is_given_by_role_and_no_phone_email_or_address_column_is_read()
    {
        var view = JobView(Read(MigrationName));
        Assert.Contains(NewColumn, view, StringComparison.Ordinal);
        // etp_viewer is never in the rule: a Viewer reads NULL.
        Assert.DoesNotContain("IS_MEMBER(N''etp_viewer'')", view, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(view, @"IS_MEMBER\(").Count);
        foreach (var word in new[] { "mobile", "phone", "landline", "mail", "address", "contact" })
            Assert.DoesNotContain(word, view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_query_reads_the_job_columns_in_the_order_0056_selects_them()
    {
        var view = JobView(Read(MigrationName));
        var finalSelect = view[view.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        var position = 0;
        foreach (var column in SqlServerServiceReportQuery.JobColumns.Split(','))
        {
            var found = Regex.Match(finalSelect[position..], $@"(\b{column}|a\.{column}),?\s*\n?");
            Assert.True(found.Success, $"v_service_job does not select {column} after the previous column.");
            position += found.Index + found.Length;
        }
        Assert.Contains("THEN nm.customer_name END customer_name,", finalSelect, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();
}
