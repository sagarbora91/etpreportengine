using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
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

    [Fact]
    public async Task Commit_check_failure_is_COMMIT_OUTCOME_UNKNOWN_and_is_not_retried()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            // The check after the failed COMMIT connects to a database that does not exist, so it cannot answer.
            var unreachable = TestSqlConnections.ForDatabase("EtpPhase0Test_Missing_" + Guid.NewGuid().ToString("N"));
            var commits = 0;
            Func<SqlTransaction, CancellationToken, Task> LostReply(bool serverCommitted) => async (transaction, token) =>
            {
                commits++;
                if (serverCommitted) await transaction.CommitAsync(token);
                throw ImportCommitTimeoutSqlTests.SqlTimeout();
            };

            // The desktop batch path: one attempt, no automatic retry, and the failure reaches diagnostics.
            var scope = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path)).Scope;
            var batchReported = new List<FolderImportFailure>();
            await using (var desktop = new Etp.Reporting.Desktop.Modules.Imports.DesktopImportCoordinator(
                _ => new SqlServerImportPersistenceUseCase(database.ConnectionString, null, LostReply(false), unreachable),
                reportImportFailure: batchReported.Add))
            {
                var summary = await desktop.RunBatchAsync([path], database.ConnectionString, () => false,
                    () => new(scope.StoreCode!, scope.PeriodEnd!.Value, "Synthetic Owner", false, ""), _ => Task.CompletedTask);
                var batchFile = Assert.Single(summary.Files);
                Assert.Equal(BatchImportFileStatus.Failed, batchFile.Status);
                Assert.Equal(1, batchFile.Attempts);
                Assert.Equal(ImportCodes.CommitOutcomeUnknown, batchFile.ErrorCode);
            }
            Assert.Equal(1, commits);
            var batchFailure = Assert.Single(batchReported);
            Assert.Equal((FailureStage.Commit, CommitState.Unknown, -2), (batchFailure.Stage, batchFailure.CommitState!.Value, batchFailure.SqlErrorNumber!.Value));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")));

            // The folder import, where the server did commit: the result says it cannot tell.
            var reported = new List<FolderImportFailure>();
            var file = Assert.Single((await new FolderImportService(
                new SqlServerImportPersistenceUseCase(database.ConnectionString, null, LostReply(true), unreachable), reportFailure: reported.Add)
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal(2, commits);
            Assert.Equal("Failed", file.Status);
            Assert.Equal(CommitState.Unknown, file.CommitState);
            Assert.Equal("The database did not confirm whether this import was saved. Import the file again: if it was saved, it is reported as already imported.", file.Message);
            var failure = Assert.Single(reported);
            var exception = Assert.IsType<SqlException>(failure.Exception);
            Assert.Equal(-2, exception.Number);
            Assert.True(exception.Data.Contains(SqlTransactionGuard.CommitCheckFailureKey));
            Assert.Equal((FailureStage.Commit, CommitState.Unknown), (failure.Stage, failure.CommitState!.Value));
            Assert.Equal(file.BatchId, failure.BatchId);
            var classifier = new SqlImportFailureClassifier();
            Assert.False(classifier.IsTransient(exception));
            var detailed = classifier.DescribeDetailed(exception, FailureStage.Apply);
            Assert.Equal((ImportCodes.CommitOutcomeUnknown, FailureStage.Commit, (int?)-2), (detailed.Code, detailed.Stage, detailed.SqlNumber));

            // As the message promises, importing the file again finds it already held.
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches WHERE status='Completed'")));
            var again = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal("Duplicate", again.Status);
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exact_duplicate_commit_failure_is_judged_by_the_earlier_file_and_names_no_batch(bool checkAnswers)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            var first = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", first.Status);
            var fileId = Convert.ToInt64(await database.ExecuteAsync("SELECT MAX(import_file_id) FROM dbo.import_files"));

            // The orchestrator goes straight to the store, whose plan finds the exact earlier file and writes nothing.
            var store = new SqlServerTransactionalImportStore(database.ConnectionString)
            {
                Commit = (_, _) => throw ImportCommitTimeoutSqlTests.SqlTimeout(),
                CommitCheckConnectionString = checkAnswers ? null : TestSqlConnections.ForDatabase("EtpPhase0Test_Missing_" + Guid.NewGuid().ToString("N"))
            };
            var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(await new OpenXmlWorkbookReader().ReadAsync(path));
            var persist = new R025SqlImportOrchestrator(store).PersistAsync(accepted, expectedBusinessDate: accepted.Scope.PeriodEnd,
                expectedStoreCode: accepted.Scope.StoreCode, importedBy: "Synthetic Owner");
            if (checkAnswers)
                Assert.Equal(fileId, (await persist).ImportFileId);
            else
            {
                var failure = await Assert.ThrowsAsync<SqlException>(() => persist);
                Assert.Equal(CommitState.Unknown, SqlTransactionGuard.CommitStateOf(failure));
                Assert.True(SqlTransactionGuard.FailedAtCommit(failure));
                Assert.Null(SqlTransactionGuard.BatchIdOf(failure));
            }
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches")));
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")));
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Commit_check_waits_for_a_commit_still_in_flight(bool commits)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var batchId = Guid.NewGuid();
            await database.ExecuteAsync($"INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES('{batchId}','Processing',SYSUTCDATETIME())");

            // A second session completes the batch and holds its transaction open, as a COMMIT
            // still being hardened on a slow disk does.
            await using var writer = new SqlConnection(database.ConnectionString);
            await writer.OpenAsync();
            await using var transaction = (SqlTransaction)await writer.BeginTransactionAsync();
            await using (var complete = new SqlCommand("UPDATE dbo.import_batches SET status='Completed',completed_utc=SYSUTCDATETIME() WHERE import_batch_id=@id", writer, transaction))
            {
                complete.Parameters.AddWithValue("@id", batchId);
                Assert.Equal(1, await complete.ExecuteNonQueryAsync());
            }

            var check = new SqlServerTransactionalImportStore(database.ConnectionString).BatchCompletedAsync(batchId);
            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.False(check.IsCompleted);

            if (commits) await transaction.CommitAsync(); else await transaction.RollbackAsync();
            Assert.Equal(commits, await check.WaitAsync(TimeSpan.FromSeconds(60)));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Migration_commit_check_failure_is_rethrown_as_unknown()
    {
        var name = "EtpPhase0Test_CommitVerification_" + Guid.NewGuid().ToString("N");
        var connectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        try
        {
            var all = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
            var last = (await all.DiscoverAsync()).Select(migration => migration.Id).Max(StringComparer.Ordinal)!;
            await new SqlServerDatabaseBootstrapper(connectionString, new Before(all, last)).BootstrapAsync();

            var store = new SqlServerMigrationStore(connectionString)
            {
                Commit = async (transaction, token) =>
                {
                    await transaction.CommitAsync(token);
                    throw ImportCommitTimeoutSqlTests.SqlTimeout();
                },
                CommitCheckConnectionString = TestSqlConnections.ForDatabase("EtpPhase0Test_Missing_" + Guid.NewGuid().ToString("N"))
            };
            var failure = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(all, store).RunAsync());
            Assert.Equal(-2, failure.Number);
            Assert.True(SqlTransactionGuard.FailedAtCommit(failure));
            Assert.Equal(CommitState.Unknown, SqlTransactionGuard.CommitStateOf(failure));
            Assert.True(failure.Data.Contains(SqlTransactionGuard.CommitCheckFailureKey));

            // The server had committed: the next run finds the migration journalled and applies nothing.
            Assert.Empty(await new MigrationRunner(all, new SqlServerMigrationStore(connectionString)).RunAsync());
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
