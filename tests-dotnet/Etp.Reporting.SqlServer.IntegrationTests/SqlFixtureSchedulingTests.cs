using System.Reflection;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class SqlFixtureSchedulingTests
{
    [Fact]
    public void Fixture_collections_cannot_overlap_process_wide_pool_cleanup()
    {
        var scheduling = typeof(SqlDatabaseFixture).Assembly.GetCustomAttribute<CollectionBehaviorAttribute>();
        Assert.True(scheduling?.DisableTestParallelization == true,
            "SQL fixture collections must run serially: fixture disposal clears process-wide connection pools.");
    }
}
