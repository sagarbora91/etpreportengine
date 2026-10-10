using System.Text.RegularExpressions;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service UI 1.10.0, lane perf (design 4.1 and 6.5, SD-11). On live, v_service_job's COUNT(DISTINCT document_number) in
/// the won CTE made a nested-loops join over a lazy spool (2.3 s of 3.1 s CPU). Lane perf wrote the fix as a migration
/// 0052; because 0050 had not run on any real database, the final 1.10.0 merge folded it into 0050 instead and dropped
/// 0052. won_rows ranks each (job, document) with ROW_NUMBER and won counts rank 1. The SQL behaviour is
/// ServiceJobModelSqlTests (SQL suite).
/// </summary>
public sealed partial class ServiceJobPerformanceTextTests
{
    private const string WonStart = "\n), won_rows AS (";
    private const string WonEnd = "\n), other_rows AS (";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string MigrationsDirectory() => Path.Combine(RepositoryRoot(), "database", "migrations");

    private static string Read0050() =>
        File.ReadAllText(Path.Combine(MigrationsDirectory(), "0050_service_centre_ui.sql")).Replace("\r\n", "\n");

    private static string JobView(string script)
    {
        var blocks = ViewBlock().Matches(script).Where(match => match.Groups["name"].Value == "v_service_job").ToList();
        Assert.Single(blocks);
        return blocks[0].Groups["body"].Value;
    }

    private static string Won(string body)
    {
        var start = body.IndexOf(WonStart, StringComparison.Ordinal);
        var end = body.IndexOf(WonEnd, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "The won_rows / won CTEs are not where v_service_job keeps them.");
        return body[start..end];
    }

    [Fact]
    public void Only_0050_defines_v_service_job_and_no_separate_performance_migration_ships()
    {
        Assert.Empty(Directory.GetFiles(MigrationsDirectory(), "0052_*.sql"));
        var definers = Directory.GetFiles(MigrationsDirectory(), "*.sql")
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"VIEW\s+dbo\.v_service_job\s+AS", RegexOptions.IgnoreCase))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Equal(["0050_service_centre_ui.sql"], definers);
    }

    [Fact]
    public void Won_counts_distinct_documents_by_rank_and_keeps_the_DateLog_rule_and_every_other_column()
    {
        var view = JobView(Read0050());
        var won = Won(view);
        Assert.DoesNotContain("COUNT(DISTINCT", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROW_NUMBER() OVER(PARTITION BY d.job_order_number,d.document_number ORDER BY d.import_file_id) document_rank", won, StringComparison.Ordinal);
        Assert.Contains("SUM(CASE WHEN d.document_number IS NOT NULL AND d.document_rank=1 THEN 1 ELSE 0 END) revenue_documents", won, StringComparison.Ordinal);
        Assert.Contains("  FROM won_rows d\n  GROUP BY d.job_order_number", won, StringComparison.Ordinal);

        // The DateLog rule (the winning reading per business date) sits in won_rows, before won.
        var rule = won[won.IndexOf("  FROM dated d JOIN dated_files f", StringComparison.Ordinal)..won.IndexOf("), won AS (", StringComparison.Ordinal)];
        Assert.Contains("WHERE d.job_order_number IS NOT NULL", rule, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS(SELECT 1 FROM dated_files g WHERE g.report_code=d.report_code AND d.business_date BETWEEN g.window_from AND g.window_to", rule, StringComparison.Ordinal);
        Assert.Contains("AND (g.snapshot_date>f.snapshot_date OR (g.snapshot_date=f.snapshot_date AND g.import_file_id>f.import_file_id)))", rule, StringComparison.Ordinal);

        // The other nine aggregates of won are the ones 0050 had before the fold.
        var aggregates = Regex.Matches(won[won.IndexOf("), won AS (", StringComparison.Ordinal)..], @"(SUM|MIN|MAX|COUNT)\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\) \w+")
            .Select(match => match.Value).Where(value => !value.EndsWith(" revenue_documents", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [
                "SUM(CASE WHEN d.report_code=''S003'' THEN d.labour_charge END) revenue_labour_charge",
                "SUM(CASE WHEN d.report_code=''S003'' THEN d.spare_charge END) revenue_spare_charge",
                "SUM(CASE WHEN d.report_code=''S003'' THEN d.net_incl_tax END) revenue_net_incl_tax",
                "MIN(d.srf_date) srf_date",
                "MAX(d.repair_date) repair_date",
                "MAX(d.delivered_date) delivered_date",
                "MAX(CASE WHEN d.status=''Delivered'' AND d.delivered_date IS NOT NULL THEN 1 ELSE 0 END) is_delivered",
                "MAX(CASE WHEN d.status IN(''RWR'',''Returned_Without_Repair'') THEN 1 ELSE 0 END) is_rwr",
                "MAX(d.brand) brand",
            ],
            aggregates);
    }

    [GeneratedRegex(@"EXEC\(N'CREATE OR ALTER VIEW dbo\.(?<name>\w+) AS\n(?<body>.*?)'\);\n", RegexOptions.Singleline)]
    private static partial Regex ViewBlock();
}
