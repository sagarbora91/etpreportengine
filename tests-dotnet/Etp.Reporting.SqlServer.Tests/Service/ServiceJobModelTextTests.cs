using System.Text.Json;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service UI 1.10.0 (decision 25), lane sql: migration 0050 holds only CREATE OR ALTER VIEW through EXEC and the grants
/// and denies of every view, names no phone, e-mail or address column, never selects *, reads only landing tables, the
/// 0048 views and the import catalogue, and leaves 0048 alone. The SQL behaviour is ServiceJobModelSqlTests (SQL suite).
/// </summary>
public sealed partial class ServiceJobModelTextTests
{
    private const string MigrationName = "0050_service_centre_ui.sql";
    private const string SectionName = "D_SERVICE_UI_READ";

    private static readonly string[] Views =
    [
        "v_service_status_view_facts", "v_service_claims", "v_service_job", "v_service_job_timeline", "v_service_parts",
        "v_service_parts_transit", "v_service_stock_summary", "v_service_pending_current",
    ];

    // The 0048 read views 0050 may build on (section C of 0048).
    private static readonly string[] Views0048 =
    [
        "v_service_families", "v_service_reading_windows", "v_service_readings", "v_service_datelog_readings", "v_service_status_view_rows",
        "v_service_job_readings", "v_service_job_status_current", "v_service_pending_current", "v_service_job_list_events",
        "v_service_s004_daily", "v_service_money_changes", "v_service_gprc_claims", "v_service_manual_money",
    ];

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", MigrationName)).Replace("\r\n", "\n");

    private static string Section(string script)
    {
        var beginMarker = $"-- >>> {SectionName} begin\n";
        var begin = script.IndexOf(beginMarker, StringComparison.Ordinal);
        var end = script.IndexOf($"-- <<< {SectionName} end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, $"Section {SectionName} is missing or its markers are out of order.");
        return script[(begin + beginMarker.Length)..end];
    }

    private static List<(string Name, string Body)> ViewBlocks(string section) =>
        ViewBlock().Matches(section).Select(match => (match.Groups["name"].Value, match.Groups["body"].Value)).ToList();

    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [Fact]
    public void The_migration_is_the_only_0050_with_one_section_and_nothing_but_SET_XACT_ABORT_outside_it()
    {
        var directory = Path.Combine(RepositoryRoot(), "database", "migrations");
        Assert.Equal([MigrationName], Directory.GetFiles(directory, "0050_*.sql").Select(Path.GetFileName));
        var script = Script();
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        var section = Section(script);
        var outside = script.Replace(section, "");
        Assert.Equal(["SET XACT_ABORT ON;"], Code(outside).Where(line => !line.StartsWith("-- ", StringComparison.Ordinal)));
        Assert.StartsWith("SET XACT_ABORT ON;", Code(script).First(), StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_holds_only_view_definitions_through_EXEC_and_permission_statements()
    {
        var section = Section(Script());
        foreach (var line in Code(ViewBlock().Replace(section, "")))
            Assert.Matches(PermissionStatement(), line);
        var views = ViewBlocks(section);
        Assert.Equal(Views, views.Select(view => view.Name));
        Assert.DoesNotMatch(new Regex(@"\b(CREATE|ALTER|DROP)\s+(TABLE|PROC|PROCEDURE|TRIGGER|INDEX|FUNCTION|SCHEMA|USER|ROLE)\b", RegexOptions.IgnoreCase), section);
        Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\s+(INTO\s+)?(dbo\.|\[)", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void Every_view_is_granted_to_the_three_roles_and_denied_writes()
    {
        var section = Section(Script());
        foreach (var view in Views)
        {
            Assert.Single(Regex.Matches(section, Regex.Escape($"GRANT SELECT ON dbo.{view} TO etp_viewer,etp_store_manager,etp_owner;")));
            Assert.Single(Regex.Matches(section, Regex.Escape($"DENY INSERT,UPDATE,DELETE ON dbo.{view} TO etp_store_manager,etp_viewer;")));
        }
    }

    [Fact]
    public void Each_view_quotes_its_text_and_uses_only_0048_views_or_views_created_before_it()
    {
        var created = new HashSet<string>(Views0048, StringComparer.Ordinal);
        foreach (var (name, body) in ViewBlocks(Section(Script())))
        {
            Assert.DoesNotContain("'", body.Replace("''", ""), StringComparison.Ordinal);
            foreach (Match used in Regex.Matches(body, @"\bdbo\.(v_service_\w+)"))
                Assert.True(used.Groups[1].Value != name && created.Contains(used.Groups[1].Value),
                    $"{name} uses dbo.{used.Groups[1].Value} before it is created.");
            created.Add(name);
            // Only Service landing tables, the 0048/0050 views and the import catalogue.
            foreach (Match table in Regex.Matches(body, @"\bdbo\.(\w+)"))
                Assert.Matches(@"^(etp_landing_s0\d\d|v_service_\w+|import_files|import_batches)$", table.Groups[1].Value);
        }
    }

    [Fact]
    public void No_view_and_no_query_names_a_phone_e_mail_or_address_column_or_selects_star()
    {
        var restricted = RestrictedColumns();
        Assert.NotEmpty(restricted);
        var script = Script();
        var queries = string.Join('\n', SqlServerServiceReportQuery.UiStatements);
        foreach (var column in restricted)
        {
            var name = new Regex($@"(?<![\w]){Regex.Escape(column)}(?![\w])", RegexOptions.IgnoreCase);
            Assert.False(name.IsMatch(script), $"0050 names the restricted column {column}.");
            Assert.False(name.IsMatch(queries), $"The Service UI query names the restricted column {column}.");
        }
        Assert.DoesNotMatch(new Regex(@"\bSELECT\s+(\w+\.)?\*\s+FROM\s+dbo\.", RegexOptions.IgnoreCase), script);
        Assert.DoesNotMatch(new Regex(@"\bSELECT\s+\*", RegexOptions.IgnoreCase), queries);
        // f.* in v_service_job and the pending view's union of its own CTEs are not landing-table stars.
        Assert.DoesNotMatch(new Regex(@"\*\s+FROM\s+dbo\.etp_landing", RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_query_reads_only_service_views_and_never_writes()
    {
        foreach (var sql in SqlServerServiceReportQuery.UiStatements)
        {
            foreach (Match used in Regex.Matches(sql, @"\bdbo\.(\w+)"))
                Assert.True(Views.Contains(used.Groups[1].Value) || Views0048.Contains(used.Groups[1].Value), $"The query reads dbo.{used.Groups[1].Value}.");
            Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC)\b", RegexOptions.IgnoreCase), sql);
        }
        // Every column the job reader maps is a column the view selects, in the same order.
        var job = ViewBlocks(Section(Script())).Single(view => view.Name == "v_service_job").Body;
        var finalSelect = job[job.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        var position = 0;
        foreach (var column in SqlServerServiceReportQuery.JobColumns.Split(','))
        {
            var found = Regex.Match(finalSelect[position..], $@"(\b{column}|a\.{column}),?\s*\n?");
            Assert.True(found.Success, $"v_service_job does not select {column} after the previous column.");
            position += found.Index + found.Length;
        }
    }

    [Fact]
    public void The_pending_view_keeps_the_0048_columns_first_and_applies_the_SRN_closure_rule()
    {
        var pending = ViewBlocks(Section(Script())).Single(view => view.Name == "v_service_pending_current").Body;
        Assert.Contains("SELECT f.pending_list [list],l.report_code,f.list_label,l.job_order_number,l.job_date,", pending, StringComparison.Ordinal);
        Assert.Contains("l.brand,l.model,l.customer_name,l.pending_store,l.snapshot_date,l.import_file_id,\n  l.edd,l.jo_status,l.spare_required,l.indent_date,l.repair_date,l.srn_to_status", pending, StringComparison.Ordinal);
        // Q5: closed when srn_received_date or repaired_date is set or to_status contains Received.
        Assert.Contains("AND s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%''", pending, StringComparison.Ordinal);
        Assert.Contains("SELECT [list],job_order_number,job_date,age_days,brand,model,customer_name,pending_store,snapshot_date", SqlServerServiceReportQuery.PendingSql, StringComparison.Ordinal);
    }

    [Fact]
    public void Claims_take_each_document_from_the_new_header_family_and_keep_the_GPRC_union()
    {
        var claims = ViewBlocks(Section(Script())).Single(view => view.Name == "v_service_claims").Body;
        foreach (var (old, @new) in new[] { ("wdc_old", "wdc_new"), ("wra_old", "wra_new") })
        {
            Assert.Contains($"FROM {old} h\nWHERE NOT EXISTS(SELECT 1 FROM {@new} c WHERE c.document_number=h.document_number)\n  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM {@new} c WHERE c.business_date=h.business_date))", claims, StringComparison.Ordinal);
        }
        Assert.Contains("FROM dbo.v_service_gprc_claims g", claims, StringComparison.Ordinal);
        foreach (var code in new[] { "S024", "S025", "S026", "S039", "S040" })
            Assert.Contains($"w.report_code=''{code}'' AND w.reading_rank=1", claims, StringComparison.Ordinal);
        Assert.Equal(["GPRC", "MB", "WDC", "WRA"], Regex.Matches(claims, @"SELECT ''(GPRC|MB|WDC|WRA)''").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(ServiceClaimTypes.All.Order(StringComparer.Ordinal), new[] { "GPRC", "MB", "WDC", "WRA" });
    }

    [Fact]
    public void An_old_WRA_line_that_carries_a_WDC_document_is_counted_only_as_WDC()
    {
        // Sagar, 10 Oct 2026: 28 S026 lines on live repeat WDC document numbers; they are WDC claims, not WRA claims.
        var claims = ViewBlocks(Section(Script())).Single(view => view.Name == "v_service_claims").Body;
        var wraOld = claims[claims.IndexOf("FROM wra_old h", StringComparison.Ordinal)..];
        Assert.Contains("AND NOT EXISTS(SELECT 1 FROM wdc_old c WHERE c.document_number=h.document_number)", wraOld, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.document_number=h.document_number)", wraOld, StringComparison.Ordinal);
        var wdcOld = claims[claims.IndexOf("FROM wdc_old h", StringComparison.Ordinal)..claims.IndexOf("FROM wra_new", StringComparison.Ordinal)];
        Assert.DoesNotContain("wra_old", wdcOld, StringComparison.Ordinal);
    }

    [Fact]
    public void The_job_view_states_the_0048_live_reading_filter_and_every_stage_of_the_contract()
    {
        var job = ViewBlocks(Section(Script())).Single(view => view.Name == "v_service_job").Body;
        Assert.Contains("WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Completed''", job, StringComparison.Ordinal);
        var windows = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", "0048_service_centre_interim.sql")).Replace("\r\n", "\n");
        var codes0048 = Regex.Match(windows, @"AND f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value;
        var codes0050 = Regex.Match(job, @"AND f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value;
        Assert.Equal(codes0048, codes0050);
        foreach (var stage in ServiceStages.Order)
            Assert.Contains($"''{stage}''", job, StringComparison.Ordinal);
        Assert.Equal(ServiceStages.Order, ServiceJobStages.InOrder);
        // No join explosion: one aggregate over a union, no LEFT JOIN in the final select.
        var finalSelect = job[job.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        Assert.DoesNotContain("LEFT JOIN", finalSelect, StringComparison.Ordinal);
        Assert.Contains("FROM agg a\nCROSS JOIN snap", finalSelect, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("JOAW330252600001", true)]   // FY code 2526 + 5-digit sequence (16 characters)
    [InlineData("JOAW33000001234", true)]    // FY 2024-25 numbering (15 characters)
    [InlineData("  JOAW330262700042 ", true)] // trimmed, never padded
    [InlineData("JOAW330SYN0001", false)]    // the synthetic fixture shape: still a key, not the live shape
    [InlineData("JOAW3302526000011", false)]
    [InlineData("SAMPLE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_job_key_shape_is_JOAW330_and_8_or_9_digits(string? key, bool wellFormed)
    {
        Assert.Equal(@"^JOAW330[0-9]{8,9}$", ServiceJobKey.Pattern);
        Assert.Equal(wellFormed, ServiceJobKey.IsWellFormed(key));
    }

    [Fact]
    public void Migration_0048_is_untouched_by_0050()
    {
        var script0048 = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", "0048_service_centre_interim.sql"));
        Assert.Contains("-- <<< C_SERVICE_READ end", script0048, StringComparison.Ordinal);
        Assert.DoesNotContain("v_service_job_timeline", script0048, StringComparison.Ordinal);
        Assert.DoesNotContain("D_SERVICE_UI_READ", script0048, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> RestrictedColumns()
    {
        using var spec = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "families.spec.json")));
        return spec.RootElement.EnumerateArray()
            .SelectMany(family => family.GetProperty("Columns").EnumerateArray())
            .Select(column => column.GetProperty("CanonicalField").GetString()!)
            .Where(name => RestrictedName().IsMatch(name))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex(@"mobile|phone(?!pe)|landline|e_?mail|address|contact", RegexOptions.IgnoreCase)]
    private static partial Regex RestrictedName();

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();

    [GeneratedRegex(@"^(GRANT SELECT ON dbo\.v_service_\w+ TO etp_viewer,etp_store_manager,etp_owner;|DENY INSERT,UPDATE,DELETE ON dbo\.v_service_\w+ TO etp_store_manager,etp_viewer;)$")]
    private static partial Regex PermissionStatement();
}
