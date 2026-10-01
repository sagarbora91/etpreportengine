using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// IF-014 (spec P0): after a COMMIT whose reply is lost, the import and the migration runner ask
// the database on a fresh connection whether the work landed, and report what it holds.
public sealed class CommitVerificationSqlTests
{
    [Fact]
    public async Task Commit_timeout_after_server_commit_reports_imported()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            async Task LostReply(SqlTransaction transaction, CancellationToken token)
            {
                await transaction.CommitAsync(token);
                throw ImportCommitTimeoutSqlTests.SqlTimeout();
            }
            var reported = new List<FolderImportFailure>();
            var file = Assert.Single((await new FolderImportService(
                new SqlServerImportPersistenceUseCase(database.ConnectionString, null, LostReply), reportFailure: reported.Add)
                .RunAsync(path, new("Synthetic Owner"))).Files);

            Assert.Equal("Imported", file.Status);
            Assert.Equal(CommitState.Committed, file.CommitState);
            Assert.True(file.NewRows > 0);
            Assert.Empty(reported);
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches WHERE status='Completed'")));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches WHERE status<>'Completed'")));
            var lines = Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            Assert.Equal(file.NewRows, lines);

            // The import really committed once: importing the same file again finds it held.
            var again = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.NotEqual("Failed", again.Status);
            Assert.NotEqual("Imported", again.Status);
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")));
            Assert.Equal(lines, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines")));
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Migration_commit_timeout_is_verified_against_journal(bool serverCommitted)
    {
        var name = "EtpPhase0Test_CommitVerification_" + Guid.NewGuid().ToString("N");
        var connectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        try
        {
            var all = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
            var last = (await all.DiscoverAsync()).Select(migration => migration.Id).Max(StringComparer.Ordinal)!;
            await new SqlServerDatabaseBootstrapper(connectionString, new Before(all, last)).BootstrapAsync();
            var before = await new SqlServerMigrationStore(connectionString).GetAppliedAsync();
            Assert.DoesNotContain(before, migration => migration.Id == last);

            var commits = 0;
            var store = new SqlServerMigrationStore(connectionString)
            {
                Commit = async (transaction, token) =>
                {
                    commits++;
                    if (serverCommitted) await transaction.CommitAsync(token);
                    throw ImportCommitTimeoutSqlTests.SqlTimeout();
                }
            };

            if (serverCommitted)
            {
                Assert.Equal([last], await new MigrationRunner(all, store).RunAsync());
                var after = await store.GetAppliedAsync();
                Assert.Equal(before.Count + 1, after.Count);
                Assert.Contains(after, migration => migration.Id == last);
            }
            else
            {
                var failure = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(all, store).RunAsync());
                Assert.Equal(-2, failure.Number);
                Assert.True(SqlTransactionGuard.FailedAtCommit(failure));
                Assert.Equal(CommitState.RolledBack, SqlTransactionGuard.CommitStateOf(failure));
                Assert.False(failure.Data.Contains(SqlTransactionGuard.CommitCheckFailureKey));
                Assert.Equal(before, await store.GetAppliedAsync());

                // Nothing of the migration landed, so a plain run applies it.
                Assert.Equal([last], await new MigrationRunner(all, new SqlServerMigrationStore(connectionString)).RunAsync());
            }
            Assert.Equal(1, commits);
        }
        finally { await DropAsync(name, connectionString); }
    }

    private sealed class Before(IMigrationSource source, string id) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, id) < 0).ToArray();
    }

    private static async Task DropAsync(string name, string connectionString)
    {
        if (!name.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name.");
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
        await command.ExecuteNonQueryAsync();
    }
}
