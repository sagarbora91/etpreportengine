extern alias EtpApplication;

using System.Globalization;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

using ControlledReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IControlledReportQuery;
using OperationalReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IOperationalReportQuery<Etp.Reporting.Reporting.DailySalesReportDocument>;
using ManagementTrendQuery = EtpApplication::Etp.Reporting.Application.Reports.IManagementTrendQuery;
using ApplicationReportScope = EtpApplication::Etp.Reporting.Application.Reports.ReportScope;
using ApplicationSalesDimension = EtpApplication::Etp.Reporting.Application.Reports.ReportSalesDimension;
using TenderVarianceDiagnostic = EtpApplication::Etp.Reporting.Application.Reports.ITenderVarianceDiagnostic;
using TenderReconciliation = EtpApplication::Etp.Reporting.Application.Reports.TenderReconciliationReport;

/// <summary>
/// RPT-01..22 (design 5.2, Reports): one health line per catalogue report, run for the business date D-1 through the
/// same query classes and the same status helpers as the Reports screen, one report at a time.
/// <para>
/// Outcome rules (5.1): a report that completes is a <b>Pass</b> whatever its own status word says (Blocked or Failed
/// because of data is the data checks' business; the word is shown for information). <b>Warning</b> = slower than
/// <see cref="SlowLimit"/>, or more than 3 times the previous run's time. <b>Fail</b> = an exception (also written to
/// the diagnostics log as <c>HEALTH_REPORT_PROBE_FAILED</c> with the report code, so it enters the error register) or
/// a timeout (<see cref="HealthCheckBudget.PerReportTimeout"/>, SQL -2). <b>Skipped</b> = not run (no Retail store,
/// time budget used up, RPT-18 without an RPT-16 result for the store).
/// </para>
/// <para>
/// Read-only: only the report query classes are called; the view is never touched, so no <c>ReportRun</c> audit row is
/// written. Rows are counted and dropped inside the call; nothing from a row (document numbers, customer names, item
/// codes) reaches a result, a field, a log line or a field of this object. The one exception in time is RPT-16's tender
/// reconciliation, kept in a per-run local for RPT-18 and released when <see cref="RunAsync"/> returns.
/// </para>
/// <para>This probe is self-contained: it does not publish RPT-16's result to <see cref="HealthCheckContext.Items"/>.</para>
/// </summary>
public sealed class ReportProbes : IHealthProbe
{
    /// <summary>A report that takes longer than this (and finishes inside the per-report timeout) is a Warning.</summary>
    public static readonly TimeSpan SlowLimit = TimeSpan.FromSeconds(10);

    /// <summary>A line is also a Warning when it takes more than this many times the previous run's time...</summary>
    public const decimal SlowerThanPreviousFactor = 3m;

    /// <summary>...and takes at least this many seconds (so 0.1 s -> 0.4 s is not "slow").</summary>
    public const decimal SlowerThanPreviousFloorSeconds = 2m;

    private const string FindingOk = "OK";
    private const string FindingSlow = "REPORT_SLOW";
    private const string FindingSlowerThanUsual = "REPORT_SLOWER_THAN_USUAL";
    private const string FindingFailed = "REPORT_PROBE_FAILED";
    private const string FindingTimeout = "REPORT_TIMEOUT";
    private const string FindingNoStore = "NO_RETAIL_STORE";
    private const string FindingBudget = "BUDGET_EXHAUSTED";
    private const string FindingNoTender = "TENDER_RESULT_UNAVAILABLE";

    private readonly ControlledReportQuery controlled;
    private readonly OperationalReportQuery operational;
    private readonly ManagementTrendQuery trend;
    private readonly TenderVarianceDiagnostic diagnostic;
    private readonly Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<CashBookDay>>> cashBookLoader;
    private readonly TimeProvider time;
    private readonly Action<Exception, string, string?, DateOnly> failureRecorder;
    private readonly IReadOnlyList<Spec> specs;

    /// <param name="cashBookLoader">store, from, to, token -> the cash book days (RPT-17).</param>
    /// <param name="timeProvider">For tests; the system clock when null.</param>
    /// <param name="failureRecorder">report code, store (null = all), business date; writes the diagnostics entry. Null = <see cref="DesktopDiagnostics"/>.</param>
    public ReportProbes(
        ControlledReportQuery controlled,
        OperationalReportQuery operational,
        ManagementTrendQuery trend,
        TenderVarianceDiagnostic diagnostic,
        Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<CashBookDay>>> cashBookLoader,
        TimeProvider? timeProvider = null,
        Action<Exception, string, string?, DateOnly>? failureRecorder = null)
    {
        this.controlled = controlled ?? throw new ArgumentNullException(nameof(controlled));
        this.operational = operational ?? throw new ArgumentNullException(nameof(operational));
        this.trend = trend ?? throw new ArgumentNullException(nameof(trend));
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        this.cashBookLoader = cashBookLoader ?? throw new ArgumentNullException(nameof(cashBookLoader));
        time = timeProvider ?? TimeProvider.System;
        this.failureRecorder = failureRecorder ?? RecordFailure;
        specs = BuildSpecs();
        CheckIds = specs.Select(x => x.Id).ToArray();
    }

    /// <summary>The production wiring: the same query class the Reports screen uses, on <paramref name="connectionString"/>.</summary>
    public static ReportProbes ForConnection(string connectionString, TenderVarianceDiagnostic? tenderDiagnostic = null)
    {
        var query = new Etp.Reporting.Infrastructure.SqlServer.SqlServerApplicationReportQuery(connectionString);
        var cash = new Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository(connectionString);
        return new ReportProbes(query, query, query,
            tenderDiagnostic ?? new Etp.Reporting.Infrastructure.SqlServer.ReportingTenderVarianceDiagnostic(),
            (store, from, to, token) => cash.LoadCashBookAsync(store, from, to, token));
    }

    public IReadOnlyList<string> CheckIds { get; }

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = new RunState();
        var results = new List<HealthCheckResult>(specs.Count);
        try
        {
            foreach (var spec in specs)
                results.Add(await RunCheckAsync(spec, context, state, cancellationToken).ConfigureAwait(false));
        }
        finally { state.Tender.Clear(); }
        return results;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The 22 checks. Each call returns only (status word, row count); the rows die inside the lambda.
    // ---------------------------------------------------------------------------------------------------------------

    private IReadOnlyList<Spec> BuildSpecs() =>
    [
        new(HealthCheckId.Rpt01, "dsr", "Daily Sales / DSR", false, async (c, ct) =>
        {
            var rows = await operational.LoadDsrAsync(c.Day, [], ct).ConfigureAwait(false);
            _ = await operational.ComposeDsrDocumentAsync(c.Day, rows, ct).ConfigureAwait(false);
            var coverageBlock = ReportsWorkspaceView.DsrCoverageBlock(rows, c.Day);
            var status = coverageBlock is null && rows.Any(x => x.TySales is not null) ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked;
            return new Probed(status, rows.Count);
        }),
        new(HealthCheckId.Rpt02, "sales-store", "Store Sales Summary", true, Sales(ApplicationSalesDimension.Daily)),
        new(HealthCheckId.Rpt03, "sales-combined", "Combined Sales Summary", false, Sales(ApplicationSalesDimension.Store)),
        new(HealthCheckId.Rpt04, "invoice", "Customer-wise Invoices", false, async (c, ct) =>
            CountStatus((await operational.LoadInvoiceSummaryAsync(c.Scope(), ct).ConfigureAwait(false)).Count)),
        new(HealthCheckId.Rpt05, "sales-returns", "Returns", false, Sales(ApplicationSalesDimension.Returns)),
        new(HealthCheckId.Rpt06, "sales-brand", "Brand-wise Sales", false, Sales(ApplicationSalesDimension.Brand)),
        new(HealthCheckId.Rpt07, "sales-segment", "Brand-Segment Sales", false, Sales(ApplicationSalesDimension.BrandSegment)),
        new(HealthCheckId.Rpt08, "sales-item", "Item-wise Sales", false, Sales(ApplicationSalesDimension.Item)),
        new(HealthCheckId.Rpt09, "stock-closing", "Closing Stock", false, async (c, ct) =>
            CountStatus((await operational.LoadStockInventoryAsync(c.Scope(), ct).ConfigureAwait(false)).Count)),
        new(HealthCheckId.Rpt10, "stock-physical", "Physical Stock", true, async (c, ct) =>
        {
            var rows = await operational.LoadPhysicalStockAsync(c.Store!, c.Day, ct).ConfigureAwait(false);
            var status = rows.Any(x => x.Status == "FAIL") ? ReconciliationStatus.Failed
                : rows.Count == 0 || rows.Any(x => x.Status != "PASS") ? ReconciliationStatus.Blocked
                : ReconciliationStatus.Passed;
            return new Probed(status, rows.Count);
        }),
        new(HealthCheckId.Rpt11, "stock-variance", "Stock Variance", false, async (c, ct) =>
        {
            var report = await controlled.RunStockReconciliationAsync(c.Scope(), ct).ConfigureAwait(false);
            return new Probed(ReportsWorkspaceView.ToReportingStatus(report.Status), report.Items.Count);
        }),
        new(HealthCheckId.Rpt12, "stock-movement", "Stock Movement", false, async (c, ct) =>
        {
            var count = (await controlled.LoadStockMovementsAsync(c.Scope(), ct).ConfigureAwait(false)).Count;
            return new Probed(ReportsWorkspaceView.StockMovementStatus(count), count);
        }),
        // RPT-13 and RPT-14 make their own call, so a fault in either path is attributed to its own line.
        new(HealthCheckId.Rpt13, "stock-brand", "Brand Stock", false, async (c, ct) =>
            CountStatus((await operational.LoadStockInventoryAsync(c.Scope(), ct).ConfigureAwait(false)).Count)),
        new(HealthCheckId.Rpt14, "stock-slow", "Slow / Exception Stock", false, async (c, ct) =>
        {
            var rows = await operational.LoadStockInventoryAsync(c.Scope(), ct).ConfigureAwait(false);
            // RA-STOCK-06: the screen applies the same rule before it counts.
            return CountStatus(rows.Count(x => StockAgeing.IsSlow(x.Quantity, x.MovementStatus)));
        }),
        new(HealthCheckId.Rpt15, "staff", "Staff/CRO Performance", false, async (c, ct) =>
        {
            var report = await operational.LoadStaffPerformanceAsync(c.Scope(), ct).ConfigureAwait(false);
            return new Probed(ReportsWorkspaceView.ToReportingStatus(report.Status), report.Rows.Count);
        }),
        new(HealthCheckId.Rpt16, "tender", "Tender Reconciliation", true, async (c, ct) =>
        {
            var report = await controlled.RunTenderReconciliationAsync(c.Scope(), ct).ConfigureAwait(false);
            c.State.Tender[c.Store!] = report; // for RPT-18 only; cleared when the run ends
            return new Probed(ReportsWorkspaceView.ToReportingStatus(report.Status), report.Documents.Count);
        }),
        new(HealthCheckId.Rpt17, "cash", "Cash Book", true, async (c, ct) =>
        {
            var days = await cashBookLoader(c.Store!, c.Day.AddDays(-6), c.Day, ct).ConfigureAwait(false);
            var status = days.All(x => x.Status == "Complete") ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked;
            return new Probed(status, days.Count);
        }),
        new(HealthCheckId.Rpt18, "tender-diagnostic", "Tender Diagnostics", true, (c, ct) =>
        {
            if (!c.State.Tender.TryGetValue(c.Store!, out var reconciliation))
                throw new ProbeSkipped(FindingNoTender, "RPT-16 has no result for this store");
            var report = diagnostic.Diagnose(reconciliation, RetailReportingPolicy.Tender.AbsoluteTolerance);
            return Task.FromResult(new Probed(ReportsWorkspaceView.ToReportingStatus(report.Status), report.Rows.Count));
        }),
        new(HealthCheckId.Rpt19, "service", "Service Sales", false, async (c, ct) =>
        {
            var rows = await operational.LoadServiceSalesAsync(c.Day, null, ct).ConfigureAwait(false);
            return new Probed(rows.Any(x => x.Total is not null) ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked, rows.Count);
        }),
        new(HealthCheckId.Rpt20, "exceptions", "Daily Exception Report", true, async (c, ct) =>
        {
            var rows = await operational.LoadDailyExceptionsAsync(c.Store!, c.Day, ct).ConfigureAwait(false);
            var (status, _) = ReportsWorkspaceView.DailyExceptionStatus(rows.Select(x => (x.Severity, x.Code)).ToArray());
            return new Probed(status, rows.Count);
        }),
        new(HealthCheckId.Rpt21, "management-trend", "Management Trend", false, async (c, ct) =>
        {
            var scope = new ApplicationReportScope(c.Day.AddDays(-29), c.Day);
            return CountStatus((await trend.LoadAsync(scope, ct).ConfigureAwait(false)).Count);
        }),
        new(HealthCheckId.Rpt22, "invoice-lineage", "Invoice Source Drill-down", false, async (c, ct) =>
            CountStatus((await operational.LoadInvoiceLineageAsync(c.Scope(), ct).ConfigureAwait(false)).Count))
    ];

    private Func<Call, CancellationToken, Task<Probed>> Sales(ApplicationSalesDimension dimension) => async (c, ct) =>
    {
        var report = await controlled.RunSalesSummaryAsync(c.Scope(), dimension, ct).ConfigureAwait(false);
        return new Probed(ReportsWorkspaceView.ToReportingStatus(report.Status), report.Rows.Count);
    };

    private static Probed CountStatus(int rows) => new(rows == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed, rows);

    // ---------------------------------------------------------------------------------------------------------------
    // Engine
    // ---------------------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunCheckAsync(Spec spec, HealthCheckContext context, RunState state, CancellationToken cancellationToken)
    {
        var area = HealthCheckArea.Reports;
        var day = context.BusinessDate;
        if (cancellationToken.IsCancellationRequested)
            return Skipped(spec, FindingBudget, "the time budget is used up");

        IReadOnlyList<string?> stores = spec.PerStore ? context.RetailStoreCodes.Cast<string?>().ToArray() : [null];
        if (stores.Count == 0)
            return Skipped(spec, FindingNoStore, "there is no active Retail store");

        // One timeout per check (design 5.3: 30 s per report); the area cap and the 120 s total are the runner's.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.Budget.PerReportTimeout);

        var subs = new List<Sub>(stores.Count);
        var checkStart = time.GetTimestamp();
        var cancelled = false;
        foreach (var store in stores)
        {
            if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
            var start = time.GetTimestamp();
            try
            {
                var probed = await spec.Run(new Call(store, day, state), timeout.Token).ConfigureAwait(false);
                var seconds = Seconds(time.GetElapsedTime(start));
                subs.Add(new Sub(store, seconds > SlowLimit.TotalSeconds ? HealthOutcome.Warning : HealthOutcome.Pass,
                    seconds > SlowLimit.TotalSeconds ? FindingSlow : FindingOk, seconds, probed.Status.ToString(), probed.Rows, null, null));
            }
            catch (ProbeSkipped skipped)
            {
                subs.Add(new Sub(store, HealthOutcome.Skipped, skipped.FindingCode, 0m, null, 0, null, skipped.Reason));
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && (ex is OperationCanceledException || IsSqlTimeout(ex)))
            {
                cancelled = true;
                break;
            }
            catch (Exception ex) when (timeout.IsCancellationRequested && (ex is OperationCanceledException || IsSqlTimeout(ex)))
            {
                subs.Add(new Sub(store, HealthOutcome.Fail, FindingTimeout, Seconds(time.GetElapsedTime(start)), null, 0, ex.GetType().Name, null));
                Record(ex, spec.Code, store, day);
                break; // the check's time is used up; the remaining stores are not tried
            }
            catch (Exception ex)
            {
                var timedOut = IsSqlTimeout(ex);
                subs.Add(new Sub(store, HealthOutcome.Fail, timedOut ? FindingTimeout : FindingFailed, Seconds(time.GetElapsedTime(start)), null, 0, ExceptionToken(ex), null));
                Record(ex, spec.Code, store, day);
            }
        }

        var elapsed = time.GetElapsedTime(checkStart);
        if (cancelled)
        {
            var skippedResult = Skipped(spec, FindingBudget, "the time budget is used up");
            return skippedResult with { Duration = elapsed, SubResults = ToSubResults(spec, subs) };
        }
        return Combine(spec, context, subs, stores.Count, elapsed, area);
    }

    private HealthCheckResult Combine(Spec spec, HealthCheckContext context, List<Sub> subs, int storeCount, TimeSpan elapsed, HealthCheckArea area)
    {
        var day = Day(context.BusinessDate);
        var seconds = Seconds(elapsed);
        var failed = subs.Where(x => x.Outcome == HealthOutcome.Fail).ToArray();
        var slow = subs.Where(x => x.Outcome == HealthOutcome.Warning).ToArray();
        var ran = subs.Where(x => x.Outcome is HealthOutcome.Pass or HealthOutcome.Warning).ToArray();
        var rows = ran.Sum(x => x.Rows);
        var fix = "report-" + spec.Code;
        var scopeText = spec.PerStore ? $"{storeCount} store(s)" : "all stores";
        var subResults = ToSubResults(spec, subs);

        if (failed.Length > 0)
        {
            var first = failed[0];
            var timedOut = failed.Any(x => x.Finding == FindingTimeout);
            var where = spec.PerStore ? $"{failed.Length} of {storeCount} store(s) ({string.Join(", ", failed.Select(x => x.Store))})" : "the run";
            var limit = Fixed((decimal)context.Budget.PerReportTimeout.TotalSeconds);
            var text = timedOut
                ? $"{spec.Name} did not finish within {limit} s for {where} on {day} (timeout, SQL -2)."
                : $"{spec.Name} failed for {where} on {day}: {first.ExceptionText}.";
            return new HealthCheckResult(spec.Id, area, HealthOutcome.Fail, timedOut ? FindingTimeout : FindingFailed, text, fix, elapsed, seconds, subResults);
        }

        if (ran.Length == 0)
        {
            // Every call was skipped (RPT-18 without RPT-16 results).
            var reason = subs.Count > 0 ? subs[0].SkipReason : "it did not run";
            var code = subs.Count > 0 ? subs[0].Finding : FindingBudget;
            return new HealthCheckResult(spec.Id, area, HealthOutcome.Skipped, code, $"{spec.Name} was not run: {reason}.", null, elapsed, null, subResults);
        }

        var statusSummary = string.Join("; ", ran.GroupBy(x => x.ReportStatus!).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{x.Key} x{x.Count()}"));
        var passText = $"{spec.Name} ran for {scopeText} on {day}: report status {statusSummary}, {rows.ToString("N0", CultureInfo.InvariantCulture)} row(s), {Fixed(seconds)} s.";

        if (slow.Length > 0)
        {
            var text = $"{spec.Name} completed but slowly on {day}: {Fixed(slow.Max(x => x.Seconds))} s for {slow.Length} of {ran.Length} call(s) (a normal run is under {Fixed((decimal)SlowLimit.TotalSeconds)} s). Report status {statusSummary}.";
            return new HealthCheckResult(spec.Id, area, HealthOutcome.Warning, FindingSlow, text, fix, elapsed, seconds, subResults);
        }

        var previous = context.PreviousRun?.Results.FirstOrDefault(x => x.CheckId == spec.Id)?.MetricValue;
        if (previous is { } before && before > 0m && seconds >= SlowerThanPreviousFloorSeconds && seconds > SlowerThanPreviousFactor * Math.Max(before, 0.5m))
        {
            var text = $"{spec.Name} took {Fixed(seconds)} s on {day}, more than {SlowerThanPreviousFactor:0} times the previous run ({Fixed(before)} s). Report status {statusSummary}.";
            return new HealthCheckResult(spec.Id, area, HealthOutcome.Warning, FindingSlowerThanUsual, text, fix, elapsed, seconds, subResults);
        }

        return new HealthCheckResult(spec.Id, area, HealthOutcome.Pass, FindingOk, passText, null, elapsed, seconds, subResults);
    }

    private static IReadOnlyList<HealthCheckSubResult>? ToSubResults(Spec spec, List<Sub> subs)
    {
        if (!spec.PerStore) return null;
        return subs.Select(x =>
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["seconds"] = Fixed(x.Seconds) };
            if (x.ReportStatus is not null) { fields["reportStatus"] = x.ReportStatus; fields["rows"] = x.Rows.ToString(CultureInfo.InvariantCulture); }
            if (x.ExceptionText is not null) fields["exception"] = x.ExceptionText;
            if (x.SkipReason is not null) fields["skipped"] = x.SkipReason;
            return new HealthCheckSubResult(x.Store ?? spec.Code, x.Outcome, x.Finding, x.Seconds, fields);
        }).ToArray();
    }

    private static HealthCheckResult Skipped(Spec spec, string finding, string reason) =>
        new(spec.Id, HealthCheckArea.Reports, HealthOutcome.Skipped, finding, $"{spec.Name} was not run: {reason}.", null, TimeSpan.Zero);

    private void Record(Exception exception, string reportCode, string? store, DateOnly day)
    {
        try { failureRecorder(exception, reportCode, store, day); }
        catch { /* the diagnostics log must never turn a health line into a crash */ }
    }

    private static void RecordFailure(Exception exception, string reportCode, string? store, DateOnly day) =>
        DesktopDiagnostics.Record(exception, "Health.ReportProbe", "HEALTH_REPORT_PROBE_FAILED",
            context: new DesktopDiagnosticContext(reportCode, store ?? DesktopDiagnosticContext.AllStores, day, day),
            operation: "Health check report probe failed");

    private static bool IsSqlTimeout(Exception exception) =>
        exception is SqlException sql && (sql.Number == -2 || sql.Errors.Cast<SqlError>().Any(x => x.Number == -2));

    /// <summary>Exception type name plus the SQL error number when there is one: a token, never the message.</summary>
    private static string ExceptionToken(Exception exception) =>
        exception is SqlException sql ? $"{sql.GetType().Name} (SQL {sql.Number})" : exception.GetType().Name;

    private static decimal Seconds(TimeSpan span) => Math.Round((decimal)span.TotalSeconds, 2, MidpointRounding.AwayFromZero);

    private static string Fixed(decimal value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------------------------------------------------------

    private sealed record Spec(string Id, string Code, string Name, bool PerStore, Func<Call, CancellationToken, Task<Probed>> Run);

    private sealed record Call(string? Store, DateOnly Day, RunState State)
    {
        /// <summary>The Reports screen's scope for a single business date: From = To = D-1, one store or all.</summary>
        public ApplicationReportScope Scope() => new(Day, Day, Store is null ? null : [Store]);
    }

    private readonly record struct Probed(ReconciliationStatus Status, int Rows);

    private sealed record Sub(string? Store, HealthOutcome Outcome, string Finding, decimal Seconds, string? ReportStatus, int Rows, string? ExceptionText, string? SkipReason);

    private sealed class RunState
    {
        public Dictionary<string, TenderReconciliation> Tender { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProbeSkipped(string findingCode, string reason) : Exception(reason)
    {
        public string FindingCode { get; } = findingCode;
        public string Reason { get; } = reason;
    }
}
