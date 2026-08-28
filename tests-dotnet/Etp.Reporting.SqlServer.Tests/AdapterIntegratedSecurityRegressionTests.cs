namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class AdapterIntegratedSecurityRegressionTests
{
    public static TheoryData<Func<string, object>> ApplicationAdapters => new()
    {
        { value => new SqlServerAccessSessionQuery(value) },
        { value => new SqlServerApplicationReportQuery(value) },
        { value => new SqlServerDailyWorkflowService(value) },
        { value => new SqlServerDashboardQuery(value) },
        { value => new SqlServerReportArchiveQuery(value) }
    };

    [Theory]
    [MemberData(nameof(ApplicationAdapters))]
    public void Application_SQL_adapters_reject_credentials_even_when_integrated_security_is_also_set(
        Func<string, object> create)
    {
        const string mixedCredentials =
            @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;User ID=reporter;Password=secret;TrustServerCertificate=True";

        Assert.Throws<ArgumentException>(() => create(mixedCredentials));
    }
}
