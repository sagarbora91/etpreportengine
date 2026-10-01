using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>Every attempt keeps why it failed: code, stage, message, SQL error and issues (IF-017).</summary>
public sealed class ImportDiagnosticsSqlTests
{
    internal const string CustomerName = "Synthetic Customer Zeta";
    internal const string CustomerPhone = "9876500999";
    internal const string LoyaltyNumber = "ULP-SYNTH-777";

    [Fact]
    public async Task Failed_conflict_attempt_persists_code_stage_and_sample()
    {
        await WithDatabase(async database =>
        {
            var sample = await Sample();
            var first = Workbook(sample, "first.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 25))]);
            var moved = Workbook(sample, "moved.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 26))]);
            var service = Service(database, first, moved);
            Assert.Equal("Imported", Assert.Single((await service.RunFilesAsync(["first.xlsx"], new("Synthetic Owner"))).Files).Status);

            var failed = Assert.Single((await service.RunFilesAsync(["moved.xlsx"], new("Synthetic Owner"))).Files);
            Assert.Equal("Failed", failed.Status);
            Assert.Equal(ImportCodes.ImportConflict, failed.Failure!.Code);

            var attempt = await Single(database, """
                SELECT failure_code,failure_stage,commit_state,source_sha256,failure_message,exception_type,import_attempt_id
                FROM dbo.import_attempts WHERE file_name='moved.xlsx'
                """);
            Assert.Equal(ImportCodes.ImportConflict, attempt[0]);
            Assert.Equal("APPLY", attempt[1]);
            Assert.Equal("ROLLED_BACK", attempt[2]);
            Assert.Equal(moved.Sha256, attempt[3]);
            Assert.StartsWith("1 conflicting rows.", (string)attempt[4]!);
            Assert.Equal("ImportConflictException", attempt[5]);
            var sample1 = await Single(database, $"""
                SELECT code,severity,message,document_ref,source_row_number,occurrences FROM dbo.import_attempt_issues
                WHERE import_attempt_id={attempt[6]} AND code='IMPORT_CONFLICT'
                """);
            Assert.Equal("BLOCKER", sample1[1]);
            Assert.Equal("Invoice date differs. Review and request a controlled restatement.", sample1[2]);
            Assert.Contains("100000001", (string)sample1[3]!);
            Assert.Contains("2026-08-26", (string)sample1[3]!);
            Assert.Equal(2, sample1[4]);
            Assert.Equal(1, sample1[5]);

            var history = Assert.Single(await new SqlServerImportHistoryQuery(database.ConnectionString)
                .LoadAsync(new(new(2026, 8, 26), new(2026, 8, 26))));
            Assert.Equal(ImportCodes.ImportConflict, history.Result.Failure!.Code);
            Assert.Equal(FailureStage.Apply, history.Result.Failure.Stage);
            Assert.Equal(CommitState.RolledBack, history.Result.CommitState);
            Assert.Equal(attempt[4], history.Result.Message);
            Assert.Contains(history.Result.Diagnostics!, issue => issue.Code == ImportCodes.ImportConflict && issue.DocumentRef!.Contains("100000001"));
        });
    }

    [Fact]
    public async Task Locked_day_refusal_persists_SQL_51021()
    {
        await WithDatabase(async database =>
        {
            var sample = await Sample();
            var workbook = Workbook(sample, "locked.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 25))]);
            var store = new MatchedImportEnvelopeFactory().RequireAccepted(workbook).Scope.StoreCode!;
            await database.ExecuteAsync($"""
                INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc)
                VALUES('{store}','20260825','LOCKED',SUSER_SNAME(),SYSUTCDATETIME());
                """);
            var failed = Assert.Single((await Service(database, workbook).RunFilesAsync(["locked.xlsx"], new("Synthetic Owner"))).Files);
            Assert.Equal("Failed", failed.Status);

            var attempt = await Single(database, """
                SELECT failure_code,sql_error_number,failure_stage,commit_state,exception_type,failure_message
                FROM dbo.import_attempts WHERE file_name='locked.xlsx'
                """);
            Assert.Equal("SQL_51021", attempt[0]);
            Assert.Equal(51021, attempt[1]);
            Assert.Equal("APPLY", attempt[2]);
            Assert.Equal("ROLLED_BACK", attempt[3]);
            Assert.Equal("SqlException", attempt[4]);
            Assert.False(string.IsNullOrWhiteSpace((string)attempt[5]!));
            Assert.DoesNotContain("Database error", (string)attempt[5]!);
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
        });
    }

    [Fact]
    public async Task Pending_restatement_persists_request_code()
    {
        await WithDatabase(async database =>
        {
            var sample = await Sample();
            var original = Workbook(sample, "original.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 25))]);
            var replacement = Workbook(sample, "replacement.xlsx",
                [Row(sample, 2, "100000001", new(2026, 8, 25)), Row(sample, 3, "100000002", new(2026, 8, 25))]);
            var service = Service(database, original, replacement);
            await service.RunFilesAsync(["original.xlsx"], new("Synthetic Owner"));

            var pending = Assert.Single((await service.RunFilesAsync(["replacement.xlsx"],
                new("Synthetic Owner", RestatementEnabled: true, RestatementReason: "Synthetic correction"))).Files);
            Assert.Equal("RESTATEMENT_APPROVAL_PENDING", pending.Failure!.Code);

            var attempt = await Single(database, """
                SELECT failure_code,failure_stage,failure_message,commit_state,outcome
                FROM dbo.import_attempts WHERE file_name='replacement.xlsx'
                """);
            Assert.Equal("RESTATEMENT_APPROVAL_PENDING", attempt[0]);
            Assert.Equal("PLAN", attempt[1]);
            var request = await database.ExecuteAsync("SELECT approval_request_id FROM dbo.import_restatement_approvals");
            Assert.Contains($"Restatement request {request} is awaiting Owner approval.", (string)attempt[2]!);
            Assert.Equal(DBNull.Value, attempt[3]);
            Assert.Equal("Failed", attempt[4]);
        });
    }

    [Fact]
    public async Task Attempt_is_recorded_per_file_before_the_run_ends()
    {
        await WithDatabase(async database =>
        {
            var sample = await Sample();
            var first = Workbook(sample, "first.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 25))]);
            var second = Workbook(sample, "second.xlsx", [Row(sample, 2, "100000002", new(2026, 8, 26))]);
            int? recordedBeforeSecond = null;
            var persistence = new ObservedPersistence(new SqlServerImportPersistenceUseCase(database.ConnectionString), async request =>
            {
                if (request.AcceptedImport.Workbook.FileName == "second.xlsx")
                    recordedBeforeSecond = Convert.ToInt32(await database.ExecuteAsync(
                        "SELECT COUNT(*) FROM dbo.import_attempts WHERE file_name='first.xlsx' AND outcome='Imported'"));
            });
            var summary = await new FolderImportService(persistence, new Reader(first, second))
                .RunFilesAsync(["first.xlsx", "second.xlsx"], new("Synthetic Owner"));
            Assert.Equal(2, summary.Imported);
            Assert.Equal(1, recordedBeforeSecond);
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_attempts"));
            Assert.Equal(2, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_attempts WHERE commit_state='COMMITTED' AND import_batch_id IS NOT NULL AND source_sha256 IS NOT NULL"));
        });
    }

    [Fact]
    public async Task Persisted_details_contain_no_customer_values()
    {
        await WithDatabase(async database =>
        {
            var sample = await Sample();
            var first = Workbook(sample, "first.xlsx", [Row(sample, 2, "100000001", new(2026, 8, 25))]);
            var conflicting = Workbook(sample, "conflicting.xlsx",
                [Row(sample, 2, "100000001", new(2026, 8, 26)), Row(sample, 3, "100000003", new(2026, 8, 26), type: "XX")]);
            var invalid = Workbook(sample, "invalid.xlsx", [Row(sample, 2, "100000004", new(2026, 8, 27), quantity: CustomerName)]);
            var service = Service(database, first, conflicting, invalid);
            await service.RunFilesAsync(["first.xlsx"], new("Synthetic Owner"));
            var results = (await service.RunFilesAsync(["conflicting.xlsx", "invalid.xlsx"], new("Synthetic Owner"))).Files;
            Assert.All(results, result => Assert.Equal("Failed", result.Status));

            var attempts = (string)(await database.ExecuteAsync("""
                SELECT STRING_AGG(CONCAT(file_name,'|',failure_code,'|',failure_stage,'|',failure_message,'|',exception_type,'|',
                  summary_json,'|',CONVERT(nvarchar(max),diagnostics_json)),'#') FROM dbo.import_attempts
                """))!;
            var issues = (string)(await database.ExecuteAsync("""
                SELECT STRING_AGG(CONCAT(code,'|',message,'|',document_ref,'|',column_name),'#') FROM dbo.import_attempt_issues
                """))!;
            Assert.Contains("IMPORT_CONFLICT", issues);
            Assert.Contains("UNKNOWN_SALES_TRANSACTION_TYPE", issues);
            Assert.Contains("VALUE_INVALID", issues);
            Assert.Contains("\"skippedRows\":1", attempts);
            var history = JsonSerializer.Serialize(await new SqlServerImportHistoryQuery(database.ConnectionString)
                .LoadAsync(new(new(2026, 8, 1), new(2026, 8, 31))));
            foreach (var text in new[] { attempts, issues, history })
            {
                Assert.DoesNotContain(CustomerName, text);
                Assert.DoesNotContain(CustomerPhone, text);
                Assert.DoesNotContain(LoyaltyNumber, text);
            }
        });
    }

    [Fact]
    public async Task Issue_rows_are_capped_at_200_and_the_rest_are_counted_by_code()
    {
        await WithDatabase(async database =>
        {
            var issues = Enumerable.Range(1, 300).Select(row => new ImportIssue(ImportIssueSeverity.Warning, "UNKNOWN_SALES_TRANSACTION_TYPE",
                    "Unrecognised transaction type; this row was skipped.", row, "TRANS_TYPE"))
                .Concat(Enumerable.Range(301, 150).Select(row => new ImportIssue(ImportIssueSeverity.Blocker, "VALUE_INVALID",
                    "Value cannot be converted to Decimal.", row, "QTY")))
                .ToArray();
            await new SqlServerImportHistoryQuery(database.ConnectionString).RecordAttemptAsync(
                new("many.xlsx", "R025", "HEMW", new(2026, 8, 25), new(2026, 8, 25), "Failed", Diagnostics: issues));

            Assert.Equal(200, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_attempt_issues"));
            Assert.Equal(450, await database.ExecuteAsync("SELECT SUM(occurrences) FROM dbo.import_attempt_issues"));
            Assert.Equal(198, await database.ExecuteAsync("SELECT MAX(seq) FROM dbo.import_attempt_issues WHERE occurrences=1 AND source_row_number IS NOT NULL"));
            var counted = await Single(database, """
                SELECT STRING_AGG(CONCAT(code,':',occurrences,':',severity),',') WITHIN GROUP(ORDER BY seq)
                FROM dbo.import_attempt_issues WHERE source_row_number IS NULL
                """);
            Assert.Equal("UNKNOWN_SALES_TRANSACTION_TYPE:102:WARNING,VALUE_INVALID:150:BLOCKER", counted[0]);
            var summary = (string)(await database.ExecuteAsync("SELECT summary_json FROM dbo.import_attempts"))!;
            Assert.Contains("\"skippedRows\":300", summary);
        });
    }

    [Fact]
    public async Task Every_outcome_is_accepted()
    {
        await WithDatabase(async database =>
        {
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            foreach (var outcome in ImportAttemptOutcomes.All)
                await history.RecordAttemptAsync(new($"{ImportAttemptOutcomes.All.ToList().IndexOf(outcome)}.xlsx", "R025", "HEMW",
                    new(2026, 8, 25), new(2026, 8, 25), outcome) { Evidence = EvidenceState.AlreadyHeld, CommitState = CommitState.Committed });
            Assert.Equal(13, await database.ExecuteAsync("SELECT COUNT(DISTINCT outcome) FROM dbo.import_attempts WHERE evidence_state='ALREADY_HELD'"));
            var loaded = await history.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25)));
            Assert.Equal(ImportAttemptOutcomes.All.Order(), loaded.Select(entry => entry.Result.Status).Order());
            Assert.All(loaded, entry => Assert.Equal(EvidenceState.AlreadyHeld, entry.Result.Evidence));
        });
    }

    [Fact]
    public async Task Database_errors_are_classified_by_number()
    {
        await WithDatabase(async database =>
        {
            await database.ExecuteAsync("EXEC(N'CREATE PROCEDURE dbo.synthetic_divide AS SELECT 1/0;')");
            var own = SqlImportFailures.Describe(await Fail(database, "THROW 51999,'Synthetic refusal written by our code.',1;"), FailureStage.Apply);
            Assert.Equal("SQL_51999", own.Code);
            Assert.Equal("Synthetic refusal written by our code.", own.SafeMessage);
            var other = SqlImportFailures.Describe(await Fail(database, "EXEC dbo.synthetic_divide;"), FailureStage.Apply);
            Assert.Equal("SQL_8134", other.Code);
            Assert.Equal(8134, other.SqlNumber);
            Assert.Matches(@"^Database error 8134 in (dbo\.)?synthetic_divide, line \d+\.$", other.SafeMessage);
            var timeout = SqlImportFailures.Describe(await Fail(database, "WAITFOR DELAY '00:00:05';", timeoutSeconds: 1), FailureStage.Commit);
            Assert.Equal(ImportCodes.ImportTimeout, timeout.Code);
            Assert.Equal(-2, timeout.SqlNumber);
            Assert.Equal(FailureStage.Commit, timeout.Stage);
            var wrapped = SqlImportFailures.Describe(new InvalidOperationException("outer", await Fail(database, "EXEC dbo.synthetic_divide;")), FailureStage.Apply);
            Assert.Equal("SQL_8134", wrapped.Code);
        });

        static async Task<SqlException> Fail(SqlDatabaseFixture database, string sql, int timeoutSeconds = 30)
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
            return await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        }
    }

    internal static async Task WithDatabase(Func<SqlDatabaseFixture, Task> test)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await test(database);
        }
        finally { await database.DisposeAsync(); }
    }

    internal static async Task<object?[]> Single(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "No row.");
        var values = new object?[reader.FieldCount];
        reader.GetValues(values!);
        Assert.False(await reader.ReadAsync(), "More than one row.");
        return values;
    }

    internal static Task<WorkbookSnapshot> Sample() => new OpenXmlWorkbookReader().ReadAsync(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single());

    // A row of the synthetic R025 fixture carrying distinctive synthetic customer values.
    internal static WorkbookRow Row(WorkbookSnapshot sample, int number, string invoice, DateOnly date, string type = "INV", object? quantity = null)
    {
        var headers = sample.Sheets[0].Headers.ToList();
        var cells = sample.Sheets[0].Rows[0].Cells.ToArray();
        cells[headers.IndexOf("INVNUMBER")] = new(invoice);
        cells[headers.IndexOf("INVDATE")] = new(date);
        cells[headers.IndexOf("TRANS_TYPE")] = new(type);
        cells[headers.IndexOf("CUSTOMERNAME")] = new(CustomerName);
        cells[headers.IndexOf("CONTACTNO")] = new(CustomerPhone);
        cells[headers.IndexOf("ULPNUMBER")] = new(LoyaltyNumber);
        if (quantity is not null) cells[headers.IndexOf("QTY")] = new(quantity);
        return new(number, cells);
    }

    internal static WorkbookSnapshot Workbook(WorkbookSnapshot sample, string fileName, WorkbookRow[] rows) => sample with
    {
        FileName = fileName, Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        Sheets = [sample.Sheets[0] with { Rows = rows }]
    };

    internal static FolderImportService Service(SqlDatabaseFixture database, params WorkbookSnapshot[] workbooks) =>
        new(new SqlServerImportPersistenceUseCase(database.ConnectionString), new Reader(workbooks));

    private sealed class Reader(params WorkbookSnapshot[] workbooks) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(workbooks.Single(workbook => workbook.FileName == Path.GetFileName(path)));
    }

    private sealed class ObservedPersistence(SqlServerImportPersistenceUseCase inner,
        Func<ImportPersistenceRequest<MatchedImportEnvelope>, Task> beforePersist) : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportAttemptRecorder
    {
        public Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default) =>
            inner.RecordAttemptAsync(result, cancellationToken);
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => inner.ExistsByHashAsync(hash, cancellationToken);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            inner.ExistsInScopeAsync(hash, report, store, start, end, cancellationToken);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) =>
            inner.FindCurrentImportFileIdAsync(report, store, date, cancellationToken);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            inner.PrepareRestatementAsync(request, cancellationToken);
        public async Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            await beforePersist(request);
            return await inner.PersistAsync(request, cancellationToken);
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            inner.LoadOutcomeByHashAsync(hash, cancellationToken);
        public Task<ImportRowOutcome> LoadOutcomeInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            inner.LoadOutcomeInScopeAsync(hash, report, store, start, end, cancellationToken);
    }
}
