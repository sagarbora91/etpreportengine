using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.10.1 (decision 30, 1101-DAILY-INPUTS-DESIGN.md sections 3 and 6): the folder import recognises a shop PC's
/// daily-inputs workbook by its content, before matching, parses it and hands it to the applier; the file never
/// reaches the matcher or the import store, and its attempt is recorded as family D001 like any other file.
/// </summary>
public sealed class DailyInputsFolderImportTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static readonly string DailyName = $"ETP-Daily-Inputs-HEMW-RETAIL-{Today:yyyy-MM-dd}.xlsx";
    private static readonly string DailySha = new('d', 64);

    [Fact]
    public async Task A_daily_inputs_file_is_applied_and_never_reaches_matching_or_the_import_store()
    {
        var persistence = new RecordingPersistence();
        var applier = new FakeApplier(new DailyInputsApplyResult("Imported", 2, 0, null, "2 daily input(s) saved; 0 unchanged.", []));
        var service = new FolderImportService(persistence,
            new Reader(_ => Daily(("WALK_INS", 12m), ("EXPENSES", 250.5m))), dailyInputs: applier);

        var summary = await service.RunFilesAsync([DailyName], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal(("D001", "HEMW", Today, Today, "Imported"), (file.ReportCode, file.StoreCode, file.PeriodStart, file.PeriodEnd, file.Status));
        Assert.Equal((2, 2, 0), (file.RowsProcessed, file.NewRows, file.AlreadyPresentRows));
        Assert.Equal(DailySha, file.SourceSha256);
        Assert.Equal(EvidenceState.NotAttempted, file.Evidence);
        Assert.Null(file.Failure);
        Assert.Equal(1, summary.Imported);
        // Never matched, never persisted as a report: no scope lookup and no persist call.
        Assert.Empty(persistence.Requests);
        Assert.Equal(0, persistence.ScopeLookups);
        Assert.Same(file, Assert.Single(persistence.Attempts));

        var (document, user) = Assert.Single(applier.Calls);
        Assert.Equal("tester", user);
        Assert.Equal((DailyInputsKind.Retail, "HEMW", Today), (document.Kind, document.StoreCode, document.BusinessDate));
        Assert.Equal(new[] { "WALK_INS", "EXPENSES" }, document.Values.Select(value => value.FieldCode));
    }

    [Theory]
    [InlineData("Already present")]
    [InlineData("Duplicate")]
    public async Task An_unchanged_or_repeated_file_keeps_the_applier_outcome_and_is_not_a_failure(string outcome)
    {
        var persistence = new RecordingPersistence();
        var failures = new List<FolderImportFailure>();
        var service = new FolderImportService(persistence, new Reader(_ => Daily(("WALK_INS", 12m))), reportFailure: failures.Add,
            dailyInputs: new FakeApplier(new DailyInputsApplyResult(outcome, 0, 1, null, "Nothing was changed.", [])));

        var summary = await service.RunFilesAsync([DailyName], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal((outcome, "D001", 0, 1), (file.Status, file.ReportCode, file.NewRows, file.AlreadyPresentRows));
        Assert.Equal(1, summary.Duplicates);
        Assert.Equal(0, summary.Failed);
        Assert.Empty(failures);
        Assert.Empty(service.FailedPaths);
        Assert.Single(persistence.Attempts);
    }

    [Fact]
    public async Task A_refused_apply_fails_the_file_records_the_attempt_and_writes_a_register_row()
    {
        const string code = "DAILY_INPUTS_DAY_FINALISED";
        const string message = "This business day is finalised, so its manual entries cannot change. Reopen the day, then put the file in the watch folder again.";
        var persistence = new RecordingPersistence();
        var failures = new List<FolderImportFailure>();
        var service = new FolderImportService(persistence, new Reader(_ => Daily(("WALK_INS", 12m))), reportFailure: failures.Add,
            dailyInputs: new FakeApplier(new DailyInputsApplyResult("Failed", 0, 0, code, message, [new(null, null, code, message)])));

        var summary = await service.RunFilesAsync([DailyName], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal(("Failed", "D001", "HEMW", Today), (file.Status, file.ReportCode, file.StoreCode, file.PeriodEnd));
        Assert.Equal((code, FailureStage.Apply, message), (file.Failure!.Code, file.Failure.Stage, file.Failure.SafeMessage));
        Assert.Equal(message, file.Message);
        Assert.Contains(file.Diagnostics!, issue => issue.Severity == ImportIssueSeverity.Blocker && issue.Code == code);
        Assert.Same(file, Assert.Single(persistence.Attempts));
        Assert.Equal(new[] { DailyName }, service.FailedPaths);

        var failure = Assert.Single(failures);
        Assert.Equal((DailyName, FailureStage.Apply, "D001", "HEMW", (DateOnly?)Today), (failure.FileName, failure.Stage, failure.ReportCode, failure.StoreCode, failure.PeriodEnd));
        Assert.Equal(code, Assert.IsType<ImportSourceException>(failure.Exception).Code);
    }

    [Fact]
    public async Task A_sheet_the_parser_refuses_never_reaches_the_applier()
    {
        var persistence = new RecordingPersistence();
        var failures = new List<FolderImportFailure>();
        var applier = new FakeApplier(new DailyInputsApplyResult("Imported", 1, 0, null, "", []));
        // Every Value blank: the sheet was saved empty (DAILY_INPUTS_NO_VALUES).
        var service = new FolderImportService(persistence, new Reader(_ => Daily()), reportFailure: failures.Add, dailyInputs: applier);

        var file = Assert.Single((await service.RunFilesAsync([DailyName], new("tester"))).Files);

        Assert.Empty(applier.Calls);
        Assert.Equal(("Failed", "D001", FailureStage.Source), (file.Status, file.ReportCode, file.Failure!.Stage));
        Assert.Equal("DAILY_INPUTS_NO_VALUES", file.Failure.Code);
        Assert.Contains(file.Diagnostics!, issue => issue.Severity == ImportIssueSeverity.Blocker && issue.Code == "DAILY_INPUTS_NO_VALUES");
        Assert.Single(persistence.Attempts);
        Assert.Equal("DAILY_INPUTS_NO_VALUES", Assert.IsType<ImportSourceException>(Assert.Single(failures).Exception).Code);
    }

    [Fact]
    public async Task Without_an_applier_a_daily_inputs_file_fails_as_not_available()
    {
        // A persistence that is not the SQL Server one gives no default applier.
        var persistence = new RecordingPersistence();
        var failures = new List<FolderImportFailure>();
        var service = new FolderImportService(persistence, new Reader(_ => Daily(("WALK_INS", 12m))), reportFailure: failures.Add);

        var file = Assert.Single((await service.RunFilesAsync([DailyName], new("tester"))).Files);

        Assert.Equal("Failed", file.Status);
        Assert.Equal(FolderImportService.DailyInputsNotAvailableCode, file.Failure!.Code);
        Assert.Equal("DAILY_INPUTS_NOT_AVAILABLE", FolderImportService.DailyInputsNotAvailableCode);
        Assert.Single(failures);
        Assert.Single(persistence.Attempts);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task An_unexpected_applier_exception_is_classified_and_reported_with_the_real_exception()
    {
        var persistence = new RecordingPersistence();
        var failures = new List<FolderImportFailure>();
        var service = new FolderImportService(persistence, new Reader(_ => Daily(("WALK_INS", 12m))), reportFailure: failures.Add,
            dailyInputs: new FakeApplier(new InvalidOperationException("Synthetic applier failure.")));

        var file = Assert.Single((await service.RunFilesAsync([DailyName], new("tester"))).Files);

        Assert.Equal(("Failed", "D001"), (file.Status, file.ReportCode));
        Assert.Equal(FailureStage.Apply, file.Failure!.Stage);
        Assert.IsType<InvalidOperationException>(Assert.Single(failures).Exception);
        Assert.Single(persistence.Attempts);
    }

    [Fact]
    public async Task A_folder_with_a_daily_inputs_file_and_an_R025_export_imports_both()
    {
        var persistence = new RecordingPersistence();
        var applier = new FakeApplier(new DailyInputsApplyResult("Imported", 1, 0, null, "1 daily input(s) saved; 0 unchanged.", []));
        var service = new FolderImportService(persistence, new Reader(path =>
            path == DailyName ? Daily(("WALK_INS", 12m)) : Sales(path, "HEMW", 20260825)), dailyInputs: applier);

        var summary = await service.RunFilesAsync([DailyName, "HEMW_R025_20260825.xlsx"], new("tester"));

        Assert.Equal(2, summary.Imported);
        Assert.Equal(0, summary.Failed);
        Assert.Equal("D001", Assert.Single(summary.Files, file => file.FileName == DailyName).ReportCode);
        Assert.Equal("R025", Assert.Single(summary.Files, file => file.FileName != DailyName).ReportCode);
        Assert.Equal("R025", Assert.Single(persistence.Requests).AcceptedImport.ProfileIdentity.ReportCode);
        Assert.Single(applier.Calls);
        Assert.Equal(2, persistence.Attempts.Count);
    }

    [Fact]
    public void An_imported_daily_inputs_date_makes_its_report_pack_due()
    {
        var batch = new FolderImportSummary(
        [
            new(DailyName, "D001", "HEMW", Today, Today, "Imported", 1, 1),
            new("unchanged.xlsx", "D001", "HEMW", Today.AddDays(-1), Today.AddDays(-1), "Already present", 1, 0, 1)
        ]);
        Assert.Equal(new[] { Today }, AutomatedOperationsService.ImportedDates(batch));
        Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(batch));
        var unchanged = new FolderImportSummary([batch.Files[1]]);
        Assert.Empty(AutomatedOperationsService.ImportedDates(unchanged));
        Assert.Equal(AutomationSourceRoute.Duplicate, AutomatedOperationsService.RouteOf(unchanged));
    }

    // The RETAIL template sheet (section 1): header row 1, key rows 2-6, then one row per field in template order.
    private static WorkbookSnapshot Daily(params (string Code, object? Value)[] values)
    {
        var given = values.ToDictionary(value => value.Code, value => value.Value);
        var rows = new List<WorkbookRow>
        {
            Row(2, "TEMPLATE", "Template", "ETP-DAILY-INPUTS-1", "Do not change"),
            Row(3, "KIND", "Sheet type", "RETAIL", null),
            Row(4, "STORE", "Store", "HEMW", "Helios"),
            Row(5, "BUSINESS_DATE", "Business date", Today.ToDateTime(TimeOnly.MinValue), null),
            Row(6, "FILLED_BY", "Filled by", null, null)
        };
        string[] fields = ["WALK_INS", "OPENING_CASH", "EXPENSES", "CASH_DEPOSIT", "CASH_ADJUSTMENT", "CLOSING_CASH_COUNTED", "OPERATIONAL_REMARK"];
        rows.AddRange(fields.Select((field, index) => Row(7 + index, field, field, given.GetValueOrDefault(field), null)));
        return new(DailyName, 1, DailySha, [new("Daily Inputs", 1, ["Field code", "Input", "Value", "Note"], rows)]);
    }

    private static WorkbookRow Row(int number, string code, string label, object? value, string? note) =>
        new(number, [new(code), new(label), new(value), new(note)]);

    private static WorkbookSnapshot Sales(string path, string store, int date)
    {
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = store, ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = date, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
        };
        var row = new WorkbookRow(2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());
        return new(path, 1, new string('b', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, [row])]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class FakeApplier : IDailyInputsApplier
    {
        private readonly DailyInputsApplyResult? result;
        private readonly Exception? failure;
        public FakeApplier(DailyInputsApplyResult result) => this.result = result;
        public FakeApplier(Exception failure) => this.failure = failure;
        public List<(DailyInputsDocument Document, string User)> Calls { get; } = [];
        public Task<DailyInputsApplyResult> ApplyAsync(DailyInputsDocument document, string user, CancellationToken cancellationToken = default)
        {
            Calls.Add((document, user));
            return failure is null ? Task.FromResult(result!) : Task.FromException<DailyInputsApplyResult>(failure);
        }
    }

    // A new-file store that records attempts as History would.
    private sealed class RecordingPersistence : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportAttemptRecorder
    {
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public List<FolderImportFileResult> Attempts { get; } = [];
        public int ScopeLookups { get; private set; }
        public Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default)
        {
            Attempts.Add(result);
            return Task.CompletedTask;
        }
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        {
            ScopeLookups++;
            return Task.FromResult(false);
        }
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count)
                { Status = "Imported" });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(1, 1, 0, 0));
    }
}
