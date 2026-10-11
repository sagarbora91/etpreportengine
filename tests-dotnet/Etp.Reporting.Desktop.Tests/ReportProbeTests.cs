using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Reporting;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 self-diagnostics, lane L5: RPT-01..22 (design 5.2). Fake queries only; no database, no clock waits (a fake
/// <see cref="TimeProvider"/> is advanced inside the fake calls), no diagnostics log (the failure recorder is replaced).
/// </summary>
public sealed class ReportProbeTests
{
    private static readonly DateOnly Day = new(2026, 10, 10);
    private static readonly string[] Stores = ["WLMHW", "HEMW"];
    private static readonly string[] PerStoreIds = ["RPT-02", "RPT-10", "RPT-16", "RPT-17", "RPT-18", "RPT-20"];

    [Fact]
    public void The_probe_answers_exactly_RPT_01_to_RPT_22_in_catalogue_order()
    {
        var expected = Enumerable.Range(1, 22).Select(x => $"RPT-{x:00}").ToArray();
        Assert.Equal(expected, Create(new Fakes()).Probe.CheckIds);
        Assert.Equal(expected, HealthCheckId.All.Where(x => x.StartsWith("RPT-", StringComparison.Ordinal)).ToArray());
        Assert.Equal(22, ProductReportCatalogue.All.Count);
    }

    [Fact]
    public async Task Reports_that_complete_with_a_Blocked_status_word_are_a_Pass()
    {
        var run = Create(new Fakes { TenderStatus = ReportStatus.Blocked });

        var results = await run.Probe.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(expected: Enumerable.Range(1, 22).Select(x => $"RPT-{x:00}").ToArray(), results.Select(x => x.CheckId).ToArray());
        Assert.All(results, x => { Assert.Equal(HealthOutcome.Pass, x.Outcome); Assert.Equal(HealthCheckArea.Reports, x.Area); Assert.Equal("OK", x.FindingCode); Assert.Null(x.FixTaskId); });
        var tender = results.Single(x => x.CheckId == "RPT-16");
        Assert.Contains("Blocked", tender.Explanation);
        Assert.All(tender.SubResults!, sub => Assert.Equal("Blocked", sub.Fields!["reportStatus"]));
        Assert.Empty(run.Recorded);
    }

    [Fact]
    public async Task An_exception_is_a_Fail_recorded_with_the_report_code_and_never_quotes_the_message()
    {
        var fakes = new Fakes
        {
            Hook = (name, _, _) => name == nameof(Fakes.LoadStaffPerformanceAsync)
                ? throw new InvalidOperationException("PLANTED-CUSTOMER-NAME 99999999 C:\\Secret\\Path")
                : Task.CompletedTask
        };
        var run = Create(fakes);

        var results = await run.Probe.RunAsync(Context(), CancellationToken.None);

        var staff = results.Single(x => x.CheckId == "RPT-15");
        Assert.Equal(HealthOutcome.Fail, staff.Outcome);
        Assert.Equal("REPORT_PROBE_FAILED", staff.FindingCode);
        Assert.Equal("report-staff", staff.FixTaskId);
        Assert.Contains("InvalidOperationException", staff.Explanation);
        Assert.DoesNotContain("PLANTED", Flatten(results));
        Assert.DoesNotContain("Secret", Flatten(results));
        Assert.Equal(new[] { ("staff", (string?)null) }, run.Recorded.Select(x => (x.ReportCode, x.Store)).ToArray());
        Assert.Equal(21, results.Count(x => x.Outcome == HealthOutcome.Pass));
    }

    [Fact]
    public async Task A_per_store_exception_names_the_store_and_the_other_store_still_runs()
    {
        var fakes = new Fakes { Hook = (name, scope, _) => name == nameof(Fakes.RunSalesSummaryAsync) && scope?.StoreCodes is ["HEMW"] ? throw new TimeoutException("x") : Task.CompletedTask };
        var run = Create(fakes);

        var results = await run.Probe.RunAsync(Context(), CancellationToken.None);

        var line = results.Single(x => x.CheckId == "RPT-02");
        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal(new[] { "WLMHW", "HEMW" }, line.SubResults!.Select(x => x.Key).ToArray());
        Assert.Equal(new[] { HealthOutcome.Pass, HealthOutcome.Fail }, line.SubResults!.Select(x => x.Outcome).ToArray());
        Assert.Contains("HEMW", line.Explanation);
        Assert.Equal(new[] { ("sales-store", (string?)"HEMW") }, run.Recorded.Select(x => (x.ReportCode, x.Store)).ToArray());
        // the all-stores sales reports use the same method but another dimension and are unaffected
        Assert.Equal(HealthOutcome.Pass, results.Single(x => x.CheckId == "RPT-03").Outcome);
    }

    [Fact]
    public async Task A_slow_report_is_a_Warning_and_a_Blocked_one_stays_a_Pass()
    {
        var clock = new FakeTime();
        var fakes = new Fakes { Clock = clock, Hook = (name, _, _) => { if (name == nameof(Fakes.LoadServiceSalesAsync)) clock.Advance(TimeSpan.FromSeconds(12)); return Task.CompletedTask; } };
        var run = Create(fakes, clock);

        var results = await run.Probe.RunAsync(Context(), CancellationToken.None);

        var service = results.Single(x => x.CheckId == "RPT-19");
        Assert.Equal(HealthOutcome.Warning, service.Outcome);
        Assert.Equal("REPORT_SLOW", service.FindingCode);
        Assert.Equal(12m, service.MetricValue);
        Assert.Equal("report-service", service.FixTaskId);
        Assert.Contains("Blocked", service.Explanation); // the report itself is Blocked (no rows) and the line still says so
        Assert.Equal(21, results.Count(x => x.Outcome == HealthOutcome.Pass));
    }

    [Fact]
    public async Task Exactly_the_slow_limit_is_still_a_Pass()
    {
        var clock = new FakeTime();
        var fakes = new Fakes { Clock = clock, Hook = (name, _, _) => { if (name == nameof(Fakes.LoadAsync)) clock.Advance(TimeSpan.FromSeconds(10)); return Task.CompletedTask; } };

        var results = await Create(fakes, clock).Probe.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(HealthOutcome.Pass, results.Single(x => x.CheckId == "RPT-21").Outcome);
    }

    [Theory]
    [InlineData(1.0, 6, HealthOutcome.Warning)]
    [InlineData(3.0, 6, HealthOutcome.Pass)]
    [InlineData(0.1, 1, HealthOutcome.Pass)]
    public async Task More_than_three_times_the_previous_run_is_a_Warning_but_only_when_it_is_not_trivially_short(double previousSeconds, int seconds, HealthOutcome expected)
    {
        var clock = new FakeTime();
        var fakes = new Fakes { Clock = clock, Hook = (name, _, _) => { if (name == nameof(Fakes.LoadInvoiceLineageAsync)) clock.Advance(TimeSpan.FromSeconds(seconds)); return Task.CompletedTask; } };
        var previous = new HealthCheckPreviousRun(7, DateTimeOffset.UtcNow.AddDays(-1),
        [
            new HealthCheckResult("RPT-22", HealthCheckArea.Reports, HealthOutcome.Pass, "OK", "x", null, TimeSpan.Zero, (decimal)previousSeconds)
        ]);

        var results = await Create(fakes, clock).Probe.RunAsync(Context(previous: previous), CancellationToken.None);

        var line = results.Single(x => x.CheckId == "RPT-22");
        Assert.Equal(expected, line.Outcome);
        if (expected == HealthOutcome.Warning) Assert.Equal("REPORT_SLOWER_THAN_USUAL", line.FindingCode);
    }

    [Fact]
    public async Task A_report_that_does_not_finish_inside_the_per_report_timeout_is_a_Fail_and_the_next_report_still_runs()
    {
        var fakes = new Fakes { Hook = (name, _, ct) => name == nameof(Fakes.LoadStaffPerformanceAsync) ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask };
        var run = Create(fakes);
        var budget = new HealthCheckBudget(TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(60), HealthCheckBudget.Default.AreaCaps);

        var results = await run.Probe.RunAsync(Context(budget: budget), CancellationToken.None);

        var staff = results.Single(x => x.CheckId == "RPT-15");
        Assert.Equal(HealthOutcome.Fail, staff.Outcome);
        Assert.Equal("REPORT_TIMEOUT", staff.FindingCode);
        Assert.Contains("SQL -2", staff.Explanation);
        Assert.Equal(HealthOutcome.Pass, results.Single(x => x.CheckId == "RPT-16").Outcome);
        Assert.Equal(new[] { ("staff", (string?)null) }, run.Recorded.Select(x => (x.ReportCode, x.Store)).ToArray());
    }

    [Fact]
    public async Task A_SQL_timeout_error_minus_2_is_a_timeout_Fail_with_the_number_but_not_the_message()
    {
        var sql = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(-2, "Execution Timeout Expired for WLMHW invoice PLANTED"));
        var fakes = new Fakes { Hook = (name, _, _) => name == nameof(Fakes.LoadAsync) ? throw sql : Task.CompletedTask };

        var results = await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        var trend = results.Single(x => x.CheckId == "RPT-21");
        Assert.Equal(HealthOutcome.Fail, trend.Outcome);
        Assert.Equal("REPORT_TIMEOUT", trend.FindingCode);
        Assert.DoesNotContain("PLANTED", Flatten(results));
    }

    [Fact]
    public async Task Other_SQL_errors_are_a_Fail_that_names_the_SQL_number_only()
    {
        var sql = SqlExceptionFactory.Create(new SqlExceptionFactory.Error(229, "The SELECT permission was denied on PLANTED"));
        var fakes = new Fakes { Hook = (name, _, _) => name == nameof(Fakes.LoadInvoiceSummaryAsync) ? throw sql : Task.CompletedTask };

        var results = await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        var invoice = results.Single(x => x.CheckId == "RPT-04");
        Assert.Equal(HealthOutcome.Fail, invoice.Outcome);
        Assert.Equal("REPORT_PROBE_FAILED", invoice.FindingCode);
        Assert.Contains("SQL 229", invoice.Explanation);
        Assert.DoesNotContain("PLANTED", Flatten(results));
    }

    [Fact]
    public async Task Rows_are_counted_and_dropped_nothing_from_a_row_reaches_a_result()
    {
        var fakes = new Fakes { PlantRows = true };

        var results = await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        var text = Flatten(results);
        foreach (var planted in new[] { "PLANTED-CUSTOMER", "PLANTED-DOC", "PLANTED-ITEM", "PLANTED-WORKBOOK", "PLANTED-CRO", "PLANTED-MESSAGE" })
            Assert.DoesNotContain(planted, text);
        // the counts are there
        Assert.Contains("2 row(s)", results.Single(x => x.CheckId == "RPT-04").Explanation);
        Assert.Contains("2 row(s)", results.Single(x => x.CheckId == "RPT-22").Explanation);
        Assert.All(results, x => Assert.Equal(HealthOutcome.Pass, x.Outcome));
    }

    [Fact]
    public async Task Per_store_reports_run_once_per_Retail_store_and_all_store_reports_run_with_no_store_filter()
    {
        var fakes = new Fakes();
        var results = await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        foreach (var id in PerStoreIds)
            Assert.Equal(Stores, results.Single(x => x.CheckId == id).SubResults!.Select(x => x.Key).ToArray());
        Assert.All(results.Where(x => !PerStoreIds.Contains(x.CheckId)), x => Assert.Null(x.SubResults));

        Assert.Equal(2, fakes.Seen.Count(x => x.Name == nameof(Fakes.RunSalesSummaryAsync) && x.Dimension == ReportSalesDimension.Daily));
        Assert.All(fakes.Seen.Where(x => x.Name == nameof(Fakes.RunSalesSummaryAsync) && x.Dimension != ReportSalesDimension.Daily), x => Assert.Null(x.Scope!.StoreCodes));
        Assert.Equal(2, fakes.Seen.Count(x => x.Name == nameof(Fakes.LoadPhysicalStockAsync)));
        Assert.Equal(2, fakes.Seen.Count(x => x.Name == nameof(Fakes.LoadDailyExceptionsAsync)));
        Assert.Equal(2, fakes.CashCalls.Count);
        Assert.Equal(2, fakes.TenderDiagnosed);
    }

    [Fact]
    public async Task Every_report_is_run_for_the_business_date_D_minus_1_only()
    {
        var fakes = new Fakes();
        await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        Assert.All(fakes.Seen.Where(x => x.Scope is not null && x.Name != nameof(Fakes.LoadAsync)), x => { Assert.Equal(Day, x.Scope!.DateFrom); Assert.Equal(Day, x.Scope.DateTo); });
        Assert.All(fakes.Seen.Where(x => x.Date is not null), x => Assert.Equal(Day, x.Date));
        Assert.All(fakes.CashCalls, x => { Assert.Equal(Day.AddDays(-6), x.From); Assert.Equal(Day, x.To); }); // D-7..D-1 relative to today
        var trend = fakes.Seen.Single(x => x.Name == nameof(Fakes.LoadAsync));
        Assert.Equal(Day.AddDays(-29), trend.Scope!.DateFrom);
        Assert.Equal(Day, trend.Scope.DateTo);
        Assert.Null(trend.Scope.StoreCodes);
    }

    [Fact]
    public async Task Tender_diagnostics_reuse_the_tender_result_and_make_no_second_tender_query()
    {
        var fakes = new Fakes();
        await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(2, fakes.Seen.Count(x => x.Name == nameof(Fakes.RunTenderReconciliationAsync)));
        Assert.Equal(2, fakes.TenderDiagnosed);
    }

    [Fact]
    public async Task A_failed_tender_reconciliation_leaves_the_diagnostics_line_Skipped_not_Failed()
    {
        var fakes = new Fakes { Hook = (name, _, _) => name == nameof(Fakes.RunTenderReconciliationAsync) ? throw new InvalidOperationException("x") : Task.CompletedTask };

        var results = await Create(fakes).Probe.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(HealthOutcome.Fail, results.Single(x => x.CheckId == "RPT-16").Outcome);
        var diagnostics = results.Single(x => x.CheckId == "RPT-18");
        Assert.Equal(HealthOutcome.Skipped, diagnostics.Outcome);
        Assert.Equal("TENDER_RESULT_UNAVAILABLE", diagnostics.FindingCode);
        Assert.Equal(0, fakes.TenderDiagnosed);
    }

    [Fact]
    public async Task With_no_Retail_store_the_per_store_checks_are_Skipped_and_the_rest_still_run()
    {
        var results = await Create(new Fakes()).Probe.RunAsync(Context(stores: []), CancellationToken.None);

        Assert.Equal(22, results.Count);
        Assert.All(results.Where(x => PerStoreIds.Contains(x.CheckId)), x => { Assert.Equal(HealthOutcome.Skipped, x.Outcome); Assert.Equal("NO_RETAIL_STORE", x.FindingCode); });
        Assert.All(results.Where(x => !PerStoreIds.Contains(x.CheckId)), x => Assert.Equal(HealthOutcome.Pass, x.Outcome));
    }

    [Fact]
    public async Task An_already_cancelled_run_returns_a_Skipped_line_for_every_id_and_calls_nothing()
    {
        var fakes = new Fakes();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var results = await Create(fakes).Probe.RunAsync(Context(), cts.Token);

        Assert.Equal(22, results.Count);
        Assert.All(results, x => { Assert.Equal(HealthOutcome.Skipped, x.Outcome); Assert.Equal("BUDGET_EXHAUSTED", x.FindingCode); });
        Assert.Empty(fakes.Seen);
    }

    [Fact]
    public async Task Cancellation_in_the_middle_of_a_run_skips_that_check_and_the_rest_without_throwing_or_recording_a_failure()
    {
        using var cts = new CancellationTokenSource();
        var fakes = new Fakes
        {
            Hook = (name, _, _) =>
            {
                if (name != nameof(Fakes.LoadStockInventoryAsync)) return Task.CompletedTask;
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }
        };
        var run = Create(fakes);

        var results = await run.Probe.RunAsync(Context(), cts.Token);

        Assert.Equal(22, results.Count);
        Assert.All(results.Take(8), x => Assert.Equal(HealthOutcome.Pass, x.Outcome));          // RPT-01..08
        Assert.All(results.Skip(8), x => Assert.Equal(HealthOutcome.Skipped, x.Outcome));       // RPT-09 (cancelled) .. RPT-22
        Assert.Empty(run.Recorded);
    }

    [Fact]
    public async Task A_failure_recorder_that_throws_does_not_break_the_run()
    {
        var fakes = new Fakes { Hook = (name, _, _) => name == nameof(Fakes.LoadAsync) ? throw new InvalidOperationException("x") : Task.CompletedTask };
        var probe = new ReportProbes(fakes, fakes, fakes, fakes, fakes.LoadCashBookAsync, null, (_, _, _, _) => throw new IOException("log disk full"));

        var results = await probe.RunAsync(Context(), CancellationToken.None);

        Assert.Equal(HealthOutcome.Fail, results.Single(x => x.CheckId == "RPT-21").Outcome);
        Assert.Equal(22, results.Count);
    }

    [Fact]
    public void The_probe_has_no_reference_to_the_view_or_the_audit_repository()
    {
        var source = File.ReadAllText(FindSource("Modules", "HealthCheck", "ReportProbes.cs"));
        // code only: the summary comment names both on purpose
        var code = string.Join('\n', source.Split('\n').Where(x => !x.TrimStart().StartsWith("//", StringComparison.Ordinal) && !x.TrimStart().StartsWith("///", StringComparison.Ordinal)));
        Assert.DoesNotContain("OperationalAuditRepository", code);
        Assert.DoesNotContain("new ReportsWorkspaceView", code);
        Assert.DoesNotContain("auditRecorder", code);
        Assert.DoesNotContain("RunReportAsync", code);
        Assert.DoesNotContain("INSERT ", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", code, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------------------------------------------

    private static (ReportProbes Probe, List<(string ReportCode, string? Store)> Recorded) Create(Fakes fakes, TimeProvider? clock = null)
    {
        var recorded = new List<(string ReportCode, string? Store)>();
        var probe = new ReportProbes(fakes, fakes, fakes, fakes, fakes.LoadCashBookAsync, clock, (_, code, store, _) => recorded.Add((code, store)));
        return (probe, recorded);
    }

    private static HealthCheckContext Context(IReadOnlyList<string>? stores = null, HealthCheckPreviousRun? previous = null, HealthCheckBudget? budget = null) =>
        new(Day, HealthCheckTrigger.Manual, DateTimeOffset.UtcNow, stores ?? Stores, previous, budget ?? HealthCheckBudget.Default, new Dictionary<string, object>());

    private static string Flatten(IEnumerable<HealthCheckResult> results) => string.Join('\n', results.SelectMany(x =>
        new[] { x.CheckId, x.FindingCode, x.Explanation, x.FixTaskId ?? "" }.Concat((x.SubResults ?? [])
            .SelectMany(s => new[] { s.Key, s.FindingCode ?? "" }.Concat((s.Fields ?? new Dictionary<string, string>()).SelectMany(f => new[] { f.Key, f.Value }))))));

    private static string FindSource(params string[] relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(new[] { dir.FullName, "src", "Etp.Reporting.Desktop" }.Concat(relative).ToArray());
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException(string.Join('/', relative));
    }

    private sealed class FakeTime : TimeProvider
    {
        private long milliseconds;
        public override long GetTimestamp() => Interlocked.Read(ref milliseconds);
        public override long TimestampFrequency => 1000;
        public void Advance(TimeSpan by) => Interlocked.Add(ref milliseconds, (long)by.TotalMilliseconds);
    }

    private sealed record SeenCall(string Name, ReportScope? Scope, ReportSalesDimension? Dimension, DateOnly? Date);

    private sealed record CashCall(string Store, DateOnly From, DateOnly To);

    private sealed class Fakes : IControlledReportQuery, IOperationalReportQuery<DailySalesReportDocument>, IManagementTrendQuery, ITenderVarianceDiagnostic
    {
        public Func<string, ReportScope?, CancellationToken, Task>? Hook { get; init; }
        public FakeTime? Clock { get; init; }
        public ReportStatus TenderStatus { get; init; } = ReportStatus.Passed;
        public bool PlantRows { get; init; }
        public List<SeenCall> Seen { get; } = [];
        public List<CashCall> CashCalls { get; } = [];
        public int TenderDiagnosed { get; private set; }

        private async Task Enter(string name, ReportScope? scope, CancellationToken ct, ReportSalesDimension? dimension = null, DateOnly? date = null)
        {
            Seen.Add(new SeenCall(name, scope, dimension, date));
            if (Hook is not null) await Hook(name, scope, ct);
        }

        public async Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(RunSalesSummaryAsync), scope, cancellationToken, dimension);
            return new SalesSummaryReport(dimension, ReportStatus.Passed, [new SalesSummaryRecord(PlantRows ? "PLANTED-ITEM" : "K", 1m, 100m, 1, 0)], "policy", PlantRows ? "PLANTED-MESSAGE" : "ok");
        }

        public async Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(RunTenderReconciliationAsync), scope, cancellationToken);
            var store = scope.StoreCodes?.FirstOrDefault() ?? "ALL";
            return new TenderReconciliationReport(TenderStatus, [new TenderDocumentRecord(store, PlantRows ? "PLANTED-DOC" : "D1", 100m, 100m, 0m, ReportStatus.Passed)], 100m, 100m, 0m, "rule", PlantRows ? "PLANTED-MESSAGE" : "ok");
        }

        public async Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(RunStockReconciliationAsync), scope, cancellationToken);
            return new StockReconciliationReport(ReportStatus.Passed, [], "rule", "ok");
        }

        public async Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadStockMovementsAsync), scope, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadInvoiceSummaryAsync), scope, cancellationToken);
            return PlantRows
                ? [new(Day, "WLMHW", "PLANTED-DOC", "INV", 1m, 100m, 1, "PLANTED-CUSTOMER"), new(Day, "HEMW", "PLANTED-DOC", "INV", 1m, 100m, 1, "PLANTED-CUSTOMER")]
                : [];
        }

        public async Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadInvoiceLineageAsync), scope, cancellationToken);
            return PlantRows
                ? [new(Day, "WLMHW", "PLANTED-DOC", "1", "PLANTED-ITEM", null, null, "INV", 1m, 100m, "PLANTED-CRO", "PLANTED-WORKBOOK", "Sheet", 2), new(Day, "HEMW", "PLANTED-DOC", "1", "PLANTED-ITEM", null, null, "INV", 1m, 100m, "PLANTED-CRO", "PLANTED-WORKBOOK", "Sheet", 3)]
                : [];
        }

        public async Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadDsrAsync), null, cancellationToken, date: businessDate);
            return [];
        }

        public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default) =>
            Task.FromResult(DailySalesReportBuilder.Build(businessDate, [], [], new Dictionary<string, decimal?>()));

        public async Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadStaffPerformanceAsync), scope, cancellationToken);
            return new StaffPerformanceReport([], 0m, 0m, 0m, ReportStatus.Passed, "ok", "policy");
        }

        public async Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadServiceSalesAsync), null, cancellationToken, date: businessDate);
            return [];
        }

        public Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadPhysicalStockAsync), null, cancellationToken, date: businessDate);
            return [];
        }

        public async Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadStockInventoryAsync), scope, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadDailyExceptionsAsync), null, cancellationToken, date: businessDate);
            return PlantRows
                ? [new("INFO", "Area", "CODE", storeCode, businessDate, "PLANTED-DOC", "PLANTED-ITEM", 1m, "PLANTED-WORKBOOK", "Sheet", 1, "PLANTED-MESSAGE", "action")]
                : [];
        }

        /// <summary>The management trend query (IManagementTrendQuery.LoadAsync).</summary>
        public async Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default)
        {
            await Enter(nameof(LoadAsync), scope, cancellationToken);
            return [];
        }

        public async Task<IReadOnlyList<CashBookDay>> LoadCashBookAsync(string store, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            CashCalls.Add(new CashCall(store, from, to));
            if (Hook is not null) await Hook(nameof(LoadCashBookAsync), null, cancellationToken);
            return [];
        }

        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance)
        {
            TenderDiagnosed++;
            return new TenderVarianceDiagnosticReport(ReportStatus.Passed, [], 0, 0m, "rule", "ok");
        }
    }
}
