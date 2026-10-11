using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Import.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// Health checks DATA-01..05 (design 5.2) on fake readers: outcomes, finding codes, the "reported once" rule between
/// DATA-01 and DATA-02, DATA-02 computing its own tender result, and the guard on every inline SQL text. Synthetic store codes only.
/// </summary>
public sealed class DataProbeTests
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private static readonly string[] Stores = ["ZZ001", "ZZ002"];

    private static HealthCheckContext Context(params string[] stores) =>
        new(Day, HealthCheckTrigger.Nightly, DateTimeOffset.UtcNow, stores.Length == 0 ? Stores : stores, null,
            HealthCheckBudget.Default, new Dictionary<string, object>());

    private static DailyReadinessItem Missing(DailyReadinessItemKind kind, string store, string code, DailyReadinessDestination destination) =>
        new(kind, store, code, "message", destination);

    private static DailyReadinessResult Complete() => new(Day, Stores, []);

    private static TenderReconciliationReport Tender(ReportStatus status, params TenderDocumentRecord[] documents) =>
        new(status, documents, 0m, 0m, 0m, "rule", "message");

    private static DataProbes Probes(
        DailyReadinessResult? readiness = null,
        Func<ReportScope, TenderReconciliationReport>? tender = null,
        IReadOnlyList<StockSnapshotFact>? snapshots = null,
        ServiceFreshnessFacts? service = null,
        IReadOnlyList<DataQualityFact>? quality = null) =>
        new(
            (_, _) => Task.FromResult(readiness ?? Complete()),
            (scope, _) => Task.FromResult(tender is null ? Tender(ReportStatus.Passed) : tender(scope)),
            _ => Task.FromResult(snapshots ?? [new(Stores[0], Day), new(Stores[1], Day)]),
            _ => Task.FromResult(service ?? new ServiceFreshnessFacts(true, false, [], new Dictionary<string, string>(), null)),
            _ => Task.FromResult(quality ?? []));

    private static HealthCheckResult Line(IReadOnlyList<HealthCheckResult> results, string id) => results.Single(r => r.CheckId == id);

    // ---- all checks ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_clean_day_gives_five_Pass_lines_in_catalogue_order_with_area_Data()
    {
        var results = await Probes().RunAsync(Context(), CancellationToken.None);

        Assert.Equal(["DATA-01", "DATA-02", "DATA-03", "DATA-04", "DATA-05"], results.Select(r => r.CheckId));
        Assert.Equal(results.Select(r => r.CheckId), Probes().CheckIds);
        Assert.All(results, r => { Assert.Equal(HealthCheckArea.Data, r.Area); Assert.Equal(HealthOutcome.Pass, r.Outcome); });
    }

    [Fact]
    public void Every_inline_SQL_text_of_the_probes_is_read_only_for_the_guard()
    {
        foreach (var sql in new[]
        {
            DataProbes.SnapshotsSql, DataProbes.ServiceFactsSql, DataProbes.ServiceReadingsSql, DataProbes.ServiceFilesSql,
            DataProbes.DataQualitySql, ImportProbes.FailuresSql
        })
            Assert.Empty(ReadOnlySqlGuard.Violations(sql));
    }

    [Fact]
    public async Task A_reader_that_throws_gives_a_Fail_line_with_the_exception_type_and_never_its_message()
    {
        var probes = new DataProbes(
            (_, _) => throw new InvalidOperationException("secret customer detail"),
            (_, _) => Task.FromResult(Tender(ReportStatus.Passed)),
            _ => Task.FromResult<IReadOnlyList<StockSnapshotFact>>([]),
            _ => Task.FromResult(new ServiceFreshnessFacts(true, false, [], new Dictionary<string, string>(), null)),
            _ => Task.FromResult<IReadOnlyList<DataQualityFact>>([]));

        var line = Line(await probes.RunAsync(Context(), CancellationToken.None), "DATA-01");

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("PROBE_EXCEPTION", line.FindingCode);
        Assert.Contains("InvalidOperationException", line.Explanation);
        Assert.DoesNotContain("secret", line.Explanation);
    }

    [Fact]
    public async Task An_unreadable_database_gives_Skipped_not_Fail()
    {
        var probes = new DataProbes(
            (_, _) => throw SqlExceptionFactory.Create(new SqlExceptionFactory.Error(4060, "cannot open database")),
            (_, _) => Task.FromResult(Tender(ReportStatus.Passed)),
            _ => Task.FromResult<IReadOnlyList<StockSnapshotFact>>([]),
            _ => Task.FromResult(new ServiceFreshnessFacts(true, false, [], new Dictionary<string, string>(), null)),
            _ => Task.FromResult<IReadOnlyList<DataQualityFact>>([]));

        var line = Line(await probes.RunAsync(Context(), CancellationToken.None), "DATA-01");

        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
        Assert.Equal("DATABASE_NOT_READABLE", line.FindingCode);
        Assert.Contains("4060", line.Explanation);
        Assert.DoesNotContain("cannot open", line.Explanation);
    }

    // ---- DATA-01 ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Data01_groups_by_kind_with_store_codes_and_a_sub_finding_per_kind()
    {
        var readiness = new DailyReadinessResult(Day, Stores,
        [
            Missing(DailyReadinessItemKind.MissingExport, "ZZ001", "R025", DailyReadinessDestination.ImportIntake),
            Missing(DailyReadinessItemKind.MissingExport, "ZZ002", "R022", DailyReadinessDestination.ImportIntake),
            Missing(DailyReadinessItemKind.MissingExport, "ZZ001", "R011", DailyReadinessDestination.ImportIntake),
            Missing(DailyReadinessItemKind.MissingManualInput, "ZZ001", "WALK_INS", DailyReadinessDestination.TodayWalkIns),
            Missing(DailyReadinessItemKind.MissingMonthlyTarget, "ZZ001", "MONTHLY_TARGET", DailyReadinessDestination.SettingsMonthlyTargets),
            Missing(DailyReadinessItemKind.MissingStaffTargets, "ZZ002", "STAFF_TARGETS", DailyReadinessDestination.SettingsStaffTargets)
        ]);

        var line = DataProbes.EvaluateCompleteness(Day, readiness, TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("DAY_INCOMPLETE", line.FindingCode);
        Assert.Equal("import-files", line.FixTaskId);
        Assert.Equal(6m, line.MetricValue);
        Assert.Equal(["EXPORTS_MISSING", "CLOSING_STOCK_MISSING", "INPUTS_MISSING", "MONTHLY_TARGET_MISSING", "STAFF_TARGETS_MISSING"],
            line.SubResults!.Select(s => s.Key));
        var exports = line.SubResults!.Single(s => s.Key == "EXPORTS_MISSING");
        Assert.Equal(2m, exports.MetricValue);
        Assert.Equal("R022,R025", exports.Fields!["codes"]);
        Assert.Equal("ZZ001,ZZ002", exports.Fields["stores"]);
        Assert.Equal("R011", line.SubResults!.Single(s => s.Key == "CLOSING_STOCK_MISSING").Fields!["codes"]);
        Assert.Contains("09 Oct 2026", line.Explanation);
        Assert.DoesNotContain("message", line.Explanation);
    }

    [Theory]
    [InlineData(DailyReadinessItemKind.MissingManualInput, "WALK_INS", DailyReadinessDestination.TodayWalkIns, "walk-ins")]
    [InlineData(DailyReadinessItemKind.MissingManualInput, "EXPENSES", DailyReadinessDestination.TodayCashEntries, "cash-input")]
    [InlineData(DailyReadinessItemKind.MissingMonthlyTarget, "MONTHLY_TARGET", DailyReadinessDestination.SettingsMonthlyTargets, "masters")]
    [InlineData(DailyReadinessItemKind.MissingStaffTargets, "STAFF_TARGETS", DailyReadinessDestination.SettingsStaffTargets, "staff-target")]
    public void Data01_where_to_fix_follows_the_readiness_destination(DailyReadinessItemKind kind, string code, DailyReadinessDestination destination, string task)
    {
        var line = DataProbes.EvaluateCompleteness(Day, new DailyReadinessResult(Day, Stores, [Missing(kind, "ZZ001", code, destination)]), TimeSpan.Zero);

        Assert.Equal(task, line.FixTaskId);
    }

    [Fact]
    public async Task Data01_publishes_its_missing_exports_for_the_other_data_checks()
    {
        var context = Context();
        var readiness = new DailyReadinessResult(Day, Stores,
            [Missing(DailyReadinessItemKind.MissingExport, "ZZ001", "R022", DailyReadinessDestination.ImportIntake),
             Missing(DailyReadinessItemKind.MissingManualInput, "ZZ001", "WALK_INS", DailyReadinessDestination.TodayWalkIns)]);

        await Probes(readiness).RunAsync(context, CancellationToken.None);

        var published = Assert.IsType<HashSet<string>>(context.Items[DataProbes.MissingItemsKey]);
        Assert.Single(published);
        Assert.Contains("R022|ZZ001", published);
    }

    // ---- DATA-02 and the de-duplication with DATA-01 ---------------------------------------------------------------

    [Fact]
    public async Task Data02_says_Skipped_see_DATA_01_for_a_store_whose_R022_is_missing_and_does_not_run_its_report()
    {
        var queried = new List<string>();
        var readiness = new DailyReadinessResult(Day, Stores,
            [Missing(DailyReadinessItemKind.MissingExport, "ZZ001", "R022", DailyReadinessDestination.ImportIntake)]);
        var probes = Probes(readiness, scope => { queried.AddRange(scope.StoreCodes!); return Tender(ReportStatus.Passed); });

        var results = await probes.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(["ZZ002"], queried);
        var line = Line(results, "DATA-02");
        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Contains("ZZ001", line.Explanation);
        Assert.Contains("DATA-01", line.Explanation);
        Assert.Equal(HealthOutcome.Skipped, line.SubResults!.Single(s => s.Key == "ZZ001").Outcome);
        Assert.Equal(HealthOutcome.Warning, Line(results, "DATA-01").Outcome);
    }

    [Fact]
    public async Task Data02_is_Skipped_when_every_store_is_missing_R022_so_the_gap_is_reported_once()
    {
        var readiness = new DailyReadinessResult(Day, Stores, Stores.Select(store =>
            Missing(DailyReadinessItemKind.MissingExport, store, "R022", DailyReadinessDestination.ImportIntake)).ToArray());
        var probes = Probes(readiness, _ => throw new InvalidOperationException("must not run"));

        var line = Line(await probes.RunAsync(Context(), CancellationToken.None), "DATA-02");

        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
        Assert.Equal("SEE_DATA_01", line.FindingCode);
        Assert.Contains("see DATA-01", line.Explanation);
    }

    [Fact]
    public async Task Data02_computes_its_own_result_when_DATA_01_gave_nothing_and_still_reports_once()
    {
        // No DATA-01 information in Items (the readiness reader is not run first): a Blocked report with tender missing is R022 missing.
        var blocked = Tender(ReportStatus.Blocked, new TenderDocumentRecord("ZZ001", "DOC-1", 10m, null, null, ReportStatus.Blocked));
        var probes = Probes(tender: _ => blocked);

        var line = await RunDataOnly(probes, "DATA-02");

        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
        Assert.Equal("SEE_DATA_01", line.FindingCode);
    }

    [Fact]
    public async Task Data02_blocked_for_another_reason_is_a_Warning()
    {
        var probes = Probes(tender: _ => Tender(ReportStatus.Blocked));

        var line = await RunDataOnly(probes, "DATA-02");

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("TENDER_BLOCKED", line.FindingCode);
    }

    [Fact]
    public async Task Data02_variance_beyond_tolerance_is_a_Fail_with_document_count_and_total_per_store()
    {
        var failed = Tender(ReportStatus.Failed,
            new TenderDocumentRecord("ZZ001", "DOC-1", 100m, 90m, -10m, ReportStatus.Failed),
            new TenderDocumentRecord("ZZ001", "DOC-2", 50m, 55.5m, 5.5m, ReportStatus.Failed),
            new TenderDocumentRecord("ZZ001", "DOC-3", 20m, 20m, 0m, ReportStatus.Passed));
        var probes = Probes(tender: scope => scope.StoreCodes!.Single() == "ZZ001" ? failed : Tender(ReportStatus.Passed));

        var line = await RunDataOnly(probes, "DATA-02");

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("TENDER_VARIANCE", line.FindingCode);
        Assert.Equal(2m, line.MetricValue);
        var store = line.SubResults!.Single(s => s.Key == "ZZ001");
        Assert.Equal("2", store.Fields!["documents"]);
        Assert.Equal("15.50", store.Fields["variance"]);
        Assert.Contains("15.50", line.Explanation);
        Assert.DoesNotContain("DOC-1", line.Explanation);
        Assert.Equal(HealthOutcome.Pass, line.SubResults!.Single(s => s.Key == "ZZ002").Outcome);
    }

    [Fact]
    public async Task Data02_scopes_each_store_to_the_business_date_only()
    {
        var scopes = new List<ReportScope>();
        await RunDataOnly(Probes(tender: scope => { scopes.Add(scope); return Tender(ReportStatus.Passed); }), "DATA-02");

        Assert.Equal(2, scopes.Count);
        Assert.All(scopes, scope => { Assert.Equal(Day, scope.DateFrom); Assert.Equal(Day, scope.DateTo); Assert.Single(scope.StoreCodes!); });
    }

    private static async Task<HealthCheckResult> RunDataOnly(DataProbes probes, string id) =>
        Line(await probes.RunAsync(Context(), CancellationToken.None), id);

    // ---- DATA-03 ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Data03_Pass_when_every_store_has_the_business_date_or_later()
    {
        var line = DataProbes.EvaluateSnapshots(Day, Stores, [new("ZZ001", Day), new("ZZ002", Day.AddDays(1))], TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
    }

    [Fact]
    public void Data03_one_to_thirty_days_behind_is_a_Warning_naming_the_latest_dates()
    {
        var line = DataProbes.EvaluateSnapshots(Day, Stores, [new("ZZ001", Day.AddDays(-6)), new("ZZ002", Day)], TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("SNAPSHOT_STALE", line.FindingCode);
        Assert.Equal(6m, line.MetricValue);
        Assert.Contains("ZZ001 03 Oct 2026", line.Explanation);
        Assert.Equal("import-files", line.FixTaskId);
    }

    [Fact]
    public void Data03_thirty_days_is_still_a_Warning_and_thirty_one_is_a_Fail()
    {
        Assert.Equal(HealthOutcome.Warning, DataProbes.EvaluateSnapshots(Day, ["ZZ001"], [new("ZZ001", Day.AddDays(-30))], TimeSpan.Zero).Outcome);
        var old = DataProbes.EvaluateSnapshots(Day, ["ZZ001"], [new("ZZ001", Day.AddDays(-31))], TimeSpan.Zero);
        Assert.Equal(HealthOutcome.Fail, old.Outcome);
        Assert.Equal("SNAPSHOT_VERY_OLD", old.FindingCode);
    }

    [Fact]
    public void Data03_a_store_with_no_snapshot_is_a_Fail()
    {
        var line = DataProbes.EvaluateSnapshots(Day, Stores, [new("ZZ001", Day)], TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("SNAPSHOT_MISSING", line.FindingCode);
        Assert.Contains("ZZ002", line.Explanation);
    }

    [Fact]
    public void Data03_ignores_stores_that_are_not_active_Retail_and_skips_with_none()
    {
        Assert.Equal(HealthOutcome.Pass, DataProbes.EvaluateSnapshots(Day, ["ZZ001"], [new("ZZ001", Day), new("OTHER", Day.AddDays(-90))], TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Skipped, DataProbes.EvaluateSnapshots(Day, [], [], TimeSpan.Zero).Outcome);
    }

    // ---- DATA-04 ---------------------------------------------------------------------------------------------------

    private static ServiceRefresh Refresh(string code, DateOnly date) => new(code, date, 10, 1, DateTime.UtcNow);

    private static ServiceFreshnessFacts ServiceFacts(bool active, params ServiceRefresh[] refreshes) =>
        new(true, active, refreshes, new Dictionary<string, string>(), null);

    [Fact]
    public void Data04_no_Service_data_at_all_is_a_Pass_because_there_is_nothing_to_age()
    {
        var line = DataProbes.EvaluateService(Day.AddDays(1), ServiceFacts(false), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Equal("NO_SERVICE_DATA", line.FindingCode);
    }

    [Fact]
    public void Data04_fresh_groups_pass()
    {
        var today = Day.AddDays(1);
        var line = DataProbes.EvaluateService(today, ServiceFacts(true, Refresh("S002", today.AddDays(-1)), Refresh("S036", today.AddDays(-7))), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
    }

    [Fact]
    public void Data04_eight_to_fourteen_days_is_a_Warning_naming_the_group()
    {
        var today = Day.AddDays(1);
        var line = DataProbes.EvaluateService(today, ServiceFacts(true, Refresh("S002", today.AddDays(-8)), Refresh("S036", today.AddDays(-8)), Refresh("S037", today.AddDays(-8))), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("SERVICE_AGEING", line.FindingCode);
        Assert.Contains("Jobs", line.Explanation);
    }

    [Fact]
    public void Data04_over_fourteen_days_is_a_Fail_while_the_Service_store_is_active_and_a_Warning_when_it_is_not()
    {
        var today = Day.AddDays(1);
        var stale = new[] { Refresh("S002", today.AddDays(-15)), Refresh("S036", today.AddDays(-15)), Refresh("S037", today.AddDays(-15)) };

        var active = DataProbes.EvaluateService(today, ServiceFacts(true, stale), TimeSpan.Zero);
        var inactive = DataProbes.EvaluateService(today, ServiceFacts(false, stale), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Fail, active.Outcome);
        Assert.Equal(HealthOutcome.Warning, inactive.Outcome);
        Assert.Equal("SERVICE_STALE", active.FindingCode);
        Assert.Equal("SERVICE_STALE", inactive.FindingCode);
        Assert.All(inactive.SubResults!, sub => Assert.Equal(HealthOutcome.Warning, sub.Outcome));
    }

    [Fact]
    public void Data04_before_the_readings_view_exists_the_newest_Service_file_date_stands_for_all_families()
    {
        var today = Day.AddDays(1);
        ServiceFreshnessFacts Facts(DateOnly? newest) => new(false, true, [], new Dictionary<string, string>(), newest);

        Assert.Equal(HealthOutcome.Pass, DataProbes.EvaluateService(today, Facts(null), TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Pass, DataProbes.EvaluateService(today, Facts(today.AddDays(-3)), TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Warning, DataProbes.EvaluateService(today, Facts(today.AddDays(-9)), TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Fail, DataProbes.EvaluateService(today, Facts(today.AddDays(-20)), TimeSpan.Zero).Outcome);
    }

    // ---- DATA-05 ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Data05_open_critical_issues_are_a_Warning_with_a_count_per_category_and_other_severities_are_ignored()
    {
        var line = DataProbes.EvaluateQuality(
            [new("UNMATCHED_ENRICHMENT", "CRITICAL", 1), new("UNMATCHED_ENRICHMENT", "WARNING", 40), new("TENDER_GAP", "CRITICAL", 2), new("OTHER", "INFO", 9)],
            TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("DQ_CRITICAL_OPEN", line.FindingCode);
        Assert.Equal(3m, line.MetricValue);
        Assert.Equal("data-quality", line.FixTaskId);
        Assert.Equal(["TENDER_GAP", "UNMATCHED_ENRICHMENT"], line.SubResults!.Select(s => s.Key));
        Assert.DoesNotContain("OTHER", line.Explanation);
    }

    [Fact]
    public void Data05_no_critical_issue_passes()
    {
        Assert.Equal(HealthOutcome.Pass, DataProbes.EvaluateQuality([new("X", "WARNING", 5)], TimeSpan.Zero).Outcome);
        Assert.Equal(HealthOutcome.Pass, DataProbes.EvaluateQuality([], TimeSpan.Zero).Outcome);
    }
}

/// <summary>Health checks IMP-01..03 (design 5.2) on fake readers and folders.</summary>
public sealed class ImportProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 2, 30, 0, TimeSpan.Zero);

    private sealed class FakeFolders(Dictionary<string, List<DateTime>?> folders) : IAutomaticImportFolders
    {
        public bool Exists(string path) => folders.ContainsKey(path) && folders[path] is not null;
        public IReadOnlyList<DateTime> FileTimesUtc(string path) => folders[path] ?? [];
    }

    private const string Inbound = @"C:\EtpHealthTest\Inbound", Processed = @"C:\EtpHealthTest\Processed", Failed = @"C:\EtpHealthTest\Failed", Output = @"C:\EtpHealthTest\Output";

    private static WatchFolderSettings Settings(bool enabled = true) => new(Inbound, Processed, Failed, Output, enabled, DateTime.UtcNow, "test");

    private static FakeFolders Folders(List<DateTime>? inbound = null, List<DateTime>? failed = null) => new(new()
    {
        [Inbound] = inbound ?? [], [Processed] = [], [Failed] = failed ?? [], [Output] = []
    });

    // ---- IMP-01 ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Imp01_passes_when_every_family_and_the_daily_inputs_sheet_are_recognised()
    {
        var line = ImportProbes.EvaluateFamilies(SyntheticFamilySamples.CheckAll(), true, TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Equal(HealthCheckArea.Imports, line.Area);
        Assert.Equal(73m, line.MetricValue);
    }

    [Fact]
    public void Imp01_lists_the_family_codes_that_are_not_recognised()
    {
        var checks = new[]
        {
            new SyntheticFamilyCheck("R001", "R001", "R001", []),
            new SyntheticFamilyCheck("R025", "R025", null, ["LAYOUT_UNKNOWN"]),
            new SyntheticFamilyCheck("S014", "S014", "S018", [])
        };

        var line = ImportProbes.EvaluateFamilies(checks, false, TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("FAMILY_NOT_RECOGNISED", line.FindingCode);
        Assert.Equal(["R025", "S014", "D001"], line.SubResults!.Select(s => s.Key));
        Assert.Equal("FAMILY_BLOCKED", line.SubResults![0].FindingCode);
        Assert.Equal("FAMILY_MISMATCH", line.SubResults![1].FindingCode);
        Assert.Equal("S018", line.SubResults![1].Fields!["matched"]);
        Assert.Contains("R025, S014, D001", line.Explanation);
    }

    [Fact]
    public async Task Imp01_runs_in_memory_over_the_real_registry_and_passes_today()
    {
        var probes = new ImportProbes(
            () => SyntheticFamilySamples.CheckAll(), SyntheticFamilySamples.CheckDailyInputs,
            _ => Task.FromResult<IReadOnlyList<ImportFailureRow>>([]),
            _ => Task.FromResult(Settings(false)), Folders(), () => Now);

        var results = await probes.RunAsync(
            new HealthCheckContext(new DateOnly(2026, 10, 9), HealthCheckTrigger.Nightly, Now, [], null, HealthCheckBudget.Default, new Dictionary<string, object>()),
            CancellationToken.None);

        Assert.Equal(["IMP-01", "IMP-02", "IMP-03"], results.Select(r => r.CheckId));
        Assert.All(results, r => Assert.Equal(HealthOutcome.Pass, r.Outcome));
    }

    // ---- IMP-02 ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Imp02_no_unresolved_failure_passes()
    {
        Assert.Equal(HealthOutcome.Pass, ImportProbes.EvaluateFailures([], TimeSpan.Zero).Outcome);
    }

    [Fact]
    public void Imp02_unresolved_failures_are_a_Warning_with_counts_by_report_code_pointing_to_Problems()
    {
        var line = ImportProbes.EvaluateFailures(
            [new("R025", "Failed", 2), new("R025", "Unknown layout", 1), new("UNKNOWN", "Unknown layout", 1)], TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("IMPORT_FAILURES_OPEN", line.FindingCode);
        Assert.Equal(4m, line.MetricValue);
        Assert.Equal("conflicts", line.FixTaskId);
        Assert.Equal(["R025", "UNKNOWN"], line.SubResults!.Select(s => s.Key));
        Assert.Equal(3m, line.SubResults![0].MetricValue);
        Assert.Contains("R025 3", line.Explanation);
    }

    // ---- IMP-03 ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Imp03_disabled_by_choice_passes()
    {
        var line = ImportProbes.EvaluateAutomaticImport(Settings(false), Folders(), Now, Now.AddHours(-24), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Equal("DISABLED_BY_CHOICE", line.FindingCode);
    }

    [Fact]
    public void Imp03_enabled_with_recent_inbound_files_and_nothing_new_in_Failed_passes()
    {
        var line = ImportProbes.EvaluateAutomaticImport(Settings(),
            Folders(inbound: [Now.UtcDateTime.AddMinutes(-59)], failed: [Now.UtcDateTime.AddDays(-3)]), Now, Now.AddHours(-24), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Equal(1m, line.MetricValue);
    }

    [Fact]
    public void Imp03_an_inbound_file_older_than_sixty_minutes_is_a_Warning_with_counts_and_ages_only()
    {
        var line = ImportProbes.EvaluateAutomaticImport(Settings(),
            Folders(inbound: [Now.UtcDateTime.AddMinutes(-61), Now.UtcDateTime.AddMinutes(-200)]), Now, Now.AddHours(-24), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, line.Outcome);
        Assert.Equal("INBOUND_STUCK", line.FindingCode);
        Assert.Equal("watch-folder", line.FixTaskId);
        var sub = Assert.Single(line.SubResults!);
        Assert.Equal("2", sub.Fields!["files"]);
        Assert.Equal("200", sub.Fields["oldestMinutes"]);
        Assert.DoesNotContain(@"C:\", line.Explanation);
    }

    [Fact]
    public void Imp03_new_files_in_Failed_since_the_previous_run_are_a_Warning_old_ones_are_not()
    {
        var previous = Now.AddHours(-24);
        var fresh = ImportProbes.EvaluateAutomaticImport(Settings(), Folders(failed: [previous.UtcDateTime.AddHours(1)]), Now, previous, TimeSpan.Zero);
        var old = ImportProbes.EvaluateAutomaticImport(Settings(), Folders(failed: [previous.UtcDateTime.AddHours(-1)]), Now, previous, TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Warning, fresh.Outcome);
        Assert.Equal("FAILED_NEW", fresh.FindingCode);
        Assert.Equal(HealthOutcome.Pass, old.Outcome);
    }

    [Fact]
    public void Imp03_enabled_with_a_missing_folder_is_a_Fail()
    {
        var folders = new FakeFolders(new() { [Inbound] = [], [Processed] = [], [Failed] = null, [Output] = [] });

        var line = ImportProbes.EvaluateAutomaticImport(Settings(), folders, Now, Now.AddHours(-24), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("FOLDER_MISSING", line.FindingCode);
        Assert.Contains("Failed", line.Explanation);
    }

    [Fact]
    public void Imp03_enabled_with_a_folder_the_path_policy_refuses_is_a_Fail()
    {
        var settings = new WatchFolderSettings(Inbound, Inbound, Failed, Output, true, DateTime.UtcNow, "test"); // inbound used twice

        var line = ImportProbes.EvaluateAutomaticImport(settings, Folders(), Now, Now.AddHours(-24), TimeSpan.Zero);

        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("FOLDER_NOT_ALLOWED", line.FindingCode);
    }
}
