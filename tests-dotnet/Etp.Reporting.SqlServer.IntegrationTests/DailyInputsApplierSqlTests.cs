using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.10.1 (decision 30, 1101-DAILY-INPUTS-DESIGN.md sections 3 and 6): a shop PC's daily-inputs workbook is applied
/// through the Today screen's manual-input write, so the trigger keeps the history with the file name and a
/// finalised day stays protected. Only changed values are written, blanks never delete, an exact file already
/// imported is a Duplicate, and a file with any bad row writes nothing. Each test uses its own business date.
/// </summary>
public sealed class DailyInputsApplierSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string User = "TEST\\daily-inputs";
    private const string Retail = "ETP-Daily-Inputs-HEMW-RETAIL-{0:yyyy-MM-dd}.xlsx";

    [SqlFact]
    public async Task First_apply_writes_values_history_and_events_and_a_repeat_changes_nothing()
    {
        var day = new DateOnly(2026, 9, 1);
        var name = string.Format(Retail, day);
        var document = Document(name, Sha('1'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m), Value(9, "EXPENSES", DailyInputValueKind.Money, 250.50m));

        var first = await Applier().ApplyAsync(document, User);

        Assert.Equal(("Imported", 2, 0, (string?)null), (first.Outcome, first.Changed, first.Unchanged, first.FailureCode));
        Assert.Equal(12m, await Numeric("HEMW", day, "WALK_INS"));
        Assert.Equal(250.50m, await Numeric("HEMW", day, "EXPENSES"));
        Assert.Equal(User, await Scalar<string>($"SELECT modified_by FROM dbo.manual_operational_inputs WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND field_code='WALK_INS'"));
        Assert.Equal("OPEN", await Scalar<string>($"SELECT status FROM dbo.daily_reporting_days WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'"));
        // The trigger's history: the old value is empty and the reason names the file and its short hash.
        Assert.Equal(2, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND old_numeric_value IS NULL AND change_reason LIKE N'%{name}%' AND change_reason LIKE N'%({Sha('1')[..12]})%' AND changed_by=N'{User}'"));
        Assert.Equal(2, await Count($"dbo.daily_reporting_events WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND event_type='ManualInputChanged' AND reason LIKE N'%{name}%'"));

        // The same values again (e.g. the same sheet re-saved): Already present, no history, no event.
        var repeat = await Applier().ApplyAsync(document with { Sha256 = Sha('2') }, User);

        Assert.Equal(("Already present", 0, 2), (repeat.Outcome, repeat.Changed, repeat.Unchanged));
        Assert.Equal(2, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'"));
        Assert.Equal(2, await Count($"dbo.daily_reporting_events WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'"));
    }

    [SqlFact]
    public async Task A_corrected_file_changes_only_the_differing_value_and_a_blank_keeps_the_stored_one()
    {
        var day = new DateOnly(2026, 9, 2);
        var name = string.Format(Retail, day);
        await Applier().ApplyAsync(Document(name, Sha('3'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m), Value(9, "EXPENSES", DailyInputValueKind.Money, 250.50m),
            Value(8, "OPENING_CASH", DailyInputValueKind.Money, 5000m)), User);

        // The corrected sheet: EXPENSES differs, WALK_INS is the same, OPENING_CASH was left blank.
        var corrected = await Applier().ApplyAsync(Document(name, Sha('4'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m), Value(9, "EXPENSES", DailyInputValueKind.Money, 300m)), User);

        Assert.Equal(("Imported", 1, 1), (corrected.Outcome, corrected.Changed, corrected.Unchanged));
        Assert.Equal(300m, await Numeric("HEMW", day, "EXPENSES"));
        Assert.Equal(12m, await Numeric("HEMW", day, "WALK_INS"));
        Assert.Equal(5000m, await Numeric("HEMW", day, "OPENING_CASH"));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND field_code='EXPENSES' AND old_numeric_value=250.50 AND new_numeric_value=300 AND change_reason LIKE N'%({Sha('4')[..12]})%'"));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND field_code='WALK_INS'"));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND field_code='OPENING_CASH'"));
    }

    [SqlFact]
    public async Task An_exact_file_already_imported_is_a_duplicate_and_overwrites_no_later_screen_correction()
    {
        var day = new DateOnly(2026, 9, 3);
        var name = string.Format(Retail, day);
        var document = Document(name, Sha('5'), DailyInputsKind.Retail, "HEMW", day, Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m));
        Assert.Equal("Imported", (await Applier().ApplyAsync(document, User)).Outcome);
        await RecordAttempt(name, Sha('5'), "HEMW", day, "Imported");
        // The Owner corrects the value on the Today screen afterwards.
        await new DailyReportingWorkflowRepository(database.ConnectionString).SaveManualInputAsync("HEMW", day, "WALK_INS", 14m, null, "TEST\\owner", "Counted again");

        var again = await Applier().ApplyAsync(document, User);

        Assert.Equal(("Duplicate", 0, (string?)null), (again.Outcome, again.Changed, again.FailureCode));
        Assert.Equal(14m, await Numeric("HEMW", day, "WALK_INS"));
        Assert.Equal(2, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'"));

        // Only an Imported attempt of the same file, store and date counts: a failed one, another date or store does not.
        var otherDay = day.AddDays(20);
        await RecordAttempt(string.Format(Retail, otherDay), Sha('6'), "HEMW", otherDay, "Failed");
        var retried = await Applier().ApplyAsync(Document(string.Format(Retail, otherDay), Sha('6'), DailyInputsKind.Retail, "HEMW", otherDay,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 3m)), User);
        Assert.Equal("Imported", retried.Outcome);
    }

    [SqlFact]
    public async Task A_finalised_day_refuses_changes_with_nothing_written_but_accepts_an_unchanged_file()
    {
        var day = new DateOnly(2026, 9, 4);
        var name = string.Format(Retail, day);
        await Applier().ApplyAsync(Document(name, Sha('7'), DailyInputsKind.Retail, "HEMW", day, Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m)), User);
        await database.ExecuteAsync($"UPDATE dbo.daily_reporting_days SET status='LOCKED',finalised_by=SUSER_SNAME(),finalised_utc=SYSUTCDATETIME() WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'");

        var changed = await Applier().ApplyAsync(Document(name, Sha('8'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 13m), Value(9, "EXPENSES", DailyInputValueKind.Money, 10m)), User);

        Assert.Equal(("Failed", "DAILY_INPUTS_DAY_FINALISED", 0), (changed.Outcome, changed.FailureCode, changed.Changed));
        Assert.Equal(SqlServerDailyInputsApplier.DayFinalisedMessage, changed.Message);
        Assert.Contains("Reopen the day", changed.Message, StringComparison.Ordinal);
        Assert.Equal(12m, await Numeric("HEMW", day, "WALK_INS"));
        Assert.Null(await Numeric("HEMW", day, "EXPENSES"));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'"));

        var unchanged = await Applier().ApplyAsync(Document(name, Sha('9'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m)), User);
        Assert.Equal(("Already present", 1), (unchanged.Outcome, unchanged.Unchanged));
    }

    [SqlFact]
    public async Task A_retired_field_or_a_field_of_the_other_sheet_refuses_the_whole_file()
    {
        var day = new DateOnly(2026, 9, 5);
        var name = string.Format(Retail, day);

        var inactive = await Applier().ApplyAsync(Document(name, Sha('a'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m), Value(8, "SALES_TARGET", DailyInputValueKind.Money, 100000m)), User);
        var mismatch = await Applier().ApplyAsync(Document(name, Sha('b'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 12m), Value(8, "SERVICE_CASH", DailyInputValueKind.Money, 500m)), User);

        Assert.Equal(("Failed", "DAILY_INPUTS_FIELD_INACTIVE"), (inactive.Outcome, inactive.FailureCode));
        Assert.Equal((8, "SALES_TARGET"), (Assert.Single(inactive.Errors).RowNumber, inactive.Errors[0].FieldCode));
        Assert.Equal(("Failed", "DAILY_INPUTS_FIELD_NOT_FOR_SHEET"), (mismatch.Outcome, mismatch.FailureCode));
        Assert.Equal(0, await Count($"dbo.manual_operational_inputs WHERE business_date='{day:yyyyMMdd}'"));
        Assert.Equal(0, await Count($"dbo.daily_reporting_days WHERE business_date='{day:yyyyMMdd}'"));
    }

    [SqlFact]
    public async Task An_unknown_or_inactive_store_and_an_account_without_import_permission_are_refused()
    {
        var day = new DateOnly(2026, 9, 6);
        await database.ExecuteAsync("IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code='DIOLD') INSERT dbo.stores(store_code,store_name,is_active) VALUES('DIOLD',N'Synthetic closed store',0)");
        foreach (var store in new[] { "DINONE", "DIOLD" })
        {
            var refused = await Applier().ApplyAsync(Document($"ETP-Daily-Inputs-{store}-RETAIL-{day:yyyy-MM-dd}.xlsx", Sha('c'), DailyInputsKind.Retail,
                store, day, Value(7, "WALK_INS", DailyInputValueKind.Quantity, 1m)), User);
            Assert.Equal(("Failed", "DAILY_INPUTS_UNKNOWN_STORE"), (refused.Outcome, refused.FailureCode));
        }

        var viewer = new SqlServerDailyInputsApplier(database.ConnectionString,
            _ => Task.FromResult(new ApplicationAccess("TEST\\viewer", "Viewer", ApplicationRole.Viewer, true)));
        var denied = await viewer.ApplyAsync(Document(string.Format(Retail, day), Sha('d'), DailyInputsKind.Retail, "HEMW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 1m)), "TEST\\viewer");
        Assert.Equal(("Failed", "DAILY_INPUTS_NO_PERMISSION"), (denied.Outcome, denied.FailureCode));
        Assert.Equal(0, await Count($"dbo.manual_operational_inputs WHERE business_date='{day:yyyyMMdd}'"));
    }

    [SqlFact]
    public async Task The_Titan_retail_sheet_and_the_Service_sheet_for_the_same_day_do_not_touch_each_other()
    {
        var day = new DateOnly(2026, 9, 7);
        var retail = await Applier().ApplyAsync(Document($"ETP-Daily-Inputs-WLMHW-RETAIL-{day:yyyy-MM-dd}.xlsx", Sha('e'), DailyInputsKind.Retail, "WLMHW", day,
            Value(7, "WALK_INS", DailyInputValueKind.Quantity, 40m), Value(10, "CASH_DEPOSIT", DailyInputValueKind.Money, 20000m)), User);
        var service = await Applier().ApplyAsync(Document($"ETP-Daily-Inputs-WLMHW-SERVICE-{day:yyyy-MM-dd}.xlsx", Sha('f'), DailyInputsKind.Service, "WLMHW", day,
            Value(7, "SERVICE_CASH", DailyInputValueKind.Money, 1500m), Value(12, "WDC_BILLS", DailyInputValueKind.Quantity, 3m)), User);

        Assert.Equal(("Imported", 2), (retail.Outcome, retail.Changed));
        Assert.Equal(("Imported", 2), (service.Outcome, service.Changed));
        Assert.Equal(4, await Count($"dbo.manual_operational_inputs WHERE store_code='WLMHW' AND business_date='{day:yyyyMMdd}'"));
        Assert.Equal(40m, await Numeric("WLMHW", day, "WALK_INS"));
        Assert.Equal(1500m, await Numeric("WLMHW", day, "SERVICE_CASH"));

        // The Service sheet again with a correction leaves the retail values alone.
        var correction = await Applier().ApplyAsync(Document($"ETP-Daily-Inputs-WLMHW-SERVICE-{day:yyyy-MM-dd}.xlsx", Sha('0'), DailyInputsKind.Service, "WLMHW", day,
            Value(7, "SERVICE_CASH", DailyInputValueKind.Money, 1600m)), User);
        Assert.Equal(("Imported", 1), (correction.Outcome, correction.Changed));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='WLMHW' AND business_date='{day:yyyyMMdd}' AND field_code='WALK_INS'"));
        Assert.Equal(20000m, await Numeric("WLMHW", day, "CASH_DEPOSIT"));
    }

    [SqlFact]
    public async Task The_screen_write_still_validates_writes_history_and_refuses_a_finalised_day()
    {
        var day = new DateOnly(2026, 9, 8);
        var screen = new DailyReportingWorkflowRepository(database.ConnectionString);
        await screen.SaveManualInputAsync("HEMW", day, "walk_ins", 9m, null, "TEST\\screen", "Counted at close");
        Assert.Equal(9m, await Numeric("HEMW", day, "WALK_INS"));
        Assert.Equal(1, await Count($"dbo.manual_operational_input_history WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND change_reason=N'Counted at close' AND changed_by=N'TEST\\screen'"));
        Assert.Equal(1, await Count($"dbo.daily_reporting_events WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}' AND event_type='ManualInputChanged'"));

        var retired = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            screen.SaveManualInputAsync("HEMW", day, "SALES_TARGET", 1m, null, "TEST\\screen", "Target"));
        Assert.Equal(51022, retired.Number);

        await database.ExecuteAsync($"UPDATE dbo.daily_reporting_days SET status='LOCKED',finalised_by=SUSER_SNAME(),finalised_utc=SYSUTCDATETIME() WHERE store_code='HEMW' AND business_date='{day:yyyyMMdd}'");
        var locked = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            screen.SaveManualInputAsync("HEMW", day, "WALK_INS", 10m, null, "TEST\\screen", "Recount"));
        Assert.Equal(51020, locked.Number);
        Assert.Equal(9m, await Numeric("HEMW", day, "WALK_INS"));
    }

    [SqlFact]
    public async Task The_folder_import_applies_a_daily_inputs_workbook_records_it_as_D001_and_skips_the_same_file_next_time()
    {
        // The parser dates the sheet against today, so this test alone uses today's date (no other test does).
        var today = DateOnly.FromDateTime(DateTime.Today);
        var name = $"ETP-Daily-Inputs-HEMW-RETAIL-{today:yyyy-MM-dd}.xlsx";
        var sha = new string('c', 64);
        var service = () => new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString), new Reader(_ => Workbook(name, sha, today)));

        var first = Assert.Single((await service().RunFilesAsync([name], new("TEST\\automation"))).Files);
        var second = Assert.Single((await service().RunFilesAsync([name], new("TEST\\automation"))).Files);

        Assert.Equal(("Imported", "D001", "HEMW", (DateOnly?)today, 1), (first.Status, first.ReportCode, first.StoreCode, first.PeriodEnd, first.NewRows));
        Assert.Equal("Duplicate", second.Status);
        Assert.Equal(7m, await Numeric("HEMW", today, "WALK_INS"));
        Assert.Equal(1, await Count($"dbo.import_attempts WHERE report_code='D001' AND store_code='HEMW' AND period_end='{today:yyyyMMdd}' AND outcome='Imported' AND source_sha256='{sha}' AND file_name=N'{name}'"));
        Assert.Equal(1, await Count($"dbo.import_attempts WHERE report_code='D001' AND outcome='Duplicate' AND source_sha256='{sha}'"));
        Assert.Equal(0, await Count($"dbo.import_files WHERE report_code='D001'"));
    }

    [SqlFact]
    public async Task A_store_manager_holds_every_right_the_applier_uses_without_a_new_migration()
    {
        // The automation account is an active Store Manager (IE-RT-08); 0022 and 0028 grant what the applier needs.
        await database.ExecuteAsync("IF DATABASE_PRINCIPAL_ID('dailyinputs_manager') IS NULL BEGIN CREATE USER dailyinputs_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER dailyinputs_manager; END");
        var rights = await Scalar<string>("""
            EXECUTE AS USER='dailyinputs_manager';
            DECLARE @r nvarchar(400)=CONCAT(
              HAS_PERMS_BY_NAME('dbo.manual_operational_inputs','OBJECT','SELECT'),HAS_PERMS_BY_NAME('dbo.manual_operational_inputs','OBJECT','INSERT'),
              HAS_PERMS_BY_NAME('dbo.manual_operational_inputs','OBJECT','UPDATE'),HAS_PERMS_BY_NAME('dbo.manual_input_definitions','OBJECT','SELECT'),
              HAS_PERMS_BY_NAME('dbo.daily_reporting_days','OBJECT','SELECT'),HAS_PERMS_BY_NAME('dbo.daily_reporting_days','OBJECT','INSERT'),
              HAS_PERMS_BY_NAME('dbo.daily_reporting_events','OBJECT','INSERT'),HAS_PERMS_BY_NAME('dbo.stores','OBJECT','SELECT'),
              HAS_PERMS_BY_NAME('dbo.import_attempts','OBJECT','SELECT'),HAS_PERMS_BY_NAME('dbo.record_import_attempt','OBJECT','EXECUTE'));
            REVERT;
            SELECT @r;
            """);
        Assert.Equal("1111111111", rights);
    }

    private SqlServerDailyInputsApplier Applier() => new(database.ConnectionString);

    private static DailyInputsDocument Document(string fileName, string sha, DailyInputsKind kind, string store, DateOnly day,
        params DailyInputValue[] values) => new(fileName, sha, "ETP-DAILY-INPUTS-1", kind, store, day, "Staff A", values);

    private static DailyInputValue Value(int row, string code, DailyInputValueKind kind, decimal value) => new(row, code, kind, value, null);

    private static string Sha(char digit) => new(digit, 64);

    private async Task RecordAttempt(string name, string sha, string store, DateOnly day, string outcome) =>
        await new SqlServerImportHistoryQuery(database.ConnectionString).RecordAttemptAsync(
            new FolderImportFileResult(name, "D001", store, day, day, outcome, 1, outcome == "Imported" ? 1 : 0)
            {
                SourceSha256 = sha, Evidence = EvidenceState.NotAttempted,
                Failure = outcome == "Failed" ? new ImportFailure("DAILY_INPUTS_DAY_FINALISED", FailureStage.Apply, SqlServerDailyInputsApplier.DayFinalisedMessage) : null
            });

    private async Task<decimal?> Numeric(string store, DateOnly day, string field) =>
        await database.ExecuteAsync($"SELECT numeric_value FROM dbo.manual_operational_inputs WHERE store_code='{store}' AND business_date='{day:yyyyMMdd}' AND field_code='{field}'")
            is decimal value ? value : null;

    private async Task<int> Count(string from) => Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM " + from));

    private async Task<T> Scalar<T>(string sql) => (T)(await database.ExecuteAsync(sql))!;

    // The RETAIL template sheet with WALK_INS = 7 and every other Value blank.
    private static WorkbookSnapshot Workbook(string name, string sha, DateOnly day)
    {
        static WorkbookRow Row(int number, string code, object? value) => new(number, [new(code), new(code), new(value), new(null)]);
        string[] fields = ["WALK_INS", "OPENING_CASH", "EXPENSES", "CASH_DEPOSIT", "CASH_ADJUSTMENT", "CLOSING_CASH_COUNTED", "OPERATIONAL_REMARK"];
        WorkbookRow[] rows =
        [
            Row(2, "TEMPLATE", "ETP-DAILY-INPUTS-1"), Row(3, "KIND", "RETAIL"), Row(4, "STORE", "HEMW"),
            Row(5, "BUSINESS_DATE", day.ToDateTime(TimeOnly.MinValue)), Row(6, "FILLED_BY", "Synthetic staff"),
            .. fields.Select((field, index) => Row(7 + index, field, field == "WALK_INS" ? 7m : null))
        ];
        return new(name, 1, sha, [new("Daily Inputs", 1, ["Field code", "Input", "Value", "Note"], rows)]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }
}
