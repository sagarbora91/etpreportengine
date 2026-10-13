using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 S1 cross-layer tests (plan Appendix B item 1): the unattended wait meets the real SQL Server's error numbers, and
/// the interactive start refuses a database whose objects are behind this installation.
/// </summary>
public sealed class StartupSqlWaitSqlTests
{
    [SqlFact]
    public async Task A_database_that_does_not_exist_raises_4060_is_retried_to_the_budget_and_the_original_error_is_thrown()
    {
        var name = SqlDatabaseFixture.NewName();
        var connectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        var now = TimeSpan.Zero;
        var probes = 0;
        var log = new List<string>();

        var error = await Assert.ThrowsAsync<SqlException>(() => SqlStartupWait.WaitAsync(
            async token => { probes++; await SqlStartupWait.ProbeAsync(connectionString, token); },
            (span, _) => { now += span; return Task.CompletedTask; },
            () => now,
            log.Add,
            TimeSpan.FromSeconds(10)));

        Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 4060);
        Assert.True(SqlStartupWait.IsTransient(error));
        Assert.True(probes > 1, "4060 must be retried, not thrown at once.");
        Assert.Equal(probes - 1, log.Count);
        Assert.True(now <= TimeSpan.FromSeconds(10));
        Assert.All(log, line => Assert.DoesNotContain(name, line, StringComparison.Ordinal));
    }

    [SqlFact]
    public async Task A_denied_select_raises_229_which_is_not_transient()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("CREATE TABLE dbo.s1_denied(v int NOT NULL); CREATE USER s1_nobody WITHOUT LOGIN;");

            // Non-pooled, and REVERT in the same batch: an impersonating connection must never return to the pool (SQL 18059).
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand("EXECUTE AS USER='s1_nobody'; SELECT COUNT(*) FROM dbo.s1_denied; REVERT;", connection);
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteScalarAsync());

            Assert.Equal(229, error.Number);
            Assert.False(SqlStartupWait.IsTransient(error));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task A_changed_object_file_refuses_the_interactive_start_and_a_database_at_the_release_state_starts()
    {
        var database = new SqlDatabaseFixture();
        var root = Directory.CreateTempSubdirectory("etp-s1-objects-").FullName;
        try
        {
            await database.InitializeAsync();

            // The fixture was built from this installation's migrations and object files: it starts.
            Assert.Null(await StartupObjectsCheck.RefusalAsync(database.ConnectionString, database.MigrationDirectory));

            // A copy of the installation with one object file's text changed (a comment inside the definition).
            var objects = Directory.CreateDirectory(Path.Combine(root, "objects")).FullName;
            var migrations = Directory.CreateDirectory(Path.Combine(root, "migrations")).FullName;
            foreach (var file in Directory.GetFiles(database.MigrationDirectory)) File.Copy(file, Path.Combine(migrations, Path.GetFileName(file)));
            var source = SchemaObjectFiles.ObjectsDirectoryFor(database.MigrationDirectory);
            foreach (var file in Directory.GetFiles(source, "*.sql")) File.Copy(file, Path.Combine(objects, Path.GetFileName(file)));
            var changed = Path.Combine(objects, "20-dbo.v_cash_deposits_sent.sql");
            var text = File.ReadAllText(changed);
            Assert.Contains(SchemaObjectFiles.GrantsMarker, text, StringComparison.Ordinal);
            File.WriteAllText(changed, text.Replace(SchemaObjectFiles.GrantsMarker, "-- s1 startup check\r\n" + SchemaObjectFiles.GrantsMarker, StringComparison.Ordinal));

            var refusal = await StartupObjectsCheck.RefusalAsync(database.ConnectionString, migrations);

            Assert.NotNull(refusal);
            Assert.StartsWith(SchemaObjectsNotUpToDateException.Reason, refusal, StringComparison.Ordinal);
            Assert.Contains("1 report or view update waiting", refusal, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            await database.DisposeAsync();
        }
    }
}
