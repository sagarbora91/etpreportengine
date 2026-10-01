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
        var import = new SqlConnectionStringBuilder(LocalSqlConnectionPolicy.ValidateForImportTransaction(LocalSqlConnectionPolicy.Validate(Local)));
        Assert.Equal(LocalSqlConnectionPolicy.ImportTransactionTimeoutSeconds, import.ConnectTimeout);
        Assert.True(import.IntegratedSecurity);
        Assert.Equal(SqlConnectionEncryptOption.Optional, import.Encrypt);
        Assert.Equal("Test", import.InitialCatalog);
    }

    [Theory]
    [InlineData("Server=remotehost;Database=Test;Integrated Security=True")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;Encrypt=False")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=True;UID=secret")]
    [InlineData("Server=localhost;Database=Test;Integrated Security=False")]
    [InlineData("Server=localhost;Integrated Security=True")]
    public void Import_transaction_budget_keeps_every_local_connection_rule(string value) =>
        Assert.Throws<ArgumentException>(() => LocalSqlConnectionPolicy.ValidateForImportTransaction(value));

    [Fact]
    public void Sql_client_timeout_is_described_as_a_retryable_timeout_even_when_wrapped()
    {
        var classifier = new SqlImportFailureClassifier();
        var timeout = SqlError(-2);
        Assert.Equal(("IMPORT_TIMEOUT", "The import timed out and can be retried."), classifier.Describe(timeout));
        Assert.Equal("IMPORT_TIMEOUT", classifier.Describe(new InvalidOperationException("This SqlTransaction has completed.", timeout)).Code);
        Assert.True(classifier.IsTransient(timeout));
        Assert.Equal("IMPORT_PROCESSING_FAILED", classifier.Describe(SqlError(547)).Code);
        Assert.Equal("IMPORT_TIMEOUT", classifier.Describe(new TimeoutException()).Code);
        Assert.Equal("STORE_OVERRIDE_MISMATCH", classifier.Describe(new ImportSourceException("STORE_OVERRIDE_MISMATCH", "Mismatch.")).Code);
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
        var failure = Assert.Single(reported);
        Assert.Same(thrown, failure.Exception);
        Assert.Equal("R025 sales.xlsx", failure.FileName);
        Assert.Equal("Persist", failure.Stage);
        Assert.Equal("R025", failure.ReportCode);
        Assert.Equal("HEMW", failure.StoreCode);
        Assert.Equal(batchId, failure.BatchId);
        Assert.Equal(-2, failure.SqlErrorNumber);
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

        var unverified = SqlError(-2);
        var rethrown = await Assert.ThrowsAsync<SqlException>(() => SqlTransactionGuard.CommitOrVerifyAsync(1L,
            () => throw unverified, () => throw new InvalidOperationException("Already closed."),
            () => throw new TimeoutException("Check timed out.")));
        Assert.Same(unverified, rethrown);
        Assert.Contains("Already closed.", (string)rethrown.Data[SqlTransactionGuard.RollbackFailureKey]!);
        Assert.Contains("Check timed out.", (string)rethrown.Data[SqlTransactionGuard.CommitCheckFailureKey]!);
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
