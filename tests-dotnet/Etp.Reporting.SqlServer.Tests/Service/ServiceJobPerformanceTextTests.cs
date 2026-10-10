using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service UI 1.10.0, lane perf (design 4.1 and 6.5, SD-11): migration 0052 re-states v_service_job exactly as 0050 defines
/// it except the won CTE, whose COUNT(DISTINCT document_number) made a nested-loops join over a lazy spool (2.3 s of 3.1 s
/// CPU on live). 0052 counts rank 1 of ROW_NUMBER per job and document instead. It adds no table, index or procedure and
/// keeps the grants. The SQL behaviour is ServiceJobModelSqlTests (SQL suite), which runs after every migration.
/// </summary>
public sealed partial class ServiceJobPerformanceTextTests
{
    private const string MigrationName = "0052_service_job_performance.sql";
    private const string SectionName = "E_SERVICE_JOB_PERFORMANCE";

    private const string WonStart = "\n), won";
    private const string WonEnd = "\n), other_rows AS (";

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

    private static string JobView(string script)
    {
        var blocks = ViewBlock().Matches(script).Where(match => match.Groups["name"].Value == "v_service_job").ToList();
        Assert.Single(blocks);
        return blocks[0].Groups["body"].Value;
    }

    private static (string Before, string Won, string After) SplitAtWon(string body)
    {
        var start = body.IndexOf(WonStart, StringComparison.Ordinal);
        var end = body.IndexOf(WonEnd, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "The won CTE is not where v_service_job keeps it.");
        return (body[..start], body[start..end], body[end..]);
    }

    private static IEnumerable<string> Code(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("--", StringComparison.Ordinal));

    [Fact]
    public void The_migration_is_the_only_0052_with_one_section_and_nothing_but_SET_XACT_ABORT_outside_it()
    {
        var directory = Path.Combine(RepositoryRoot(), "database", "migrations");
        Assert.Equal([MigrationName], Directory.GetFiles(directory, "0052_*.sql").Select(Path.GetFileName));
        var script = Script();
        Assert.Single(Regex.Matches(script, $@"^-- >>> {SectionName} begin$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(script, $@"^-- <<< {SectionName} end$", RegexOptions.Multiline));
        Assert.Equal(["SET XACT_ABORT ON;"], Code(script.Replace(Section(script), "")));
        Assert.StartsWith("SET XACT_ABORT ON;", Code(script).First(), StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b", RegexOptions.IgnoreCase), string.Join("\n", Code(script)));
        Assert.DoesNotMatch(new Regex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase), script);
    }

    [Fact]
    public void The_section_redefines_only_v_service_job_and_states_its_0050_grants_again()
    {
        var section = Section(Script());
        Assert.Equal(["v_service_job"], ViewBlock().Matches(section).Select(match => match.Groups["name"].Value));
        Assert.Equal(
            [
                "GRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;",
                "DENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;",
            ],
            Code(ViewBlock().Replace(section, "")));
        var script0050 = Read("0050_service_centre_ui.sql");
        foreach (var permission in Code(ViewBlock().Replace(section, "")))
            Assert.Contains(permission, script0050, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(CREATE|ALTER|DROP)\s+(TABLE|PROC|PROCEDURE|TRIGGER|INDEX|FUNCTION|SCHEMA|USER|ROLE|STATISTICS)\b", RegexOptions.IgnoreCase), section);
        Assert.DoesNotMatch(new Regex(@"\b(INSERT|UPDATE|DELETE|MERGE|TRUNCATE)\s+(INTO\s+)?(dbo\.|\[)", RegexOptions.IgnoreCase), section);
        Assert.DoesNotContain("'", JobView(section).Replace("''", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_equals_0050_outside_the_won_CTE()
    {
        // If lane sql changes v_service_job in 0050, 0052 must carry the same change (it replaces the 0050 view).
        var (before0050, _, after0050) = SplitAtWon(JobView(Read("0050_service_centre_ui.sql")));
        var (before0052, _, after0052) = SplitAtWon(JobView(Script()));
        Assert.Equal(before0050, before0052);
        Assert.Equal(after0050, after0052);
    }

    [Fact]
    public void Won_counts_distinct_documents_by_rank_and_keeps_the_DateLog_rule_and_every_other_column()
    {
        var (_, won0050, _) = SplitAtWon(JobView(Read("0050_service_centre_ui.sql")));
        var (_, won0052, _) = SplitAtWon(JobView(Script()));
        Assert.Contains("COUNT(DISTINCT d.document_number) revenue_documents", won0050, StringComparison.Ordinal);
        Assert.DoesNotContain("COUNT(DISTINCT", JobView(Script()), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("\n), won_rows AS (\n", won0052, StringComparison.Ordinal);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY d.job_order_number,d.document_number ORDER BY d.import_file_id) document_rank", won0052, StringComparison.Ordinal);
        Assert.Contains("SUM(CASE WHEN d.document_number IS NOT NULL AND d.document_rank=1 THEN 1 ELSE 0 END) revenue_documents", won0052, StringComparison.Ordinal);
        Assert.Contains("  FROM won_rows d\n  GROUP BY d.job_order_number", won0052, StringComparison.Ordinal);

        // The DateLog rule (the winning reading per business date) moves into won_rows word for word.
        var rule0050 = won0050[won0050.IndexOf("  FROM dated d JOIN dated_files f", StringComparison.Ordinal)..won0050.IndexOf("  GROUP BY d.job_order_number", StringComparison.Ordinal)];
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dated_files g", rule0050, StringComparison.Ordinal);
        Assert.Contains(rule0050 + "), won AS (\n", won0052, StringComparison.Ordinal);

        // Every other aggregate of won is unchanged.
        static IEnumerable<string> Aggregates(string won) =>
            Regex.Matches(won[won.IndexOf("), won AS (", StringComparison.Ordinal)..], @"(SUM|MIN|MAX|COUNT)\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\) \w+")
                .Select(match => match.Value).Where(value => !value.EndsWith(" revenue_documents", StringComparison.Ordinal));
        Assert.Equal(Aggregates(won0050), Aggregates(won0052));
        Assert.Equal(9, Aggregates(won0052).Count());
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();
}
