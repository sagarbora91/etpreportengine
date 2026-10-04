using System.Text.Json;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service interim (S-2, decision 15), lane L4: section C_SERVICE_READ of migration 0048 holds the read-rule views and
/// nothing else, exposes no phone, e-mail or address column, and reads each family by the column
/// ServiceInterimFamilies.ReadRules names. These tests read the shipped script and the query class; the views'
/// behaviour is covered by ServiceReadModelSqlTests in the SQL integration suite.
/// </summary>
public sealed partial class ServiceReadModelTextTests
{
    private const string MigrationName = "0048_service_centre_interim.sql";
    private const string SectionName = "C_SERVICE_READ";

    // The view names L7 documents (design section 5); the helpers are listed after them.
    private static readonly string[] ContractViews =
    [
        "v_service_readings", "v_service_job_readings", "v_service_job_status_current", "v_service_pending_current",
        "v_service_job_list_events", "v_service_s004_daily", "v_service_money_changes",
    ];
    private static readonly string[] HelperViews =
        ["v_service_families", "v_service_reading_windows", "v_service_datelog_readings", "v_service_status_view_rows"];
    // Lane L10's views (decision 16), emitted verbatim from c_service_read_gprc_and_money.sql.
    private static readonly string[] L10Views = ["v_service_gprc_claims", "v_service_manual_money"];

    private static readonly string[] Roles = ["etp_viewer", "etp_store_manager", "etp_owner"];

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

    // Each EXEC(N'CREATE OR ALTER VIEW dbo.name AS ...'); block, by view name, in script order.
    private static List<(string Name, string Body)> Views(string section) =>
        ViewBlock().Matches(section).Select(match => (match.Groups["name"].Value, match.Groups["body"].Value)).ToList();

    // The section with its view blocks and comment lines removed: what is left must be permission statements only.
    private static IEnumerable<string> Remainder(string section) =>
        ViewBlock().Replace(section, "").Split('\n').Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [Fact]
    public void Section_C_holds_only_view_definitions_through_EXEC_and_permission_statements()
    {
        var section = Section(Script());
        Assert.DoesNotContain("BEGIN TRAN", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COMMIT", section, StringComparison.OrdinalIgnoreCase);
        foreach (var line in Remainder(section))
            Assert.Matches(PermissionStatement(), line);
        var views = Views(section);
        Assert.Equal(ContractViews.Concat(HelperViews).Concat(L10Views).Order(StringComparer.Ordinal), views.Select(view => view.Name).Order(StringComparer.Ordinal));
        Assert.Equal(views.Count, views.Select(view => view.Name).Distinct(StringComparer.Ordinal).Count());
        // Nothing but views: no table, procedure, trigger, index or data change inside a block or around it.
        Assert.DoesNotMatch(new Regex(@"\b(CREATE|ALTER|DROP)\s+(TABLE|PROC|PROCEDURE|TRIGGER|INDEX|FUNCTION)\b", RegexOptions.IgnoreCase), section);
        Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\s+(INTO\s+)?dbo\.", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void Every_view_is_granted_to_the_three_roles_and_denied_writes()
    {
        var section = Section(Script());
        foreach (var (name, _) in Views(section))
        {
            Assert.Contains($"GRANT SELECT ON dbo.{name} TO {string.Join(',', Roles)};", section, StringComparison.Ordinal);
            Assert.Contains($"DENY INSERT,UPDATE,DELETE ON dbo.{name} TO etp_store_manager,etp_viewer;", section, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Each_view_quotes_its_text_correctly_and_uses_only_views_created_before_it()
    {
        var views = Views(Section(Script()));
        var created = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, body) in views)
        {
            // Inside N'...' every quote is doubled; an odd one would end the string early.
            Assert.DoesNotContain("'", body.Replace("''", ""), StringComparison.Ordinal);
            foreach (Match used in Regex.Matches(body, @"\bdbo\.(v_service_\w+)"))
                Assert.True(created.Contains(used.Groups[1].Value), $"{name} uses dbo.{used.Groups[1].Value} before it is created.");
            created.Add(name);
        }
    }

    [Fact]
    public void Views_read_only_importable_landing_tables_and_live_readings()
    {
        var section = Section(Script());
        var tables = Regex.Matches(section, @"\bdbo\.etp_landing_(s\d{3})\b").Select(match => match.Groups[1].Value.ToUpperInvariant()).ToHashSet();
        Assert.Equal(ServiceInterimFamilies.Importable.Order(StringComparer.Ordinal), tables.Order(StringComparer.Ordinal));
        var windows = Views(section).Single(view => view.Name == "v_service_reading_windows").Body;
        Assert.Contains("f.is_superseded=0", windows, StringComparison.Ordinal);
        Assert.Contains("f.data_truth_version=1", windows, StringComparison.Ordinal);
        Assert.Contains("b.status=''Completed''", windows, StringComparison.Ordinal);
        var listed = Regex.Match(windows, @"f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value;
        Assert.Equal(ServiceInterimFamilies.Importable.Order(StringComparer.Ordinal),
            Regex.Matches(listed, @"S\d{3}").Select(match => match.Value).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Each_family_is_read_by_the_column_its_read_rule_names()
    {
        var views = Views(Section(Script())).ToDictionary(view => view.Name, view => view.Body, StringComparer.Ordinal);
        var families = views["v_service_families"];
        foreach (var rule in ServiceInterimFamilies.ReadRules.Values)
        {
            var kind = Regex.Match(families, $@"\(''{rule.ReportCode}'',''(?<kind>\w+)''").Groups["kind"].Value;
            Assert.Equal(rule.Kind.ToString(), kind);
            if (rule.Kind == ServiceReadRuleKind.DateLog)
            {
                // The window (least and greatest row date) and the dated rows both use the rule's date column.
                Assert.Matches($@"SELECT ''{rule.ReportCode}''[^\n]*MIN\({rule.DateColumn!.CanonicalField}\)[^\n]*FROM dbo\.etp_landing_{rule.ReportCode.ToLowerInvariant()} ",
                    views["v_service_reading_windows"]);
                Assert.Matches($@"SELECT ''{rule.ReportCode}''[^\n]*,{rule.DateColumn.CanonicalField}( business_date)? FROM dbo\.etp_landing_{rule.ReportCode.ToLowerInvariant()} ",
                    views["v_service_datelog_readings"]);
            }
            if (rule.JobColumn is { } job)
            {
                var source = ServiceInterimFamilies.StatusViews.Any(status => status.ReportCode == rule.ReportCode)
                    ? views["v_service_status_view_rows"] : views["v_service_job_readings"];
                Assert.Matches($@"SELECT ''{rule.ReportCode}''[^\n]*CONVERT\(nvarchar\(100\),{job.CanonicalField}\)", source);
            }
        }
    }

    [Fact]
    public void Status_labels_ranks_and_pending_keys_match_the_shared_contract()
    {
        var families = Views(Section(Script())).Single(view => view.Name == "v_service_families").Body;
        foreach (var status in ServiceInterimFamilies.StatusViews)
            Assert.Matches($@"\(''{status.ReportCode}'',''JobList'',NULL,''[^']*'',NULL,''{status.StatusLabel}'',{status.LifecycleRank}\)", families);
        foreach (var list in ServiceInterimFamilies.PendingLists)
            Assert.Matches($@"\(''{list.ReportCode}'',''\w+'',NULL,''{list.Label}'',''{list.ListKey}'',NULL,NULL\)", families);
    }

    [Fact]
    public void No_view_and_no_query_names_a_phone_e_mail_or_address_column()
    {
        var restricted = RestrictedColumns();
        Assert.NotEmpty(restricted);
        var section = Section(Script());
        var queries = string.Join('\n', SqlServerServiceReportQuery.RefreshesSql, SqlServerServiceReportQuery.JobsSql,
            SqlServerServiceReportQuery.PendingSql, SqlServerServiceReportQuery.JobHistorySql, SqlServerServiceReportQuery.MoneyCheckSql,
            SqlServerServiceReportQuery.MoneyChangesSql);
        foreach (var column in restricted)
        {
            var name = new Regex($@"(?<![\w]){Regex.Escape(column)}(?![\w])", RegexOptions.IgnoreCase);
            Assert.False(name.IsMatch(section), $"Section C names the restricted column {column}.");
            Assert.False(name.IsMatch(queries), $"The Service query names the restricted column {column}.");
        }
        Assert.DoesNotContain(" * FROM dbo.etp_landing", section, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\bSELECT\s+\*\s+FROM\s+dbo\.", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void The_query_class_reads_only_the_service_views_and_the_manual_service_entries()
    {
        var queries = new[]
        {
            SqlServerServiceReportQuery.RefreshesSql, SqlServerServiceReportQuery.JobsSql, SqlServerServiceReportQuery.PendingSql,
            SqlServerServiceReportQuery.JobHistorySql, SqlServerServiceReportQuery.MoneyCheckSql, SqlServerServiceReportQuery.MoneyChangesSql,
            SqlServerServiceReportQuery.ManualMoneySql,
        };
        // The manual Service entries are read only through v_service_manual_money (decision 16), never from the table.
        var allowed = ContractViews.Append("v_service_manual_money").ToHashSet(StringComparer.Ordinal);
        foreach (var sql in queries)
        {
            foreach (Match used in Regex.Matches(sql, @"\bdbo\.(\w+)"))
                Assert.Contains(used.Groups[1].Value, allowed);
            Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC)\b", RegexOptions.IgnoreCase), sql);
        }
        var section = Section(Script());
        foreach (var sql in queries)
            foreach (Match column in Regex.Matches(sql, @"FROM dbo\.(v_service_\w+)"))
                Assert.Contains($"VIEW dbo.{column.Groups[1].Value} AS", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_of_lane_L4_lies_outside_its_markers()
    {
        var script = Script();
        var section = Section(script);
        var outside = script.Replace(section, "");
        Assert.DoesNotMatch(new Regex(@"\bv_service_\w+"), outside);
        Assert.DoesNotContain("CREATE OR ALTER VIEW", outside, StringComparison.Ordinal);
        var after = script[(script.IndexOf($"-- <<< {SectionName} end", StringComparison.Ordinal) + $"-- <<< {SectionName} end".Length)..];
        Assert.True(string.IsNullOrWhiteSpace(after), "Nothing may follow the C_SERVICE_READ section.");
    }

    // Decision 16 (Q1): only the Service-money shop's entries are compared; another shop's entry is listed apart and
    // never summed in. ServiceMoneyCheckTests covers the rules; this pins that the repository goes through them.
    [Fact]
    public void The_money_check_compares_only_the_service_money_shop_and_lists_other_shops_apart()
    {
        var day = new DateOnly(2026, 9, 21);
        var manual = new[]
        {
            new ServiceManualMoneyEntry(day, "SYN01", "Synthetic shop", "SERVICE_CASH", 60m, IsServiceMoneyShop: true),
            new ServiceManualMoneyEntry(day, "SYN02", "Other shop", "SERVICE_CASH", 30m, IsServiceMoneyShop: false),
        };
        var rows = ServiceMoneyCheck.Compare([new ServiceS004TenderAmount(day, "CASH", 100m)], manual);
        var cash = Assert.Single(rows);
        Assert.Equal((100m, 60m, 40m), (cash.S004Amount!.Value, cash.ManualAmount!.Value, cash.Difference!.Value));
        Assert.Equal(["SYN01"], cash.ManualStores);
        var apart = Assert.Single(ServiceMoneyCheck.Unmatched(manual));
        Assert.Equal(("SYN02", 30m), (apart.StoreCode, apart.Amount));
        Assert.Contains("FROM dbo.v_service_manual_money", SqlServerServiceReportQuery.MoneyCheckSql, StringComparison.Ordinal);
        Assert.DoesNotContain("manual_operational_inputs", SqlServerServiceReportQuery.MoneyCheckSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FirstSeen", ServiceJobEventKind.FirstSeen)]
    [InlineData("StillListed", ServiceJobEventKind.StillListed)]
    [InlineData("LeftList", ServiceJobEventKind.LeftList)]
    [InlineData("Reappeared", ServiceJobEventKind.Reappeared)]
    public void Event_kinds_of_the_view_map_to_the_contract(string code, ServiceJobEventKind expected) =>
        Assert.Equal(expected, SqlServerServiceReportQuery.EventKind(code));

    // A job-list family's last reading holding a job is not "still listed": LastSeen is not a contract kind.
    [Fact]
    public void LastSeen_is_not_an_event_kind() =>
        Assert.Throws<InvalidOperationException>(() => SqlServerServiceReportQuery.EventKind("LastSeen"));

    [Fact]
    public void Job_list_families_report_StillListed_only_from_the_latest_reading_and_LeftList_otherwise()
    {
        var events = Views(Section(Script())).Single(view => view.Name == "v_service_job_list_events").Body;
        Assert.Contains("list_dates AS (", events, StringComparison.Ordinal);
        Assert.Contains("WHERE j.last_seen=d.latest_date AND j.last_seen>j.first_seen", events, StringComparison.Ordinal);
        Assert.Contains("''LeftList'',n.next_date,j.last_seen", events, StringComparison.Ordinal);
        Assert.Contains("w.snapshot_date>j.last_seen", events, StringComparison.Ordinal);
    }

    // A Restate at the same snapshot date supersedes the old reading; the money screen compares against it.
    [Fact]
    public void Money_changes_compare_a_restated_reading_with_the_reading_it_superseded()
    {
        var changes = Views(Section(Script())).Single(view => view.Name == "v_service_money_changes").Body;
        Assert.Contains("f.superseded_by_import_file_id current_import_file_id", changes, StringComparison.Ordinal);
        Assert.Contains("WHERE f.is_superseded=1", changes, StringComparison.Ordinal);
        Assert.Contains("COALESCE(x.import_file_id,p.import_file_id)", changes, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_event_kind_the_view_emits_is_mapped()
    {
        var events = Views(Section(Script())).Single(view => view.Name == "v_service_job_list_events").Body;
        var kinds = Regex.Matches(events, @"''(?<kind>[A-Z][A-Za-z]+)''")
            .Select(match => match.Groups["kind"].Value)
            .Where(kind => kind is not ("JobList" or "DateLog" or "StateSnapshot")).ToHashSet();
        Assert.Equal(["FirstSeen", "LeftList", "Reappeared", "StillListed"], kinds.Order(StringComparer.Ordinal));
        foreach (var kind in kinds) _ = SqlServerServiceReportQuery.EventKind(kind);
    }

    // Canonical names of the phone, e-mail and address columns of every Service family, from the frozen spec.
    private static IReadOnlyList<string> RestrictedColumns()
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", "service-centre", "families.spec.json");
        using var spec = JsonDocument.Parse(File.ReadAllText(path));
        return spec.RootElement.EnumerateArray()
            .SelectMany(family => family.GetProperty("Columns").EnumerateArray())
            .Select(column => column.GetProperty("CanonicalField").GetString()!)
            .Where(name => RestrictedName().IsMatch(name))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    // phonepe (S004/S005) is a UPI tender amount, not a phone number.
    [GeneratedRegex(@"mobile|phone(?!pe)|landline|e_?mail|address|contact", RegexOptions.IgnoreCase)]
    private static partial Regex RestrictedName();

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();

    [GeneratedRegex(@"^(GRANT SELECT ON dbo\.v_service_\w+ TO etp_viewer,etp_store_manager,etp_owner;|DENY INSERT,UPDATE,DELETE ON dbo\.v_service_\w+ TO etp_store_manager,etp_viewer;)$")]
    private static partial Regex PermissionStatement();
}
