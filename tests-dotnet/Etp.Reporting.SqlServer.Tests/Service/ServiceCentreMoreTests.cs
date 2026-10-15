using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.12.1 lane P8: the per-centre filter reaches the parts invoices, the closing stock and the "Not matched to any centre" jobs.
/// Text and rule tests; the SQL behaviour is IntegrationTests/Service/ServiceCentreMoreSqlTests (SQL suite). Synthetic only.
/// </summary>
public sealed class ServiceCentreMoreTests
{
    private const string Connection = "Server=(localdb)\\MSSQLLocalDB;Database=EtpSynthetic;Integrated Security=true;Encrypt=false";

    [Fact]
    public void The_not_matched_choice_is_a_code_no_store_can_have_and_is_offered_with_two_centres()
    {
        Assert.StartsWith("~", ServiceCentreScope.NotMatchedCode);
        Assert.True(ServiceCentreScope.NotMatchedCode.Length <= SqlServerServiceReportQuery.MaxStoreCodeLength);
        Assert.True(ServiceCentreScope.IsNotMatched("  ~not_matched "));
        Assert.False(ServiceCentreScope.IsNotMatched("SC999"));
        Assert.False(ServiceCentreScope.IsNotMatched(null));
        Assert.False(ServiceCentreScope.OffersNotMatched(1));
        Assert.True(ServiceCentreScope.OffersNotMatched(2));
        Assert.Equal("Not matched to any centre", ServiceCentreScope.NotMatchedLabel);
        Assert.DoesNotMatch(@"\bS0\d\d\b", ServiceCentreScope.ScopeNote("Synthetic Service Centre"));
        Assert.DoesNotMatch(@"\bS0\d\d\b", ServiceCentreScope.NotMatchedScopeNote());
    }

    [Fact]
    public void An_all_centres_query_runs_every_statement_unchanged()
    {
        var all = new SqlServerServiceReportQuery(Connection);
        Assert.Equal(SqlServerServiceReportQuery.PartsSql, all.ScopedSql(SqlServerServiceReportQuery.PartsSql));
        Assert.Equal(SqlServerServiceReportQuery.StockSql, all.ScopedSql(SqlServerServiceReportQuery.StockSql));
        Assert.Equal(SqlServerServiceReportQuery.AllJobsSql, all.ScopedSql(SqlServerServiceReportQuery.AllJobsSql));
    }

    [Fact]
    public void A_centre_query_swaps_parts_and_stock_and_leaves_money_transit_and_the_rest_alone()
    {
        var centre = new SqlServerServiceReportQuery(Connection, "SC999");
        Assert.Equal(SqlServerServiceReportQuery.PartsCentreSql, centre.ScopedSql(SqlServerServiceReportQuery.PartsSql));
        Assert.Equal(SqlServerServiceReportQuery.StockCentreSql, centre.ScopedSql(SqlServerServiceReportQuery.StockSql));
        Assert.Equal(SqlServerServiceReportQuery.CentreJobsSql, centre.ScopedSql(SqlServerServiceReportQuery.CentreJobsSql));
        foreach (var untouched in new[]
        {
            SqlServerServiceReportQuery.TransitSql, SqlServerServiceReportQuery.S004RangeSql, SqlServerServiceReportQuery.MoneyCheckSql,
            SqlServerServiceReportQuery.ManualMoneySql, SqlServerServiceReportQuery.MoneyChangesSql, SqlServerServiceReportQuery.FreshnessSql,
            SqlServerServiceReportQuery.OneJobSql, SqlServerServiceReportQuery.ClaimsSql,
        })
            Assert.Equal(untouched, centre.ScopedSql(untouched));
    }

    [Fact]
    public void The_not_matched_query_reads_the_rows_with_no_centre()
    {
        var none = new SqlServerServiceReportQuery(Connection, ServiceCentreScope.NotMatchedCode);
        Assert.True(none.ScopeIsNotMatched);
        Assert.Equal(SqlServerServiceReportQuery.PartsNotMatchedSql, none.ScopedSql(SqlServerServiceReportQuery.PartsSql));
        Assert.Equal(SqlServerServiceReportQuery.StockNotMatchedSql, none.ScopedSql(SqlServerServiceReportQuery.StockSql));
        Assert.Equal(SqlServerServiceReportQuery.CentreJobsNotMatchedSql, none.ScopedSql(SqlServerServiceReportQuery.CentreJobsSql));
        Assert.Contains("service_store_code IS NULL", SqlServerServiceReportQuery.CentreJobsNotMatchedSql);
        Assert.Contains("c.service_store_code IS NULL", SqlServerServiceReportQuery.PartsNotMatchedSql);
        Assert.Contains("service_store_code IS NULL", SqlServerServiceReportQuery.StockNotMatchedSql);
    }

    [Fact]
    public void The_twins_keep_the_columns_of_the_statements_they_replace_and_name_no_contact_column()
    {
        Assert.Equal(Columns(SqlServerServiceReportQuery.PartsSql), Columns(SqlServerServiceReportQuery.PartsCentreSql, "p."));
        Assert.Equal(Columns(SqlServerServiceReportQuery.PartsSql), Columns(SqlServerServiceReportQuery.PartsNotMatchedSql, "p."));
        Assert.Equal(Columns(SqlServerServiceReportQuery.StockSql), Columns(SqlServerServiceReportQuery.StockCentreSql));
        Assert.Equal(Columns(SqlServerServiceReportQuery.StockSql), Columns(SqlServerServiceReportQuery.StockNotMatchedSql));
        Assert.Equal(SqlServerServiceReportQuery.CentreJobsNotMatchedSql,
            SqlServerServiceReportQuery.AllJobsSql.Replace("ORDER BY", "WHERE service_store_code IS NULL ORDER BY"));
        foreach (var sql in SqlServerServiceReportQuery.CentreStatements)
        {
            Assert.DoesNotMatch(@"mobile|phone(?!pe)|landline|e_?mail|address|contact", sql);
            Assert.Contains("dbo.v_service_", sql);
        }
    }

    [Fact]
    public void BindScope_adds_the_centre_once_and_only_when_the_text_names_it()
    {
        var centre = new SqlServerServiceReportQuery(Connection, "SC999");
        using var parts = new SqlCommand(SqlServerServiceReportQuery.PartsCentreSql);
        centre.BindScope(parts);
        centre.BindScope(parts);
        var parameter = Assert.Single(parts.Parameters.Cast<SqlParameter>());
        Assert.Equal("@centre", parameter.ParameterName);
        Assert.Equal("SC999", parameter.Value);

        using var transit = new SqlCommand(SqlServerServiceReportQuery.TransitSql);
        centre.BindScope(transit);
        Assert.Empty(transit.Parameters);

        using var notMatched = new SqlCommand(SqlServerServiceReportQuery.PartsNotMatchedSql);
        new SqlServerServiceReportQuery(Connection, ServiceCentreScope.NotMatchedCode).BindScope(notMatched);
        Assert.Empty(notMatched.Parameters);

        using var all = new SqlCommand(SqlServerServiceReportQuery.PartsCentreSql);
        new SqlServerServiceReportQuery(Connection).BindScope(all);
        Assert.Empty(all.Parameters);
    }

    [Fact]
    public void The_two_object_files_follow_the_a3_format_and_read_only_what_they_must()
    {
        foreach (var (file, view, mustRead) in new[]
        {
            ("20-dbo.v_service_parts_centre.sql", "v_service_parts_centre", "dbo.etp_landing_s007"),
            ("33-dbo.v_service_stock_summary_centre.sql", "v_service_stock_summary_centre", "dbo.etp_landing_s006"),
        })
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "objects", file)).Replace("\r\n", "\n");
            Assert.Contains($"CREATE OR ALTER VIEW dbo.{view} AS", text);
            Assert.Contains(mustRead, text);
            Assert.Contains("u.unit_kind='SERVICE'", text);
            Assert.Contains("-- >>> GRANTS", text);
            Assert.Contains($"GRANT SELECT ON dbo.{view} TO etp_owner,etp_store_manager,etp_viewer;", text);
            foreach (var verb in new[] { "DELETE", "INSERT", "UPDATE" })
                Assert.Contains($"DENY {verb} ON dbo.{view} TO etp_store_manager,etp_viewer;", text);
            Assert.DoesNotMatch(@"(?im)^(?!--).*(mobile|phone(?!pe)|landline|e_?mail|address|contact)", text);
        }
    }

    private static string[] Columns(string sql, string alias = "") =>
        sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..sql.IndexOf("FROM", StringComparison.Ordinal)]
            .Replace("SELECT", "").Replace(alias.Length == 0 ? "SELECT" : alias, "").Split(',').Select(column => column.Trim()).ToArray();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }
}
