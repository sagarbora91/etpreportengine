using System.Diagnostics;
using System.Globalization;
using Etp.Reporting.Application.DailyReadiness;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Application.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>Latest stock snapshot of one store (null when the store has none).</summary>
internal sealed record StockSnapshotFact(string StoreCode, DateOnly? LatestSnapshotDate);

/// <summary>An open data-quality issue group (workflow status OPEN or ACKNOWLEDGED).</summary>
internal sealed record DataQualityFact(string Category, string Severity, int Count);

/// <summary>
/// What DATA-04 reads: the Service refresh log (<c>dbo.v_service_readings</c>, 1.10.0), whether that view exists, whether a
/// Service-unit store is active, and, before the view exists, the newest Service import file date.
/// </summary>
internal sealed record ServiceFreshnessFacts(
    bool ReadingsViewExists,
    bool ServiceStoreActive,
    IReadOnlyList<ServiceRefresh> Refreshes,
    IReadOnlyDictionary<string, string> LatestSourceKinds,
    DateOnly? NewestServiceFileDate);

/// <summary>
/// Health checks DATA-01 to DATA-05 (design 5.2), business date D-1 (<see cref="HealthCheckContext.BusinessDate"/>).
/// <para>Each data gap is reported once, by the check that owns it (design 5.1): DATA-01 owns missing exports, inputs and
/// targets; DATA-02 and DATA-03 add their own measurements and DATA-02 says Skipped for a store whose R022 or R025 is
/// missing. DATA-01 publishes its missing items in <see cref="HealthCheckContext.Items"/> under <see cref="MissingItemsKey"/>.</para>
/// <para>DATA-02 computes its own tender reconciliation (<see cref="IControlledReportQuery.RunTenderReconciliationAsync"/>);
/// it does not depend on RPT-16 having run first (run order in 5.3 puts Data before Reports; step 0 note, watch-point 2).</para>
/// <para>Read-only: DATA-01 and DATA-02 use existing read-only query classes ("Q"); DATA-03..05 use guarded inline SELECTs ("G").</para>
/// </summary>
public sealed class DataProbes : IHealthProbe
{
    /// <summary><see cref="HealthCheckContext.Items"/> key: a <c>HashSet&lt;string&gt;</c> of <c>"CODE|STORE"</c> for each missing export.</summary>
    public const string MissingItemsKey = "DATA-01.missing";

    // TaskNavigation ids.
    internal const string FixImportFiles = "import-files", FixWalkIns = "walk-ins", FixCashInput = "cash-input",
        FixMasters = "masters", FixStaffTarget = "staff-target", FixDataQuality = "data-quality";

    public const int SnapshotVeryOldDays = 30;

    internal const string SnapshotsSql = """
        SET NOCOUNT ON;
        SELECT store_code,MAX(snapshot_date) latest_snapshot
        FROM dbo.stock_snapshots
        GROUP BY store_code;
        """;

    internal const string ServiceFactsSql = """
        SET NOCOUNT ON;
        SELECT CASE WHEN OBJECT_ID(N'dbo.v_service_readings',N'V') IS NULL THEN 0 ELSE 1 END readings_view,
               CASE WHEN EXISTS(SELECT 1 FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
                                WHERE u.business_unit_code='SERVICE' AND s.is_active=1) THEN 1 ELSE 0 END service_active;
        """;

    // Same columns as the 1.10.0 freshness strip (SqlServerServiceReportQuery.FreshnessSql).
    internal const string ServiceReadingsSql = """
        SET NOCOUNT ON;
        SELECT report_code,snapshot_date,row_count,import_file_id,imported_utc,source_kind,is_latest
        FROM dbo.v_service_readings;
        """;

    // Before 1.10.0: the newest non-superseded file of a Service-unit store.
    internal const string ServiceFilesSql = """
        SET NOCOUNT ON;
        SELECT MAX(COALESCE(f.period_end,f.business_date))
        FROM dbo.import_files f
        WHERE f.is_superseded=0
          AND f.store_code IN(SELECT s.store_code FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
                              WHERE u.business_unit_code='SERVICE');
        """;

    internal const string DataQualitySql = """
        SET NOCOUNT ON;
        SELECT category,severity,COUNT_BIG(*) issues
        FROM dbo.data_quality_issues
        WHERE workflow_status IN('OPEN','ACKNOWLEDGED')
        GROUP BY category,severity
        ORDER BY category,severity;
        """;

    private readonly Func<DateOnly, CancellationToken, Task<DailyReadinessResult>> loadReadiness;
    private readonly Func<ReportScope, CancellationToken, Task<TenderReconciliationReport>> runTender;
    private readonly Func<CancellationToken, Task<IReadOnlyList<StockSnapshotFact>>> loadSnapshots;
    private readonly Func<CancellationToken, Task<ServiceFreshnessFacts>> loadService;
    private readonly Func<CancellationToken, Task<IReadOnlyList<DataQualityFact>>> loadQuality;

    public DataProbes(string connectionString)
        : this(new ValidatedConnection(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString))))
    {
    }

    private readonly record struct ValidatedConnection(string Value);

    private DataProbes(ValidatedConnection connection)
        : this(
            ReadinessFrom(connection.Value),
            new SqlServerApplicationReportQuery(connection.Value).RunTenderReconciliationAsync,
            SnapshotsFrom(connection.Value),
            ServiceFrom(connection.Value),
            QualityFrom(connection.Value))
    {
    }

    private static Func<DateOnly, CancellationToken, Task<DailyReadinessResult>> ReadinessFrom(string connectionString)
    {
        var query = new SqlServerDailyReadinessQuery(connectionString);
        return (date, cancellationToken) => query.LoadAsync(new DailyReadinessRequest(date, [], true), cancellationToken);
    }

    internal DataProbes(
        Func<DateOnly, CancellationToken, Task<DailyReadinessResult>> loadReadiness,
        Func<ReportScope, CancellationToken, Task<TenderReconciliationReport>> runTender,
        Func<CancellationToken, Task<IReadOnlyList<StockSnapshotFact>>> loadSnapshots,
        Func<CancellationToken, Task<ServiceFreshnessFacts>> loadService,
        Func<CancellationToken, Task<IReadOnlyList<DataQualityFact>>> loadQuality)
    {
        this.loadReadiness = loadReadiness;
        this.runTender = runTender;
        this.loadSnapshots = loadSnapshots;
        this.loadService = loadService;
        this.loadQuality = loadQuality;
    }

    public IReadOnlyList<string> CheckIds { get; } =
        [HealthCheckId.Data01, HealthCheckId.Data02, HealthCheckId.Data03, HealthCheckId.Data04, HealthCheckId.Data05];

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Data01, () => RunCompletenessAsync(context, cancellationToken), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Data02, () => RunTenderAsync(context, cancellationToken), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Data03, () => RunSnapshotsAsync(context, cancellationToken), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Data04, () => RunServiceAsync(context, cancellationToken), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Data05, () => RunQualityAsync(cancellationToken), cancellationToken)
        ];
    }

    // ---- DATA-01 ---------------------------------------------------------------------------------------------------

    /// <summary>The sub-finding codes of DATA-01, one per kind of gap (each can be made Known separately, decision 33).</summary>
    public const string ExportsMissing = "EXPORTS_MISSING", ClosingStockMissing = "CLOSING_STOCK_MISSING",
        InputsMissing = "INPUTS_MISSING", MonthlyTargetMissing = "MONTHLY_TARGET_MISSING", StaffTargetsMissing = "STAFF_TARGETS_MISSING";

    private async Task<HealthCheckResult> RunCompletenessAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var readiness = await loadReadiness(context.BusinessDate, cancellationToken);
        timer.Stop();
        context.Items[MissingItemsKey] = MissingExports(readiness);
        return EvaluateCompleteness(context.BusinessDate, readiness, timer.Elapsed);
    }

    /// <summary>The missing exports as <c>"CODE|STORE"</c>, for DATA-02 (and any later check) to say "see DATA-01".</summary>
    internal static HashSet<string> MissingExports(DailyReadinessResult readiness) =>
        readiness.Missing.Where(item => item.Kind == DailyReadinessItemKind.MissingExport)
            .Select(item => item.Code + "|" + item.StoreCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? SubCodeOf(DailyReadinessItem item) => item.Kind switch
    {
        DailyReadinessItemKind.MissingExport => item.Code.Equals("R011", StringComparison.OrdinalIgnoreCase) ? ClosingStockMissing : ExportsMissing,
        DailyReadinessItemKind.MissingManualInput => InputsMissing,
        DailyReadinessItemKind.MissingMonthlyTarget => MonthlyTargetMissing,
        DailyReadinessItemKind.MissingStaffTargets => StaffTargetsMissing,
        _ => null
    };

    private static string FixFor(DailyReadinessDestination destination) => destination switch
    {
        DailyReadinessDestination.ImportIntake => FixImportFiles,
        DailyReadinessDestination.TodayWalkIns => FixWalkIns,
        DailyReadinessDestination.TodayCashEntries => FixCashInput,
        DailyReadinessDestination.SettingsMonthlyTargets => FixMasters,
        DailyReadinessDestination.SettingsStaffTargets => FixStaffTarget,
        _ => FixImportFiles
    };

    internal static HealthCheckResult EvaluateCompleteness(DateOnly businessDate, DailyReadinessResult readiness, TimeSpan duration)
    {
        // 1.12 N1 (decision 52): a store-day marked as a no-sale day asks for no exports or entries; the OK line counts them.
        var noSale = readiness.NoSaleStoreCodes?.Count ?? 0;
        if (readiness.IsComplete)
            return ImportDataProbeSupport.Make(HealthCheckId.Data01, HealthOutcome.Pass, "OK",
                ImportDataProbeSupport.Text($"Everything expected for {ImportDataProbeSupport.Day(businessDate)} is in: exports, manual inputs and targets.{NoSaleNote(noSale)}{NotYetSendingNote(readiness.NotYetSendingStoreCodes)}"),
                null, duration, 0);

        // Grouped by kind, in the order of the Today checklist; one sub-result per kind with codes and store codes only.
        var order = new[] { ExportsMissing, ClosingStockMissing, InputsMissing, MonthlyTargetMissing, StaffTargetsMissing };
        var groups = readiness.Missing
            .Select(item => (Sub: SubCodeOf(item), Item: item)).Where(entry => entry.Sub is not null)
            .GroupBy(entry => entry.Sub!, StringComparer.Ordinal)
            .OrderBy(group => Array.IndexOf(order, group.Key)).ToArray();
        var subs = groups.Select(group => new HealthCheckSubResult(group.Key, HealthOutcome.Warning, group.Key, group.Count(),
            new Dictionary<string, string>
            {
                ["codes"] = string.Join(",", group.Select(e => e.Item.Code).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)),
                ["stores"] = string.Join(",", group.Select(e => e.Item.StoreCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            })).ToArray();

        var parts = subs.Select(sub => $"{Describe(sub.FindingCode!)} {sub.MetricValue:0} (stores {sub.Fields!["stores"]}; {sub.Fields["codes"]})");
        var summary = string.Join("; ", parts);
        var fix = FixFor(groups[0].First().Item.Destination);
        return ImportDataProbeSupport.Make(HealthCheckId.Data01, HealthOutcome.Warning, "DAY_INCOMPLETE",
            ImportDataProbeSupport.Text($"{ImportDataProbeSupport.Day(businessDate)} is not complete: {summary}."),
            fix, duration, readiness.Missing.Count, subs);
    }

    /// <summary>1.12.1 P7 (decision 51): " (not sending yet: STORE2, SVC1)" when the shop-live file waives stores; empty otherwise.</summary>
    internal static string NotYetSendingNote(IReadOnlyList<string>? stores) =>
        stores is { Count: > 0 } ? $" (not sending yet: {string.Join(", ", stores)})" : "";

    /// <summary>" (2 store-days marked no-sale)" when any store-day of the date is marked; empty otherwise.</summary>
    internal static string NoSaleNote(int count) => count switch
    {
        <= 0 => "",
        1 => " (1 store-day marked no-sale)",
        _ => $" ({count} store-days marked no-sale)"
    };

    private static string Describe(string subCode) => subCode switch
    {
        ExportsMissing => "exports missing",
        ClosingStockMissing => "closing stock missing",
        InputsMissing => "manual inputs missing",
        MonthlyTargetMissing => "monthly targets missing",
        StaffTargetsMissing => "staff targets missing",
        _ => subCode
    };

    // ---- DATA-02 ---------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunTenderAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var missing = context.Items.TryGetValue(MissingItemsKey, out var value) && value is HashSet<string> set ? set : null;
        var outcomes = new List<TenderStoreOutcome>();
        foreach (var store in context.RetailStoreCodes)
        {
            if (missing is not null && (missing.Contains("R022|" + store) || missing.Contains("R025|" + store)))
            {
                outcomes.Add(new TenderStoreOutcome(store, null, true));
                continue;
            }
            var report = await runTender(new ReportScope(context.BusinessDate, context.BusinessDate, [store]), cancellationToken);
            outcomes.Add(new TenderStoreOutcome(store, report, false));
        }
        timer.Stop();
        return EvaluateTender(context.BusinessDate, outcomes, timer.Elapsed);
    }

    /// <summary>One store's tender result: the report, or <see cref="SkippedForDataGap"/> when DATA-01 already reported the missing export.</summary>
    internal sealed record TenderStoreOutcome(string StoreCode, TenderReconciliationReport? Report, bool SkippedForDataGap);

    internal static HealthCheckResult EvaluateTender(DateOnly businessDate, IReadOnlyList<TenderStoreOutcome> outcomes, TimeSpan duration)
    {
        if (outcomes.Count == 0)
            return ImportDataProbeSupport.Skipped(HealthCheckId.Data02, "NO_RETAIL_STORE", "There is no active Retail store to reconcile.", duration);

        var subs = new List<HealthCheckSubResult>();
        foreach (var outcome in outcomes)
        {
            if (outcome.SkippedForDataGap || outcome.Report is null)
            {
                subs.Add(new HealthCheckSubResult(outcome.StoreCode, HealthOutcome.Skipped, "SEE_DATA_01"));
                continue;
            }
            var report = outcome.Report;
            var failed = report.Documents.Where(document => document.Status == ReportStatus.Failed).ToArray();
            if (report.Status == ReportStatus.Failed || failed.Length > 0)
            {
                var variance = failed.Sum(document => Math.Abs(document.Variance ?? 0m));
                subs.Add(new HealthCheckSubResult(outcome.StoreCode, HealthOutcome.Fail, "TENDER_VARIANCE", failed.Length,
                    new Dictionary<string, string>
                    {
                        ["documents"] = failed.Length.ToString(CultureInfo.InvariantCulture),
                        ["variance"] = variance.ToString("0.00", CultureInfo.InvariantCulture)
                    }));
            }
            else if (report.Status == ReportStatus.Blocked)
            {
                // Tender missing for an invoice means R022 is not imported: that is DATA-01's line, not a second warning.
                var tenderMissing = report.Documents.Any(document => document.TenderAmount is null);
                subs.Add(tenderMissing
                    ? new HealthCheckSubResult(outcome.StoreCode, HealthOutcome.Skipped, "SEE_DATA_01")
                    : new HealthCheckSubResult(outcome.StoreCode, HealthOutcome.Warning, "TENDER_BLOCKED"));
            }
            else
            {
                subs.Add(new HealthCheckSubResult(outcome.StoreCode, HealthOutcome.Pass, "OK", report.Documents.Count));
            }
        }

        var day = ImportDataProbeSupport.Day(businessDate);
        var fails = subs.Where(sub => sub.Outcome == HealthOutcome.Fail).ToArray();
        if (fails.Length > 0)
        {
            var documents = fails.Sum(sub => sub.MetricValue ?? 0);
            var variance = fails.Sum(sub => decimal.Parse(sub.Fields!["variance"], CultureInfo.InvariantCulture));
            var perStore = string.Join("; ", fails.Select(sub => $"{sub.Key} {sub.Fields!["documents"]} documents, {sub.Fields["variance"]}"));
            return ImportDataProbeSupport.Make(HealthCheckId.Data02, HealthOutcome.Fail, "TENDER_VARIANCE",
                ImportDataProbeSupport.Text($"Invoices and tenders of {day} disagree beyond tolerance: {documents:0} documents, total variance {variance:0.00} ({perStore}). Open Reports > Tender reconciliation."),
                "reports-list", duration, documents, subs);
        }
        var blocked = subs.Where(sub => sub.Outcome == HealthOutcome.Warning).ToArray();
        if (blocked.Length > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Data02, HealthOutcome.Warning, "TENDER_BLOCKED",
                ImportDataProbeSupport.Text($"The tender reconciliation of {day} is blocked for {string.Join(", ", blocked.Select(sub => sub.Key))} for a reason other than a missing export."),
                "reports-list", duration, blocked.Length, subs);
        if (subs.All(sub => sub.Outcome == HealthOutcome.Skipped))
            return ImportDataProbeSupport.Make(HealthCheckId.Data02, HealthOutcome.Skipped, "SEE_DATA_01",
                ImportDataProbeSupport.Text($"Skipped: the sales or tender export of {day} is missing, see DATA-01."), null, duration, null, subs);
        var skipped = subs.Where(sub => sub.Outcome == HealthOutcome.Skipped).Select(sub => sub.Key).ToArray();
        var note = skipped.Length == 0 ? "" : $" Not checked for {string.Join(", ", skipped)}, see DATA-01.";
        return ImportDataProbeSupport.Make(HealthCheckId.Data02, HealthOutcome.Pass, "OK",
            ImportDataProbeSupport.Text($"Invoices and tenders of {day} agree.{note}"), null, duration, 0, subs);
    }

    // ---- DATA-03 ---------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunSnapshotsAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var facts = await loadSnapshots(cancellationToken);
        timer.Stop();
        return EvaluateSnapshots(context.BusinessDate, context.RetailStoreCodes, facts, timer.Elapsed);
    }

    internal static HealthCheckResult EvaluateSnapshots(
        DateOnly businessDate, IReadOnlyList<string> retailStores, IReadOnlyList<StockSnapshotFact> facts, TimeSpan duration)
    {
        if (retailStores.Count == 0)
            return ImportDataProbeSupport.Skipped(HealthCheckId.Data03, "NO_RETAIL_STORE", "There is no active Retail store to check.", duration);

        var latest = facts.GroupBy(fact => fact.StoreCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(fact => fact.LatestSnapshotDate), StringComparer.OrdinalIgnoreCase);
        var subs = new List<HealthCheckSubResult>();
        foreach (var store in retailStores)
        {
            latest.TryGetValue(store, out var date);
            if (date is not { } snapshot)
            {
                subs.Add(new HealthCheckSubResult(store, HealthOutcome.Fail, "SNAPSHOT_MISSING"));
                continue;
            }
            var age = businessDate.DayNumber - snapshot.DayNumber;
            var fields = new Dictionary<string, string>
            {
                ["latest"] = snapshot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["daysBehind"] = Math.Max(age, 0).ToString(CultureInfo.InvariantCulture)
            };
            subs.Add(age <= 0 ? new HealthCheckSubResult(store, HealthOutcome.Pass, "OK", 0, fields)
                : age > SnapshotVeryOldDays ? new HealthCheckSubResult(store, HealthOutcome.Fail, "SNAPSHOT_VERY_OLD", age, fields)
                : new HealthCheckSubResult(store, HealthOutcome.Warning, "SNAPSHOT_STALE", age, fields));
        }

        var day = ImportDataProbeSupport.Day(businessDate);
        string Stores(HealthOutcome outcome) => string.Join(", ", subs.Where(sub => sub.Outcome == outcome).Select(sub =>
            sub.Fields is null ? sub.Key : $"{sub.Key} {ImportDataProbeSupport.Day(DateOnly.ParseExact(sub.Fields["latest"], "yyyy-MM-dd", CultureInfo.InvariantCulture))}"));
        var worst = subs.Where(sub => sub.Outcome == HealthOutcome.Fail).OrderByDescending(sub => sub.MetricValue ?? decimal.MaxValue).FirstOrDefault();
        if (worst is not null)
        {
            var missing = subs.Where(sub => sub.FindingCode == "SNAPSHOT_MISSING").Select(sub => sub.Key).ToArray();
            var code = missing.Length > 0 ? "SNAPSHOT_MISSING" : "SNAPSHOT_VERY_OLD";
            var text = missing.Length > 0
                ? $"No closing-stock snapshot for {string.Join(", ", missing)}; the check date is {day}."
                : $"The closing-stock snapshot is more than {SnapshotVeryOldDays} days behind {day} for {Stores(HealthOutcome.Fail)}.";
            return ImportDataProbeSupport.Make(HealthCheckId.Data03, HealthOutcome.Fail, code,
                ImportDataProbeSupport.Text($"{text} Import the Closing Stock (R011) export."), FixImportFiles, duration, worst.MetricValue, subs);
        }
        var stale = subs.Where(sub => sub.Outcome == HealthOutcome.Warning).ToArray();
        if (stale.Length > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Data03, HealthOutcome.Warning, "SNAPSHOT_STALE",
                ImportDataProbeSupport.Text($"The closing-stock snapshot is behind {day}: latest {Stores(HealthOutcome.Warning)}. Import the Closing Stock (R011) export."),
                FixImportFiles, duration, stale.Max(sub => sub.MetricValue), subs);
        return ImportDataProbeSupport.Make(HealthCheckId.Data03, HealthOutcome.Pass, "OK",
            ImportDataProbeSupport.Text($"Every Retail store has a closing-stock snapshot for {day} or later."), null, duration, 0);
    }

    // ---- DATA-04 ---------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunServiceAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var facts = await loadService(cancellationToken);
        timer.Stop();
        // The freshness strip counts from today; D-1 is yesterday, so today is the day after the business date.
        return EvaluateService(context.BusinessDate.AddDays(1), facts, timer.Elapsed);
    }

    internal static HealthCheckResult EvaluateService(DateOnly today, ServiceFreshnessFacts facts, TimeSpan duration)
    {
        var subs = new List<HealthCheckSubResult>();
        if (facts.ReadingsViewExists)
        {
            if (facts.Refreshes.Count == 0)
                return ImportDataProbeSupport.Make(HealthCheckId.Data04, HealthOutcome.Pass, "NO_SERVICE_DATA",
                    "No Service Centre export has been imported yet; there is nothing to age.", null, duration, 0);
            foreach (var chip in ServiceFreshness.Build(facts.Refreshes, today, facts.LatestSourceKinds))
            {
                if (chip.Colour is ServiceFreshnessColour.Fresh or ServiceFreshnessColour.NoData) continue;
                var fields = new Dictionary<string, string>
                {
                    ["latest"] = chip.LatestSnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "none",
                    ["colour"] = chip.Colour.ToString()
                };
                var days = chip.LatestSnapshotDate is { } date ? today.DayNumber - date.DayNumber : (int?)null;
                subs.Add(new HealthCheckSubResult(chip.Group,
                    chip.Colour == ServiceFreshnessColour.Red ? HealthOutcome.Fail : HealthOutcome.Warning,
                    chip.Colour == ServiceFreshnessColour.Red ? "SERVICE_STALE" : "SERVICE_AGEING", days, fields));
            }
        }
        else if (facts.NewestServiceFileDate is { } newest)
        {
            // Before 1.10.0 there is no reading log: the newest Service file date stands for all families.
            var colour = ServiceFreshness.Colour(newest, today);
            if (colour is ServiceFreshnessColour.Amber or ServiceFreshnessColour.Red)
                subs.Add(new HealthCheckSubResult("service-files",
                    colour == ServiceFreshnessColour.Red ? HealthOutcome.Fail : HealthOutcome.Warning,
                    colour == ServiceFreshnessColour.Red ? "SERVICE_STALE" : "SERVICE_AGEING", today.DayNumber - newest.DayNumber,
                    new Dictionary<string, string> { ["latest"] = newest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["colour"] = colour.ToString() }));
        }
        else
        {
            return ImportDataProbeSupport.Make(HealthCheckId.Data04, HealthOutcome.Pass, "NO_SERVICE_DATA",
                "No Service Centre export has been imported yet; there is nothing to age.", null, duration, 0);
        }

        if (subs.Count == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Data04, HealthOutcome.Pass, "OK",
                "Every Service Centre family group was exported within the last 7 days (monthly families within 38 days).", null, duration, 0);

        // Red is a Fail while the Service store is active, and a Warning otherwise (the Service unit is inactive by default).
        var red = subs.Where(sub => sub.Outcome == HealthOutcome.Fail).ToArray();
        var downgraded = red.Length > 0 && !facts.ServiceStoreActive;
        if (downgraded)
            for (var index = 0; index < subs.Count; index++)
                if (subs[index].Outcome == HealthOutcome.Fail) subs[index] = subs[index] with { Outcome = HealthOutcome.Warning };

        string Groups(string code) => string.Join(", ", subs.Where(sub => sub.FindingCode == code).Select(sub => $"{sub.Key} ({sub.MetricValue:0} days)"));
        if (red.Length > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Data04, downgraded ? HealthOutcome.Warning : HealthOutcome.Fail, "SERVICE_STALE",
                ImportDataProbeSupport.Text($"Service Centre data is more than 14 days old for {Groups("SERVICE_STALE")}. Import the Service exports."),
                FixImportFiles, duration, red.Max(sub => sub.MetricValue), subs);
        return ImportDataProbeSupport.Make(HealthCheckId.Data04, HealthOutcome.Warning, "SERVICE_AGEING",
            ImportDataProbeSupport.Text($"Service Centre data is more than 7 days old for {Groups("SERVICE_AGEING")}. Import the Service exports."),
            FixImportFiles, duration, subs.Max(sub => sub.MetricValue), subs);
    }

    // ---- DATA-05 ---------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunQualityAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var facts = await loadQuality(cancellationToken);
        timer.Stop();
        return EvaluateQuality(facts, timer.Elapsed);
    }

    internal static HealthCheckResult EvaluateQuality(IReadOnlyList<DataQualityFact> facts, TimeSpan duration)
    {
        var critical = facts.Where(fact => fact.Severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (critical.Length == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Data05, HealthOutcome.Pass, "OK",
                "No critical data-quality issue is open.", null, duration, 0);
        var total = critical.Sum(fact => fact.Count);
        var subs = critical.GroupBy(fact => fact.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HealthCheckSubResult(group.Key, HealthOutcome.Warning, "DQ_CRITICAL_OPEN", group.Sum(fact => fact.Count)))
            .OrderBy(sub => sub.Key, StringComparer.Ordinal).ToArray();
        var list = string.Join(", ", subs.Select(sub => $"{sub.Key} {sub.MetricValue:0}"));
        return ImportDataProbeSupport.Make(HealthCheckId.Data05, HealthOutcome.Warning, "DQ_CRITICAL_OPEN",
            ImportDataProbeSupport.Text($"{total} critical data-quality issues are open ({list}). Open Settings > Control centre > Data quality."),
            FixDataQuality, duration, total, subs);
    }

    // ---- SQL readers (guarded SELECTs) -----------------------------------------------------------------------------

    private static Func<CancellationToken, Task<IReadOnlyList<StockSnapshotFact>>> SnapshotsFrom(string connectionString) =>
        async cancellationToken =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, SnapshotsSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<StockSnapshotFact>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new StockSnapshotFact(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1)));
            return rows;
        };

    private static Func<CancellationToken, Task<IReadOnlyList<DataQualityFact>>> QualityFrom(string connectionString) =>
        async cancellationToken =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, DataQualitySql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<DataQualityFact>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new DataQualityFact(reader.GetString(0), reader.GetString(1), (int)Math.Min(int.MaxValue, reader.GetInt64(2))));
            return rows;
        };

    private static Func<CancellationToken, Task<ServiceFreshnessFacts>> ServiceFrom(string connectionString) =>
        async cancellationToken =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            bool viewExists, active;
            await using (var first = ImportDataProbeSupport.ReadOnlyCommand(connection, ServiceFactsSql))
            await using (var reader = await first.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                viewExists = reader.GetInt32(0) == 1;
                active = reader.GetInt32(1) == 1;
            }
            var refreshes = new List<ServiceRefresh>();
            var kinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DateOnly? newest = null;
            if (viewExists)
            {
                await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, ServiceReadingsSql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    refreshes.Add(new ServiceRefresh(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
                        reader.GetInt64(3), DateTime.SpecifyKind(reader.GetDateTime(4), DateTimeKind.Utc)));
                    if (reader.GetBoolean(6)) kinds[reader.GetString(0)] = reader.GetString(5);
                }
            }
            else
            {
                await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, ServiceFilesSql);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is not null and not DBNull) newest = DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture));
            }
            return new ServiceFreshnessFacts(viewExists, active, refreshes, kinds, newest);
        };
}
