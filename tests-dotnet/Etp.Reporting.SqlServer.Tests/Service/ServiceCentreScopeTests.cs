using Etp.Reporting.Application.Service;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// 1.12.1 lane P1: the Service screens filter per Service Centre through <see cref="SqlServerServiceReportQuery"/> and
/// v_service_job's service_store_code, and (review m-1) the C# catalogue's prefix rule is the view's. The SQL behaviour is
/// IntegrationTests/Service/ServiceCentreScopeSqlTests (SQL suite).
/// </summary>
public sealed class ServiceCentreScopeTests
{
    private const string Connection = "Server=(localdb)\\MSSQLLocalDB;Database=EtpSynthetic;Integrated Security=true;Encrypt=false";

    [Fact]
    public void The_centre_statement_reads_the_job_columns_of_one_centre_from_v_service_job()
    {
        var sql = SqlServerServiceReportQuery.CentreJobsSql;
        Assert.StartsWith("SELECT " + SqlServerServiceReportQuery.JobColumns + " FROM dbo.v_service_job ", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE service_store_code=@centre", sql, StringComparison.Ordinal);
        Assert.Contains(sql, SqlServerServiceReportQuery.UiStatements);
        // The unscoped statement is unchanged, so the shared rows of the other screens stay what they were.
        Assert.Equal("SELECT " + SqlServerServiceReportQuery.JobColumns + " FROM dbo.v_service_job ORDER BY job_order_number;",
            SqlServerServiceReportQuery.AllJobsSql);
    }

    [Fact]
    public void A_query_is_scoped_by_a_trimmed_store_code_and_blank_means_every_centre()
    {
        var all = new SqlServerServiceReportQuery(Connection);
        Assert.Null(all.ServiceStoreCode);
        Assert.Equal("SC999", all.ForServiceCentre(" SC999 ").ServiceStoreCode);
        Assert.Null(all.ForServiceCentre("   ").ServiceStoreCode);
        Assert.Null(all.ForServiceCentre(null).ServiceStoreCode);
        Assert.Null(new SqlServerServiceReportQuery(Connection, "").ServiceStoreCode);
        Assert.Throws<ArgumentException>(() => all.ForServiceCentre(new string('S', SqlServerServiceReportQuery.MaxStoreCodeLength + 1)));
        Assert.Equal(new string('S', 30), all.ForServiceCentre(new string('S', 30)).ServiceStoreCode);
    }

    [Fact]
    public void Claim_lines_of_a_scoped_query_are_those_of_its_jobs_and_a_line_without_a_job_is_left_out()
    {
        ServiceClaimLine Line(string? job) => new("WDC", new DateOnly(2026, 9, 20), "DCSYN1", job, "ITEMSYN", 1m, 100m, 90m, null, "S039", new DateOnly(2026, 9, 28));
        var lines = new[] { Line("JOSC999SYN0001"), Line(" josc999syn0001 "), Line("JOAW330SYN0002"), Line(null), Line("  ") };

        var kept = SqlServerServiceReportQuery.ClaimLinesOfJobs(lines, [Job("JOSC999SYN0001")]);

        Assert.Equal(2, kept.Count);
        Assert.All(kept, line => Assert.Equal("JOSC999SYN0001", line.JobOrderNumber!.Trim(), ignoreCase: true));
        Assert.Empty(SqlServerServiceReportQuery.ClaimLinesOfJobs(lines, []));
    }

    // ------------------------------------------------------------------ review m-1: one prefix rule

    [Fact]
    public void The_catalogue_and_v_service_job_write_the_fallback_prefix_with_the_same_expression()
    {
        var view = SchemaSourceText.Current("dbo.v_service_job").Replace("\r\n", "\n");
        var expression = SqlServerServiceCentreCatalog.FallbackPrefixSql;
        Assert.Contains("COALESCE(s.service_job_prefix," + expression + ") service_job_prefix", view, StringComparison.Ordinal);
        // The 0067 read is dynamic SQL, so its quotes are doubled; the read before 0067 uses the expression as written.
        var load = SqlServerServiceCentreCatalog.LoadSql;
        var split = load.LastIndexOf("EXEC sp_executesql", StringComparison.Ordinal);
        Assert.Contains(expression + " service_job_prefix", load[..split], StringComparison.Ordinal);
        Assert.Contains("COALESCE(s.service_job_prefix," + expression.Replace("'", "''", StringComparison.Ordinal) + ") service_job_prefix",
            load[split..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SC999", "JOSC999")]
    [InlineData(" sc_97 ", "JOSC_97")]
    [InlineData("AW-330", "JOAW-330")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUV", "JOABCDEFGHIJKLMNOPQR")]
    public void The_CSharp_fallback_prefix_is_the_views_upper_case_trimmed_and_cut_to_20(string storeCode, string expected)
    {
        var prefix = SqlServerServiceCentreCatalog.FallbackPrefix(storeCode);
        Assert.Equal(expected, prefix);
        Assert.True(prefix.Length <= ServiceCentreRules.MaxPrefixLength);
    }

    [Fact]
    public void A_fallback_prefix_with_an_underscore_matches_as_a_plain_start_in_CSharp_and_in_the_view()
    {
        IReadOnlyList<ServiceCentre> centres = [new("SC_97", "Synthetic", "JOSC_97"), new("SC999", "Synthetic 2", "JOSC999")];
        Assert.Equal("SC_97", ServiceCentreRules.CentreForJob(centres, "JOSC_97SYN0004")?.StoreCode);
        Assert.Null(ServiceCentreRules.CentreForJob(centres, "JOSCX97SYN0005"));
        var view = SchemaSourceText.Current("dbo.v_service_job").Replace("\r\n", "\n");
        Assert.Contains("LEFT(a.job_order_number,LEN(c.service_job_prefix))=CONVERT(nvarchar(20),c.service_job_prefix)", view, StringComparison.Ordinal);
    }

    private static ServiceJobSummary Job(string number) =>
        new(number, null, ServiceJobTypes.Booking, false, null, "Sample Brand", "Model", "Watch", null, null, null, null,
            ServiceStages.OnBench, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false,
            null, null, null, null, null, 0, null, null, null, null, null, true, null, new DateOnly(2026, 9, 28));
}
