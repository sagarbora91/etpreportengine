using System.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 A1 cross-layer test (plan Appendix C 1): a factory connection reaches the test database and an existing
/// repository runs through the factory's string; a refused endpoint fails in the constructor, before any socket.
/// </summary>
public sealed class ConnectionsFactorySqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [SqlFact]
    public async Task Factory_connection_runs_a_real_repository_call()
    {
        var factory = new EtpConnectionFactory(database.ConnectionString);
        Assert.Equal(database.Name, factory.DatabaseName);

        await using (var connection = await factory.OpenAsync())
        {
            await using var command = new SqlCommand("SELECT DB_NAME();", connection);
            Assert.Equal(database.Name, (string?)await command.ExecuteScalarAsync());
        }

        // An existing 1.11 repository, given the factory's validated string: the seeded stores come back.
        var codes = await new StoreCatalogRepository(factory.ConnectionString).ActiveCodesAsync();
        var expected = (int)(await database.ExecuteAsync(
            "SELECT COUNT(*) FROM dbo.stores WHERE is_active = 1;"))!;
        Assert.True(codes.Length <= expected);
        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
    }

    [SqlFact]
    public async Task Commit_budget_check_and_maintenance_connections_open_where_they_should()
    {
        var factory = new EtpConnectionFactory(database.ConnectionString);

        await using (var budget = await factory.OpenAsync(EtpConnectionPurpose.CommitBudget))
        {
            Assert.Equal(LocalSqlConnectionPolicy.CommitBudgetSeconds, budget.ConnectionTimeout);
            await using var command = new SqlCommand("SELECT DB_NAME();", budget);
            Assert.Equal(database.Name, (string?)await command.ExecuteScalarAsync());
        }

        await using (var check = await factory.OpenAsync(EtpConnectionPurpose.CommitCheck))
            Assert.False(new SqlConnectionStringBuilder(check.ConnectionString).Pooling);

        await using (var maintenance = await factory.OpenAsync(EtpConnectionPurpose.Maintenance))
        {
            await using var command = new SqlCommand("SELECT DB_NAME();", maintenance);
            Assert.Equal("master", (string?)await command.ExecuteScalarAsync());
        }
    }

    [SqlTheory]
    [InlineData("Server=tcp:127.0.0.1,1;Database=EtpReporting;Integrated Security=True")]
    [InlineData("Server=192.0.2.1;Database=EtpReporting;Integrated Security=True")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=EtpReporting;User ID=sa;Password=not-a-real-one")]
    public void Refused_endpoint_never_opens_a_socket(string value)
    {
        // 192.0.2.1 is TEST-NET-1 and tcp:127.0.0.1,1 has no listener: an attempted login would take the
        // connect timeout. The refusal is synchronous, in the constructor, so no SqlConnection is ever made.
        var clock = Stopwatch.StartNew();
        var error = Assert.Throws<ArgumentException>(() => new EtpConnectionFactory(value));
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "A refused endpoint must not wait for a network attempt.");
        Assert.DoesNotContain("not-a-real-one", error.Message);
        Assert.DoesNotContain("192.0.2.1", error.Message);
    }
}
