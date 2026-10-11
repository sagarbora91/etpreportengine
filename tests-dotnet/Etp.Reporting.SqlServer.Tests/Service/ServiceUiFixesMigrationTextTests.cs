using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.10.1, lane service-sql: migration 0052 restates v_service_job (0050 shipped in 1.10.0 and is checksummed) with the
/// review findings 0050 left open, adds dbo.v_service_job_revision for the shared job rows (R-SQL-15) and takes the
/// Service-money shop from a dbo.stores flag instead of a literal (RA-OPS-21). The SQL behaviour is ServiceJobModelSqlTests
/// and ServiceUiFixesSqlTests (SQL suite).
/// </summary>
public sealed partial class ServiceUiFixesMigrationTextTests
{
    private const string MigrationName = "0052_service_ui_fixes.sql";
    private const string SectionName = "E_SERVICE_UI_FIXES";
    private static readonly string[] Views = ["v_service_manual_money", "v_service_job_revision", "v_service_job"];

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "migrations", name)).Replace("\r\n", "\n");

    private static string Script() => Read(MigrationName);

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

    [Fact]
    public void The_migration_is_the_only_0052_and_holds_one_section_without_transactions_or_GO()
    {
        var directory = Path.Combine(RepositoryRoot(), "database", "migrations");
        Assert.Equal([MigrationName], Directory.GetFiles(directory, "0052_*.sql").Select(Path.GetFileName));
        var script = Script();
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        Assert.Equal(["SET XACT_ABORT ON;"], Code(script.Replace(Section(script), "")));
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_restates_three_views_adds_one_stores_flag_and_grants_every_view()
    {
        var section = Section(Script());
        Assert.Equal(Views, ViewBlock().Matches(section).Select(match => match.Groups["name"].Value));
        foreach (var view in Views)
        {
            Assert.Single(Regex.Matches(section, Regex.Escape($"GRANT SELECT ON dbo.{view} TO etp_viewer,etp_store_manager,etp_owner;")));
            Assert.Single(Regex.Matches(section, Regex.Escape($"DENY INSERT,UPDATE,DELETE ON dbo.{view} TO etp_store_manager,etp_viewer;")));
        }
        // Outside the views: the guarded column, its index, its seed and the permissions, nothing else.
        var rest = Code(ViewBlock().Replace(section, "")).ToArray();
        Assert.All(rest, line => Assert.Matches(Allowed(), line));
        Assert.Contains("IF COL_LENGTH(N'dbo.stores', N'is_service_money_shop') IS NULL", rest);
        Assert.Contains("ALTER TABLE dbo.stores ADD is_service_money_shop bit NOT NULL CONSTRAINT DF_stores_is_service_money_shop DEFAULT (0);", rest);
        Assert.Contains("CREATE UNIQUE INDEX UX_stores_service_money_shop ON dbo.stores(is_service_money_shop) WHERE is_service_money_shop=1;');", rest);
        Assert.Contains("EXEC(N'IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE is_service_money_shop=1)", rest);
        Assert.DoesNotMatch(new Regex(@"\b(DROP|TRUNCATE|DELETE\s+FROM)\b", RegexOptions.IgnoreCase), string.Join("\n", Code(section)));
        Assert.DoesNotMatch(new Regex(@"\bSELECT\s+(\w+\.)?\*\s+FROM\s+dbo\.", RegexOptions.IgnoreCase), section);
    }

    [Fact]
    public void The_manual_money_view_keeps_its_0048_contract_and_reads_the_shop_from_the_stores_flag()
    {
        // RA-OPS-21: no store code in the view; the daily-inputs path keeps writing dbo.manual_operational_inputs.
        var view = View(Script(), "v_service_manual_money");
        Assert.DoesNotMatch(new Regex(@"store_code=''[A-Z0-9]+''"), view);
        Assert.Contains("CONVERT(bit,COALESCE(s.is_service_money_shop,0)) is_service_money_shop", view, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.manual_operational_inputs m LEFT JOIN dbo.stores s ON s.store_code=m.store_code", view, StringComparison.Ordinal);
        Assert.Contains("WHERE m.field_code LIKE ''SERVICE[_]%'' AND m.numeric_value IS NOT NULL", view, StringComparison.Ordinal);
        var old = Read("0048_service_centre_interim.sql");
        var oldView = old[old.IndexOf("EXEC(N'CREATE OR ALTER VIEW dbo.v_service_manual_money AS\n", StringComparison.Ordinal)..];
        static string Columns(string text) => text[..text.IndexOf("\nFROM ", StringComparison.Ordinal)];
        // Same columns in the same order; only the is_service_money_shop expression changed.
        var before = Columns(oldView[(oldView.IndexOf(" AS\n", StringComparison.Ordinal) + 4)..]);
        Assert.Equal(Regex.Replace(before, @"CONVERT\(bit,CASE WHEN m\.store_code=''\w+'' THEN 1 ELSE 0 END\)", "CONVERT(bit,COALESCE(s.is_service_money_shop,0))"), Columns(view));
    }

    [Fact]
    public void The_revision_view_and_the_job_view_use_the_0048_live_family_list()
    {
        var script = Script();
        var windows = Read("0048_service_centre_interim.sql");
        var codes0048 = Regex.Match(windows, @"AND f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value;
        Assert.Equal(codes0048, Regex.Match(View(script, "v_service_job"), @"AND f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value);
        var revision = View(script, "v_service_job_revision");
        Assert.Equal(codes0048, Regex.Match(revision, @"WHERE f\.report_code IN\((?<codes>[^)]*)\)").Groups["codes"].Value);
        Assert.Contains("CHECKSUM_AGG(CHECKSUM(f.import_file_id,f.is_superseded,f.data_truth_version,b.status,f.period_end,f.business_date)) state", revision, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.v_service_job_revision", SqlServerServiceReportQuery.JobRevisionSql, StringComparison.Ordinal);
    }

    [Fact]
    public void The_job_view_carries_each_open_finding_fix()
    {
        var job = View(Script(), "v_service_job");
        const string open = "s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%''";
        // R-SQL-03 tail: the SRN date of the job comes from open SRN rows; a closed SRN dates only DC/READY, as a last fallback.
        Assert.Contains($"MAX(CASE WHEN {open} THEN s.srn_date END) srn_date,MAX(s.srn_date) srn_any_date,", job, StringComparison.Ordinal);
        Assert.Contains("WHEN ''SRN_OUT'' THEN COALESCE(a.srn_srn_date,", job, StringComparison.Ordinal);
        Assert.Contains("WHEN ''DC_ISSUED'' THEN COALESCE(a.sv_wdc_date,a.srn_srn_back_date,a.srn_srn_any_date)", job, StringComparison.Ordinal);
        // R-SQL-04: S009 SRN_For_Repair is on the bench (design 4.2 rule 8).
        Assert.Contains("CASE WHEN a.st9_s009_status LIKE ''SRN%'' AND a.st9_s009_status<>''SRN_For_Repair'' THEN ''SRN_OUT''", job, StringComparison.Ordinal);
        // R-SQL-09: S009/S010 date the job only through their latest reading.
        Assert.Contains("CONVERT(date,CASE WHEN report_code IN(''S009'',''S010'') THEN NULL ELSE snapshot_date END) last_reading_date", job, StringComparison.Ordinal);
        Assert.Contains("SELECT x.job_order_number,MAX(l.snapshot_date) state_snapshot,", job, StringComparison.Ordinal);
        Assert.Contains("SELECT job_order_number,CONVERT(date,state_snapshot),", job, StringComparison.Ordinal);
        // R-SQL-10: no DateLog line without a business date.
        Assert.Contains("WHERE d.job_order_number IS NOT NULL AND d.business_date IS NOT NULL", job, StringComparison.Ordinal);
        // R-SQL-11: the claim flags come from v_service_claims, not from raw claim rows of any reading.
        Assert.Contains("FROM dbo.v_service_claims c\n  WHERE c.claim_type IN(''WDC'',''WRA'') AND c.job_order_number IS NOT NULL", job, StringComparison.Ordinal);
        Assert.DoesNotContain("o.report_code IN(''S025'',''S039'')", job, StringComparison.Ordinal);
        Assert.DoesNotContain("o.report_code IN(''S026'',''S040'')", job, StringComparison.Ordinal);
        // R-SQL-13: open ages, days in stage and the EDD test to the job's own as-at.
        Assert.Equal(3, Regex.Matches(job, Regex.Escape("COALESCE(a.last_reading_date,snap.as_at)")).Count);
        Assert.DoesNotContain("DATEDIFF(day,bd.booking_date,snap.as_at)", job, StringComparison.Ordinal);
        // R-SQL-16 / R-UI-07: claim_date and closed_date after as_at.
        var finalSelect = job[job.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        Assert.Matches(new Regex(@"a\.last_reading_date,snap\.as_at,\n  CASE sg\.stage WHEN ''DC_ISSUED'' THEN a\.o_wdc_claim_date WHEN ''RA_ISSUED'' THEN a\.o_wra_claim_date END claim_date,\n"), finalSelect);
        Assert.Contains("THEN a.o_wra_claim_date END closed_date\nFROM agg a\nCROSS JOIN snap", finalSelect, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_branch_of_the_job_union_has_the_same_width_and_the_aggregate_reads_every_column()
    {
        var job = View(Script(), "v_service_job");
        var start = job.IndexOf("), facts_by_job AS (", StringComparison.Ordinal);
        var end = job.IndexOf("), agg AS (", StringComparison.Ordinal);
        var branches = job[start..end].Split('\n').Where(line => line.StartsWith("  SELECT job_order_number,", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, branches.Count); // held, sv, view_money, lists, state, srn, won, other_jobs, claimed
        var widths = branches.Select(Width).Distinct().ToList();
        Assert.Single(widths);
        var agg = job[end..job.IndexOf("), snap AS (", StringComparison.Ordinal)];
        Assert.Equal(widths[0] - 1, Regex.Matches(agg, @"MAX\(u\.\w+\) \w+").Count); // every column but the key
        foreach (var column in new[] { "srn_srn_any_date", "o_wdc_claim_date", "o_wra_claim_date" })
            Assert.Contains($"MAX(u.{column}) {column}", agg, StringComparison.Ordinal);
    }

    [Fact]
    public void The_query_reads_the_job_columns_in_the_order_0052_selects_them()
    {
        var job = View(Script(), "v_service_job");
        var finalSelect = job[job.LastIndexOf("\nSELECT a.job_order_number", StringComparison.Ordinal)..];
        var position = 0;
        foreach (var column in SqlServerServiceReportQuery.JobColumns.Split(','))
        {
            var found = Regex.Match(finalSelect[position..], $@"(\b{column}|a\.{column}),?\s*\n?");
            Assert.True(found.Success, $"v_service_job does not select {column} after the previous column.");
            position += found.Index + found.Length;
        }
        Assert.EndsWith("as_at,claim_date,closed_date", SqlServerServiceReportQuery.JobColumns, StringComparison.Ordinal);
    }

    // The number of top-level columns of one branch line (commas outside parentheses, plus one).
    private static int Width(string line)
    {
        var body = line[..line.LastIndexOf(" FROM ", StringComparison.Ordinal)];
        var (depth, commas) = (0, 0);
        foreach (var ch in body)
        {
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            else if (ch == ',' && depth == 0) commas++;
        }
        return commas + 1;
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();

    [GeneratedRegex(@"^(GRANT SELECT ON dbo\.v_service_\w+ TO etp_viewer,etp_store_manager,etp_owner;|DENY INSERT,UPDATE,DELETE ON dbo\.v_service_\w+ TO etp_store_manager,etp_viewer;|IF COL_LENGTH\(N'dbo\.stores', N'is_service_money_shop'\) IS NULL|ALTER TABLE dbo\.stores ADD is_service_money_shop .*|EXEC\(N'IF NOT EXISTS\(SELECT 1 FROM (sys\.indexes|dbo\.stores) WHERE .*|CREATE UNIQUE INDEX UX_stores_service_money_shop .*|UPDATE dbo\.stores SET is_service_money_shop=1,.*|change_reason=N''Service money shop flag .*|WHERE store_code=''WLMHW'';'\);|SET XACT_ABORT ON;)$")]
    private static partial Regex Allowed();
}
