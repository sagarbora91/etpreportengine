using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class SqlFixtureSchedulingTests
{
    [Fact]
    public void Fixture_collections_cannot_overlap_process_wide_pool_cleanup()
    {
        var scheduling = typeof(SqlDatabaseFixture).Assembly.GetCustomAttribute<CollectionBehaviorAttribute>();
        Assert.True(scheduling?.DisableTestParallelization == true,
            "SQL fixture collections must run serially: fixture disposal clears process-wide connection pools.");
        // xunit.runner.json overrides the assembly attribute, so a file that turns collection
        // parallelism back on would defeat it without touching this assembly.
        var runnerConfiguration = Path.Combine(AppContext.BaseDirectory, "xunit.runner.json");
        if (File.Exists(runnerConfiguration))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runnerConfiguration));
            Assert.False(document.RootElement.TryGetProperty("parallelizeTestCollections", out var parallel) && parallel.ValueKind == JsonValueKind.True,
                "xunit.runner.json turns collection parallelism back on.");
        }
    }

    // F-22, as behaviour rather than configuration: the attribute above can still be overridden
    // from the command line or a runsettings file. Every SQL test class is its own collection,
    // and this class is another. While this test runs, no other collection may be holding a
    // SQL fixture. With collections in parallel, the SQL classes running beside this one hold
    // fixtures for seconds at a time (each bootstraps every migration), so the window below
    // sees them. A test that never disposes its fixture also fails here, and should.
    [Fact]
    public async Task No_other_collection_holds_a_sql_fixture_while_this_collection_runs()
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            var live = SqlDatabaseFixture.LiveFixtureCount;
            Assert.True(live == 0,
                $"{live} SQL fixture(s) from another test collection are alive while this collection runs. Either collections are running in parallel " +
                "(their disposal clears every connection pool in the process, including the pools other collections are using), or a test never disposed its fixture.");
            await Task.Delay(20);
        }
    }
}
