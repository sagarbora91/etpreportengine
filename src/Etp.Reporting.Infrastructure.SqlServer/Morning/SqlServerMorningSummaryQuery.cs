using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.DailyWorkflow;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Morning;
using Etp.Reporting.Application.NoSaleDays;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.Infrastructure.SqlServer.Morning;

/// <summary>
/// The Morning page's read model (1.12 U1, wave plan C-a). It reads only what ETP already keeps: the active Retail stores,
/// each store's Close day state (<see cref="SqlServerDailyWorkflowService"/>), the readiness items
/// (<see cref="SqlServerDailyReadinessQuery"/>), the import history of the day, the latest nightly health summary
/// (<c>diag.load_health_check_summary</c>), the Service freshness strip, N1's no-sale marks, the shop result folder and the
/// off-PC backup age; <see cref="MorningSummaryBuilder"/> turns them into the page. Each source is read on its own: one
/// that fails leaves its part of the page saying "not read", never the whole page. Read-only; no new SQL object.
/// </summary>
public sealed class SqlServerMorningSummaryQuery : IMorningSummaryQuery
{
    private readonly Func<IEtpConnectionFactory> connections;
    private readonly Func<INoSaleDayService>? noSaleDays;
    private readonly ShopResultFolder shopResults;
    private readonly IOffPcCopyEnvironment offPc;
    private readonly Func<DateTimeOffset, double?>? diskCopyAgeHours;
    private readonly TimeProvider clock;
    private readonly Action<Exception, string>? report;
    private readonly Func<Exception, string>? describe;

    /// <param name="connections">The window's current connection (read on every load).</param>
    /// <param name="noSaleDays">N1's service; null leaves the no-sale marks out.</param>
    /// <param name="diskCopyAgeHours">G1's disk copy age (decision 54); null until that copy is set up.</param>
    /// <param name="report">Records a source that failed (diagnostics event id); never throws.</param>
    /// <param name="describe">The plain sentence for a store whose day could not be read.</param>
    public SqlServerMorningSummaryQuery(
        Func<IEtpConnectionFactory> connections,
        Func<INoSaleDayService>? noSaleDays = null,
        ShopResultFolder? shopResults = null,
        IOffPcCopyEnvironment? offPc = null,
        Func<DateTimeOffset, double?>? diskCopyAgeHours = null,
        TimeProvider? clock = null,
        Action<Exception, string>? report = null,
        Func<Exception, string>? describe = null)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.noSaleDays = noSaleDays;
        this.shopResults = shopResults ?? new ShopResultFolder();
        this.offPc = offPc ?? new WindowsOffPcCopyEnvironment();
        this.diskCopyAgeHours = diskCopyAgeHours;
        this.clock = clock ?? TimeProvider.System;
        this.report = report;
        this.describe = describe;
    }

    public async Task<MorningSummary> LoadAsync(DateOnly lastBusinessDay, CancellationToken cancellationToken = default)
    {
        var connection = connections().LegacyConnectionString();
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Stores: a failure here is the page's failure (nothing to show without them).
        var stores = (await new StoreCatalogRepository(connection).LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(store => store.IsActive && !store.IsServiceCentre)
            .OrderBy(store => store.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var readiness = await Try(() => new SqlServerDailyReadinessQuery(connection)
            .LoadAsync(new DailyReadinessRequest(lastBusinessDay, [], IncludeServiceCentre: false), cancellationToken), "MORNING_READINESS_FAILED").ConfigureAwait(false);
        var history = await Try(() => new SqlServerImportHistoryQuery(connection)
            .LoadAsync(new ImportHistoryScope(lastBusinessDay, lastBusinessDay), cancellationToken), "MORNING_HISTORY_FAILED").ConfigureAwait(false);
        var marks = noSaleDays is null ? null
            : await Try(() => noSaleDays().LoadAsync(lastBusinessDay, cancellationToken), "MORNING_NO_SALE_FAILED").ConfigureAwait(false);

        var workflow = new SqlServerDailyWorkflowService(connection);
        var sources = new List<MorningStoreSource>();
        foreach (var store in stores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DailyWorkflowState? state = null;
            string? failure = null;
            try { state = await workflow.LoadAsync(new DailyWorkflowScope(store.Code, lastBusinessDay), cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                report?.Invoke(exception, "MORNING_STORE_DAY_FAILED");
                failure = describe?.Invoke(exception) ?? "The day could not be read. Refresh to try again.";
            }
            sources.Add(new MorningStoreSource(
                store.Code,
                store.Name,
                state,
                readiness?.Missing.Where(item => Same(item.StoreCode, store.Code)).ToArray() ?? [],
                history?.Where(entry => Same(entry.Result.StoreCode, store.Code)).ToArray() ?? [],
                marks?.FirstOrDefault(mark => Same(mark.StoreCode, store.Code) && mark.BusinessDate == lastBusinessDay),
                failure));
        }

        var (nightly, finding, nightlyUnreadable) = await NightlyAsync(connection, cancellationToken).ConfigureAwait(false);
        IServiceReportQuery service = new SqlServerServiceReportQuery(connection);
        var freshness = await Try(() => service.LoadFreshnessAsync(lastBusinessDay, cancellationToken), "MORNING_SERVICE_FAILED").ConfigureAwait(false);

        IReadOnlyList<DailyReadinessItem>? monthStart = null;
        if (today.Day <= MorningSummaryBuilder.MonthStartLastDay)
        {
            var current = await Try(() => new SqlServerDailyReadinessQuery(connection)
                .LoadAsync(new DailyReadinessRequest(today, [], IncludeServiceCentre: false), cancellationToken), "MORNING_MONTH_START_FAILED").ConfigureAwait(false);
            monthStart = current?.Missing.Where(item => item.Kind is DailyReadinessItemKind.MissingMonthlyTarget or DailyReadinessItemKind.MissingStaffTargets).ToArray();
        }

        var (offPcConfigured, offPcAge) = OffPcBackupAge.Read(offPc, now);
        double? diskAge = null;
        try { diskAge = diskCopyAgeHours?.Invoke(now); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { report?.Invoke(exception, "MORNING_DISK_COPY_FAILED"); }

        var input = new MorningSources(lastBusinessDay, sources)
        {
            Nightly = nightly,
            NightlyTopFinding = finding,
            NightlyUnreadable = nightlyUnreadable,
            ServiceFreshness = freshness,
            AllImports = history ?? [],
            ImportsUnreadable = history is null,
            ShopResults = shopResults.Read(lastBusinessDay),
            MonthStartMissing = monthStart,
            MonthStart = new DateOnly(today.Year, today.Month, 1),
            OffPcBackupConfigured = offPcConfigured,
            OffPcBackupAgeHours = offPcAge,
            DiskCopyAgeHours = diskAge
        };
        return MorningSummaryBuilder.Build(input, today, now);
    }

    private async Task<(HealthCheckNightlySummary? Summary, string? Finding, bool Unreadable)> NightlyAsync(string connection, CancellationToken cancellationToken)
    {
        try
        {
            var store = new SqlServerHealthCheckStore(connection);
            var summary = await store.LoadLatestNightlySummaryAsync(cancellationToken).ConfigureAwait(false);
            string? finding = null;
            if (summary is not null && (summary.Counts.Fail > 0 || summary.Counts.Warning > 0 || summary.Overall != HealthOutcome.Pass))
            {
                try
                {
                    var runs = await store.LoadRunsAsync(1, HealthCheckTrigger.Nightly, cancellationToken).ConfigureAwait(false);
                    if (runs.Count > 0) finding = TopFinding(await store.LoadResultsAsync(runs[0].HealthCheckRunId, cancellationToken).ConfigureAwait(false));
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { report?.Invoke(exception, "MORNING_HEALTH_FINDING_FAILED"); }
            }
            return (summary, finding, false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report?.Invoke(exception, "MORNING_HEALTH_FAILED");
            return (null, null, true);
        }
    }

    /// <summary>The first Fail, else the first Warning, explanation of a run, cut to one line (as the System check tile).</summary>
    internal static string? TopFinding(IReadOnlyList<HealthCheckResult> results)
    {
        var top = results.FirstOrDefault(x => x.Outcome == HealthOutcome.Fail) ?? results.FirstOrDefault(x => x.Outcome == HealthOutcome.Warning);
        if (top is null || string.IsNullOrWhiteSpace(top.Explanation)) return null;
        var text = top.Explanation.Trim().ReplaceLineEndings(" ");
        return text.Length <= 140 ? text : text[..137].TrimEnd() + "...";
    }

    private async Task<T?> Try<T>(Func<Task<T>> load, string eventId) where T : class
    {
        try { return await load().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report?.Invoke(exception, eventId);
            return null;
        }
    }

    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
