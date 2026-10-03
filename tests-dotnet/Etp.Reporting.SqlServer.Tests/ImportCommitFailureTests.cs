using System.Reflection;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.Tests;

// IF-014: an import COMMIT that outlasts its timeout must not be reported as a generic
// failure, must not be hidden by the rollback that follows, and must reach diagnostics.
public sealed class ImportCommitFailureTests
{
    private const string Local = "Server=.\\SQLEXPRESS;Database=Test;Integrated Security=True";

    [Fact]
    public void Import_transactions_get_their_own_connect_timeout_while_other_connections_keep_the_five_second_cap()
    {
        Assert.Equal(5, new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.Validate(Local + ";Connect Timeout=0")).ConnectTimeout);
        Assert.Equal(5, new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.Validate(Local + ";Connect Timeout=300")).ConnectTimeout);
        var import = new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.ValidateWithCommitBudget(LocalSqlConnectionPolicy.Validate(Local)));
        Assert.Equal(LocalSqlConnectionPolicy.CommitBudgetSeconds, import.ConnectTimeout);
        Assert.True(import.IntegratedSecurity);
        Assert.True(import.Pooling);
        Assert.Equal(SqlConnectionEncryptOption.Optional, import.Encrypt);
        Assert.Equal("Test", import.InitialCatalog);
        Assert.Equal(5, new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.Validate(LocalSqlConnectionPolicy.ValidateWithCommitBudget(Local))).ConnectTimeout);
    }

    [Fact]
    public void The_check_after_a_failed_commit_never_reuses_a_pooled_session()
    {
        var check = new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.ValidateForCommitCheck(LocalSqlConnectionPolicy.ValidateWithCommitBudget(Local)));
        Assert.False(check.Pooling);
        // A stopped server must not hold the check for the whole commit budget; its query gets the budget instead.
        Assert.Equal(LocalSqlConnectionPolicy.CommitCheckConnectSeconds, check.ConnectTimeout);
        Assert.True(check.ConnectTimeout < LocalSqlConnectionPolicy.CommitBudgetSeconds);
        Assert.True(check.IntegratedSecurity);
        Assert.Equal(SqlConnectionEncryptOption.Optional, check.Encrypt);
        Assert.Equal("Test", check.InitialCatalog);
    }

    [Theory]
    [InlineData("Server=remotehost;Database=Test;Integrated Security=True")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=False")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;UID=secret")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=False")]
    [InlineData("Server=localhost;Integrated Security=True")]
    public void Import_transaction_budget_keeps_every_local_connection_rule(string value)
    {
        Assert.Throws<ArgumentException>(() => LocalSqlConnectionPolicy.ValidateWithCommitBudget(value));
        Assert.Throws<ArgumentException>(() => LocalSqlConnectionPolicy.ValidateForCommitCheck(value));
    }

    [Fact]
    public void Sql_client_timeout_is_described_as_a_timeout_even_when_wrapped()
    {
        var classifier = new SqlImportFailureClassifier();
        var timeout = SqlError(-2);
        Assert.Equal(("IMPORT_TIMEOUT", "The import timed out and can be retried."), classifier.Describe(timeout));
        Assert.Equal("IMPORT_TIMEOUT", classifier.Describe(new InvalidOperationException("This SqlTransaction has completed.", timeout)).Code);
        // Any other database error keeps its number (IF-017), never its text, and is not a timeout.
        Assert.Equal(ImportCodes.Sql(547), classifier.Describe(SqlError(547)).Code);
        Assert.Equal("IMPORT_TIMEOUT", classifier.Describe(new TimeoutException()).Code);
        Assert.Equal("STORE_OVERRIDE_MISMATCH", classifier.Describe(new ImportSourceException("STORE_OVERRIDE_MISMATCH", "Mismatch.")).Code);
    }

    [Fact]
    public void Only_a_commit_timeout_found_rolled_back_is_retried_automatically()
    {
        var classifier = new SqlImportFailureClassifier();
        // A timeout during the apply would re-run the whole heavy import; it is reported, not retried.
        Assert.False(classifier.IsTransient(SqlError(-2)));
        var beforeCommit = SqlError(-2);
        beforeCommit.Data[SqlTransactionGuard.CommitStateKey] = CommitState.RolledBack;
        Assert.False(classifier.IsTransient(beforeCommit));
        Assert.False(classifier.IsTransient(SqlError(1205)));

        var rolledBack = SqlError(-2);
        rolledBack.Data[SqlTransactionGuard.CommitFailedKey] = true;
        rolledBack.Data[SqlTransactionGuard.CommitStateKey] = CommitState.RolledBack;
        Assert.True(classifier.IsTransient(rolledBack));
        Assert.True(classifier.IsTransient(new InvalidOperationException("Wrapped.", rolledBack)));

        var committed = SqlError(-2);
        SqlTransactionGuard.MarkCommitted(committed, null);
        Assert.False(classifier.IsTransient(committed));
        var committedIo = new IOException("Locked.");
        SqlTransactionGuard.MarkCommitted(committedIo, null);
        Assert.False(classifier.IsTransient(committedIo));

        // Non-SQL failures keep the import classifier's rule.
        Assert.True(classifier.IsTransient(new IOException("Locked.")));
        Assert.True(classifier.IsTransient(new TimeoutException()));
    }

    [Fact]
    public void A_failure_after_the_work_committed_says_it_is_saved_at_the_commit_stage()
    {
        var classifier = new SqlImportFailureClassifier();
        var batchId = Guid.NewGuid();
        var timeout = SqlError(-2);
        SqlTransactionGuard.MarkCommitted(timeout, batchId);
        Assert.Equal(CommitState.Committed, SqlTransactionGuard.CommitStateOf(timeout));
        Assert.Equal(batchId, SqlTransactionGuard.BatchIdOf(timeout));
        Assert.False(SqlTransactionGuard.FailedAtCommit(timeout));

        var (code, message) = classifier.Describe(timeout);
        Assert.Equal(ImportCodes.ImportTimeout, code);
        Assert.Contains("was saved", message);
        Assert.DoesNotContain("can be retried", message);
        Assert.Equal(FailureStage.Commit, classifier.DescribeDetailed(timeout, FailureStage.Apply).Stage);

        // A state the failure already carries is kept.
        var unknown = SqlError(-2);
        unknown.Data[SqlTransactionGuard.CommitStateKey] = CommitState.Unknown;
        SqlTransactionGuard.MarkCommitted(unknown, batchId);
        Assert.Equal(CommitState.Unknown, SqlTransactionGuard.CommitStateOf(unknown));

        var created = FolderImportFailure.Create("R025 sales.xlsx", FailureStage.Apply, "R025", "HEMW", null, timeout);
        Assert.Equal((FailureStage.Commit, CommitState.Committed, batchId, -2),
            (created.Stage, created.CommitState!.Value, created.BatchId!.Value, created.SqlErrorNumber!.Value));
        Assert.Equal(FailureStage.Read, FolderImportFailure.Create("x.xlsx", FailureStage.Read, null, null, null, new IOException()).Stage);
    }

    [Fact]
    public async Task A_failure_reading_back_a_committed_import_is_reported_as_saved_not_as_a_failed_import()
    {
        var batchId = Guid.NewGuid();
        var timeout = SqlError(-2);
        var reported = new List<FolderImportFailure>();
        var summary = await new FolderImportService(new SavedThenUnreadablePersistence(batchId, timeout), new Reader(Sales), reportFailure: reported.Add)
            .RunFilesAsync(["R025 sales.xlsx"], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Contains("was saved", file.Message);
        Assert.Equal(CommitState.Committed, file.CommitState);
        Assert.Equal(batchId, file.BatchId);
        var failure = Assert.Single(reported);
        Assert.Same(timeout, failure.Exception);
        Assert.Equal(FailureStage.Commit, failure.Stage);
        Assert.Equal(CommitState.Committed, failure.CommitState);
        Assert.Equal(batchId, failure.BatchId);
        Assert.False(new SqlImportFailureClassifier().IsTransient(failure.Exception));
    }

    [Fact]
    public void Commit_whose_outcome_could_not_be_checked_is_COMMIT_OUTCOME_UNKNOWN_at_the_commit_stage_and_not_retried()
    {
        var classifier = new SqlImportFailureClassifier();
        var unknown = SqlError(-2);
        unknown.Data[SqlTransactionGuard.CommitFailedKey] = true;
        unknown.Data[SqlTransactionGuard.CommitStateKey] = CommitState.Unknown;

        var (code, message) = classifier.Describe(unknown);
        Assert.Equal(ImportCodes.CommitOutcomeUnknown, code);
        Assert.Contains("did not confirm whether this import was saved", message);
        Assert.Equal(code, classifier.Describe(new InvalidOperationException("Wrapped.", unknown)).Code);
        Assert.False(classifier.IsTransient(unknown));
        Assert.Equal(new ImportFailure(ImportCodes.CommitOutcomeUnknown, FailureStage.Commit, message, nameof(SqlException), -2),
            classifier.DescribeDetailed(unknown, FailureStage.Apply));

        // The check found nothing: the COMMIT failed, nothing landed, and the timeout may be retried.
        var rolledBack = SqlError(-2);
        rolledBack.Data[SqlTransactionGuard.CommitFailedKey] = true;
        rolledBack.Data[SqlTransactionGuard.CommitStateKey] = CommitState.RolledBack;
        Assert.Equal(ImportCodes.ImportTimeout, classifier.Describe(rolledBack).Code);
        Assert.True(classifier.IsTransient(rolledBack));
        Assert.Equal(FailureStage.Commit, classifier.DescribeDetailed(rolledBack, FailureStage.Apply).Stage);

        // A failure before the COMMIT keeps the caller's stage.
        var before = SqlError(547);
        before.Data[SqlTransactionGuard.CommitStateKey] = CommitState.RolledBack;
        Assert.Equal(new ImportFailure(ImportCodes.Sql(547), FailureStage.Apply, classifier.Describe(before).SafeMessage, nameof(SqlException), 547),
            classifier.DescribeDetailed(before, FailureStage.Apply));
        var refusal = classifier.DescribeDetailed(new ImportSourceException("STORE_OVERRIDE_MISMATCH", "Mismatch."), FailureStage.Scope);
        Assert.Equal(("STORE_OVERRIDE_MISMATCH", FailureStage.Scope, (int?)null), (refusal.Code, refusal.Stage, refusal.SqlNumber));
    }

    // History must keep the commit-outcome messages, whatever SQL error hid the outcome: the generic
    // "failed with a database error" would tell the Owner a possibly saved import failed.
    [Theory]
    [InlineData(-2)]
    [InlineData(4060)]
    [InlineData(10054)]
    public void History_stores_and_shows_the_commit_outcome_messages_whatever_the_sql_number(int number)
    {
        var classifier = new SqlImportFailureClassifier();
        var unknown = SqlError(number);
        unknown.Data[SqlTransactionGuard.CommitFailedKey] = true;
        unknown.Data[SqlTransactionGuard.CommitStateKey] = CommitState.Unknown;
        var committed = SqlError(number);
        SqlTransactionGuard.MarkCommitted(committed, Guid.NewGuid());
        foreach (var (exception, expected) in new[]
        {
            (unknown, ImportDiagnosticCatalogue.CommitOutcomeUnknownMessage),
            (committed, ImportDiagnosticCatalogue.SavedNotReadBackMessage)
        })
        {
            var failure = classifier.DescribeDetailed(exception, FailureStage.Apply);
            Assert.Equal(number, failure.SqlNumber);
            Assert.Equal(expected, failure.SafeMessage);
            // import_attempts.failure_message, and History reading it back through the same filter.
            var stored = SqlServerImportHistoryQuery.StoredFailureMessage(failure);
            Assert.Equal(expected, stored);
            Assert.Equal(expected, ImportDiagnosticCatalogue.SafeFailureMessage(failure.Code, stored, failure.SqlNumber));
        }
        // A stored COMMIT_OUTCOME_UNKNOWN without text still says to import the file again.
        Assert.Equal(ImportDiagnosticCatalogue.CommitOutcomeUnknownMessage,
            ImportDiagnosticCatalogue.SafeFailureMessage(ImportCodes.CommitOutcomeUnknown, null, null));
        // Any other text of a non-THROW database error still becomes the generic form.
        Assert.Equal("The import failed with a database error.",
            ImportDiagnosticCatalogue.SafeFailureMessage(ImportCodes.Sql(number), "Synthetic SQL error.", number));
        Assert.Null(SqlServerImportHistoryQuery.StoredFailureMessage(null));
    }

    [Fact]
    public async Task Failed_persistence_reports_the_real_exception_with_file_and_batch_but_shows_a_safe_message()
    {
        var batchId = Guid.NewGuid();
        var timeout = SqlError(-2);
        var thrown = new InvalidOperationException("This SqlTransaction has completed.", timeout);
        thrown.Data[SqlTransactionGuard.ImportBatchIdKey] = batchId;
        var reported = new List<FolderImportFailure>();
        var summary = await new FolderImportService(new ThrowingPersistence(thrown), new Reader(Sales), reportFailure: reported.Add)
            .RunFilesAsync(["R025 sales.xlsx"], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Equal("The import timed out and can be retried.", file.Message);
        Assert.Equal(batchId, file.BatchId);
        Assert.Null(file.CommitState);
        var failure = Assert.Single(reported);
        Assert.Same(thrown, failure.Exception);
        Assert.Equal("R025 sales.xlsx", failure.FileName);
        Assert.Equal(FailureStage.Apply, failure.Stage);
        Assert.Equal("R025", failure.ReportCode);
        Assert.Equal("HEMW", failure.StoreCode);
        Assert.Equal(batchId, failure.BatchId);
        Assert.Equal(-2, failure.SqlErrorNumber);
        Assert.Null(failure.CommitState);
    }

    [Theory]
    [InlineData(CommitState.Unknown, "The database did not confirm whether this import was saved. Import the file again: if it was saved, it is reported as already imported.")]
    [InlineData(CommitState.RolledBack, "The import timed out and can be retried.")]
    public async Task Failed_commit_is_reported_with_its_commit_state_at_the_commit_stage(CommitState state, string message)
    {
        var batchId = Guid.NewGuid();
        var timeout = SqlError(-2);
        timeout.Data[SqlTransactionGuard.ImportBatchIdKey] = batchId;
        timeout.Data[SqlTransactionGuard.CommitFailedKey] = true;
        timeout.Data[SqlTransactionGuard.CommitStateKey] = state;
        var reported = new List<FolderImportFailure>();
        var summary = await new FolderImportService(new ThrowingPersistence(timeout), new Reader(Sales), reportFailure: reported.Add)
            .RunFilesAsync(["R025 sales.xlsx"], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Equal(message, file.Message);
        Assert.Equal(state, file.CommitState);
        Assert.Equal(batchId, file.BatchId);
        var failure = Assert.Single(reported);
        Assert.Equal(FailureStage.Commit, failure.Stage);
        Assert.Equal(state, failure.CommitState);
        Assert.Equal(batchId, failure.BatchId);
    }

    [Theory]
    [InlineData("Imported", CommitState.Committed)]
    [InlineData("Duplicate content", CommitState.Committed)]
    [InlineData("Duplicate", null)]
    public async Task A_saved_import_is_committed_unless_an_earlier_import_already_held_the_file(string status, CommitState? state)
    {
        var summary = await new FolderImportService(new SavingPersistence(status), new Reader(Sales)).RunFilesAsync(["R025 sales.xlsx"], new("tester"));
        var file = Assert.Single(summary.Files);
        Assert.Equal(status, file.Status);
        Assert.Equal(state, file.CommitState);
    }

    [Fact]
    public async Task Explained_refusals_are_not_logged_and_a_failing_sink_does_not_change_the_outcome()
    {
        var refusals = new List<FolderImportFailure>();
        var refused = await new FolderImportService(new ThrowingPersistence(new ImportSourceException("IMPORT_CONFLICT", "2 conflicting rows.")),
            new Reader(Sales), reportFailure: refusals.Add).RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal("2 conflicting rows.", Assert.Single(refused.Files).Message);
        Assert.Empty(refusals);

        var failed = await new FolderImportService(new ThrowingPersistence(new InvalidOperationException("boom")), new Reader(Sales),
            reportFailure: _ => throw new IOException("Log unavailable.")).RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal("Failed", Assert.Single(failed.Files).Status);
    }

    [Fact]
    public async Task Commit_failure_returns_the_result_when_the_work_is_found_committed()
    {
        var steps = new List<string>();
        var result = await SqlTransactionGuard.CommitOrVerifyAsync(42L,
            () => { steps.Add("commit"); throw SqlError(-2); },
            () => { steps.Add("release"); return Task.CompletedTask; },
            () => { steps.Add("verify"); return Task.FromResult(true); });
        Assert.Equal(42L, result);
        Assert.Equal(["commit", "release", "verify"], steps);
    }

    [Fact]
    public async Task Commit_failure_is_rethrown_unchanged_when_the_work_is_missing_or_cannot_be_checked()
    {
        var original = SqlError(-2);
        var missing = await Assert.ThrowsAsync<SqlException>(() => SqlTransactionGuard.CommitOrVerifyAsync(1L,
            () => throw original, () => Task.CompletedTask, () => Task.FromResult(false)));
        Assert.Same(original, missing);
        Assert.Equal(CommitState.RolledBack, SqlTransactionGuard.CommitStateOf(missing));
        Assert.True(SqlTransactionGuard.FailedAtCommit(missing));
        Assert.False(missing.Data.Contains(SqlTransactionGuard.CommitCheckFailureKey));

        var unverified = SqlError(-2);
        var rethrown = await Assert.ThrowsAsync<SqlException>(() => SqlTransactionGuard.CommitOrVerifyAsync(1L,
            () => throw unverified, () => throw new InvalidOperationException("Already closed."),
            () => throw new TimeoutException("Check timed out.")));
        Assert.Same(unverified, rethrown);
        Assert.Contains("Already closed.", (string)rethrown.Data[SqlTransactionGuard.RollbackFailureKey]!);
        Assert.Contains("Check timed out.", (string)rethrown.Data[SqlTransactionGuard.CommitCheckFailureKey]!);
        Assert.Equal(CommitState.Unknown, SqlTransactionGuard.CommitStateOf(new InvalidOperationException("Wrapped.", rethrown)));
        Assert.True(SqlTransactionGuard.FailedAtCommit(rethrown));
    }

    [Fact]
    public async Task A_cancelled_commit_is_not_checked_and_a_failure_before_commit_is_rolled_back()
    {
        var checks = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() => SqlTransactionGuard.CommitOrVerifyAsync(1L,
            () => throw new OperationCanceledException(), () => { checks++; return Task.CompletedTask; },
            () => { checks++; return Task.FromResult(true); }));
        Assert.Equal(0, checks);

        var before = SqlError(547);
        SqlTransactionGuard.MarkRolledBack(before);
        Assert.Equal(CommitState.RolledBack, SqlTransactionGuard.CommitStateOf(before));
        Assert.False(SqlTransactionGuard.FailedAtCommit(before));

        // A COMMIT whose outcome is unknown stays unknown when the caller then marks its failure.
        var unknown = SqlError(-2);
        unknown.Data[SqlTransactionGuard.CommitStateKey] = CommitState.Unknown;
        SqlTransactionGuard.MarkRolledBack(unknown);
        Assert.Equal(CommitState.Unknown, SqlTransactionGuard.CommitStateOf(unknown));
        Assert.Null(SqlTransactionGuard.CommitStateOf(new InvalidOperationException("Never in a transaction.")));
    }

    [Fact]
    public async Task A_successful_commit_does_not_check_or_release()
    {
        var checks = 0;
        Assert.Equal(7, await SqlTransactionGuard.CommitOrVerifyAsync(7, () => Task.CompletedTask,
            () => { checks++; return Task.CompletedTask; }, () => { checks++; return Task.FromResult(false); }));
        Assert.Equal(0, checks);
    }

    [Fact]
    public async Task Rollback_failure_never_replaces_the_original_exception()
    {
        var original = SqlError(-2);
        await SqlTransactionGuard.RollBackAsync(original, true, () => throw new InvalidOperationException("This SqlTransaction has completed."));
        Assert.Contains("This SqlTransaction has completed.", (string)original.Data[SqlTransactionGuard.RollbackFailureKey]!);

        var rolledBack = false;
        var completed = new InvalidOperationException("Commit lost.");
        await SqlTransactionGuard.RollBackAsync(completed, false, () => { rolledBack = true; return Task.CompletedTask; });
        Assert.False(rolledBack);
        Assert.False(completed.Data.Contains(SqlTransactionGuard.RollbackFailureKey));
    }

    // SqlException has no public constructor; build one through SqlClient's own factory.
    internal static SqlException SqlError(int number)
    {
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        var errorConstructor = typeof(SqlError).GetConstructors(Any).OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = errorConstructor.GetParameters().Select<ParameterInfo, object?>((parameter, index) => index == 0 ? number : parameter.ParameterType switch
        {
            var type when type == typeof(string) => "Synthetic SQL error.",
            var type when type == typeof(byte) => (byte)0,
            var type when type == typeof(int) => 0,
            var type when type == typeof(uint) => 0u,
            _ => null
        }).ToArray();
        var error = (SqlError)errorConstructor.Invoke(arguments);
        var errors = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructors(Any).Single(c => c.GetParameters().Length == 0).Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", Any)!.Invoke(errors, [error]);
        var create = typeof(SqlException).GetMethods(Any).First(method => method.Name == "CreateException" &&
            method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(SqlErrorCollection), typeof(string)]));
        return (SqlException)create.Invoke(null, [errors, "16.0"])!;
    }

    private static WorkbookSnapshot Sales(string path)
    {
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = 20260825, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
        };
        var row = new WorkbookRow(2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());
        return new(path, 1, new string('c', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, [row])]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class SavingPersistence(string status) : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportPersistenceResult("R025", status == "Imported" ? 1 : 0) { Status = status, AlreadyPresentRows = status == "Imported" ? 0 : 1 });
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(1, 1, 0, 0));
    }

    private sealed class SavedThenUnreadablePersistence(Guid batchId, Exception failure) : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportPersistenceResult("R025", 1) { BatchId = batchId });
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromException<ImportRowOutcome>(failure);
        public Task<ImportRowOutcome> LoadOutcomeInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromException<ImportRowOutcome>(failure);
    }

    private sealed class ThrowingPersistence(Exception failure) : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            Task.FromException<ImportPersistenceResult>(failure);
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
