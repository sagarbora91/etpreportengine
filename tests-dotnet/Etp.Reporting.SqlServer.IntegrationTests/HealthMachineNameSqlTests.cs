using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.11.2 L9: a Windows computer name with an apostrophe, an ampersand, spaces or Unicode letters is recorded by the
/// C# store through <c>diag.record_health_check_run</c> (whose <c>diag.is_token(@machine,64)</c> check refused it
/// before). The stored value is the sanitised token.
/// </summary>
public sealed class HealthMachineNameSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Theory]
    [InlineData("SAGAR'S PC", "SAGAR_S_PC")]
    [InlineData("A&B Jewellers", "A_B_Jewellers")]
    [InlineData("रमेश-PC", "____-PC")]
    [InlineData("Shop (2nd floor), till;1", "Shop__2nd_floor___till_1")]
    public async Task A_run_from_a_pc_with_an_unusual_name_is_recorded(string machine, string stored)
    {
        var store = new SqlServerHealthCheckStore(database.ConnectionString);
        var started = new DateTimeOffset(2026, 10, 11, 21, 0, 4, TimeSpan.Zero);
        var runId = await store.RecordRunAsync(new HealthCheckRun(
            HealthCheckTrigger.Manual, new DateOnly(2026, 10, 11), started, started.AddSeconds(3),
            "1.11.2.0", machine, "Automation", HealthOutcome.Pass, new HealthCheckCounts(1, 0, 0, 0, 0), null,
            [new HealthCheckResult("DB-01", HealthCheckArea.Database, HealthOutcome.Pass, "OK", "Fine.", null, TimeSpan.FromMilliseconds(5), 1m)]));
        Assert.True(runId > 0);
        var run = (await store.LoadRunsAsync(50, HealthCheckTrigger.Manual)).Single(item => item.HealthCheckRunId == runId);
        Assert.Equal(stored, run.MachineName);
    }
}
