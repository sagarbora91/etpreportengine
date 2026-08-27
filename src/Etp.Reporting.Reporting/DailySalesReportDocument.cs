using System.Globalization;

namespace Etp.Reporting.Reporting;

public sealed record DsrPeriodFact(string Period, string StoreCode, decimal? TySales, decimal? LySales,
    decimal? TyUnits, decimal? LyUnits, int? TyInvoices, int? LyInvoices, decimal? Upt, decimal? Atv,
    decimal? WalkIns, decimal? ConversionPercent);

public sealed record DsrServiceFact(string Period, string StoreCode, decimal? Cash, decimal? Card,
    decimal? Upi, decimal? Total, decimal? LastYearTotal);

public sealed record DsrPeriodComparison(string Period, decimal? TyValue, decimal? TyQuantity,
    decimal? LyValue, decimal? LyQuantity, CalculatedMetric ValueGrowth, CalculatedMetric QuantityGrowth,
    string? MissingSourceNote = null);

public sealed record DsrOperationalMetric(string Name, decimal? Ftd, decimal? LastYear,
    CalculatedMetric Change, string Context, bool IsCurrency);

public sealed record DsrStoreCard(string StoreCode, string DisplayName, string Accent,
    IReadOnlyList<DsrPeriodComparison> Periods, IReadOnlyList<DsrOperationalMetric> OperationalMetrics,
    decimal? FtdWalkIns);

public sealed record DsrServiceSummary(decimal? Wdc, decimal? Cash, decimal? Card, decimal? Upi,
    decimal? Total, IReadOnlyDictionary<string, decimal?> PeriodTotals);

public sealed record DsrTargetProgress(string StoreCode, string DisplayName, string Accent,
    decimal? MtdActual, decimal? MonthlyTarget, CalculatedMetric Achievement)
{
    public decimal FillPercent => Math.Clamp(Achievement.Value ?? 0m, 0m, 100m);
}

public sealed record DailySalesReportDocument(DateOnly BusinessDate, string Title, string Subtitle,
    decimal? CombinedFtd, CalculatedMetric CombinedFtdGrowth, decimal? Units, decimal? WalkIns,
    int? CombinedInvoices, CalculatedMetric Conversion, decimal? MtdSales, CalculatedMetric MtdTargetAchievement,
    decimal? YtdSales, CalculatedMetric YtdGrowth, IReadOnlyList<DsrStoreCard> Stores,
    DsrServiceSummary Service, IReadOnlyList<DsrTargetProgress> Targets, string MetricPolicy)
{
    public string Weekday(CultureInfo? culture = null) => BusinessDate.ToString("dddd", culture ?? CultureInfo.GetCultureInfo("en-IN"));
}

public static class DailySalesReportBuilder
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Accent)> StorePresentation =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["WLMHW"] = ("Titan World", "#2269E8"),
            ["HEMW"] = ("Helios", "#7137D4")
        };

    public static DailySalesReportDocument Build(DateOnly businessDate, IReadOnlyList<DsrPeriodFact> sales,
        IReadOnlyList<DsrServiceFact> service, IReadOnlyDictionary<string, decimal?> monthlyTargets,
        decimal? serviceWdc = null, string metricPolicy = "DSR_INVOICE_DENOMINATOR_SOURCE_EVIDENCE_V1")
    {
        ArgumentNullException.ThrowIfNull(sales); ArgumentNullException.ThrowIfNull(service); ArgumentNullException.ThrowIfNull(monthlyTargets);
        var engine = new ManagementMetricEngine();
        var storeCards = StorePresentation.Select(store => BuildStore(store.Key, store.Value, sales, engine)).ToArray();
        var combinedFtd = Find(sales, "COMBINED", "FTD");
        var combinedMtd = Find(sales, "COMBINED", "MTD");
        var combinedYtd = Find(sales, "COMBINED", "YTD");
        var targets = StorePresentation.Select(store => BuildTarget(store.Key, store.Value, Find(sales, store.Key, "MTD")?.TySales,
            monthlyTargets.GetValueOrDefault(store.Key), engine)).ToList();
        var combinedTarget = SumIfAny(targets.Select(x => x.MonthlyTarget));
        targets.Add(BuildTarget("COMBINED", ("Combined", "#162034"), combinedMtd?.TySales, combinedTarget, engine));
        var serviceSummary = BuildService(service, serviceWdc);
        var conversion = engine.Conversion(combinedFtd?.TyInvoices, combinedFtd?.WalkIns);
        return new(businessDate, "Daily Sales Report (DSR)", "Executive summary · Titan World + Helios",
            combinedFtd?.TySales, engine.Growth(combinedFtd?.TySales, combinedFtd?.LySales), combinedFtd?.TyUnits,
            combinedFtd?.WalkIns, combinedFtd?.TyInvoices, conversion, combinedMtd?.TySales,
            Achievement(combinedMtd?.TySales, combinedTarget), combinedYtd?.TySales,
            engine.Growth(combinedYtd?.TySales, combinedYtd?.LySales), storeCards, serviceSummary, targets, metricPolicy);
    }

    public static CalculatedMetric Achievement(decimal? actual, decimal? target)
    {
        if (actual is null || target is null) return new(null, MetricAvailability.MissingSource, "MTD actual and monthly target are required.");
        if (target == 0) return new(null, MetricAvailability.NotApplicable, "Target achievement is not applicable when the monthly target is zero.");
        return CalculatedMetric.Available(actual.Value / target.Value * 100m, "MTD actual / monthly target × 100");
    }

    private static DsrStoreCard BuildStore(string code, (string Name, string Accent) presentation,
        IReadOnlyList<DsrPeriodFact> rows, ManagementMetricEngine engine)
    {
        var periods = new[] { "FTD", "MTD", "YTD" }.Select(period =>
        {
            var row = Find(rows, code, period);
            var missing = period == "MTD" && row?.LySales is null ? "LY MTD source required" : null;
            return new DsrPeriodComparison(period, row?.TySales, row?.TyUnits, row?.LySales, row?.LyUnits,
                engine.Growth(row?.TySales, row?.LySales), engine.Growth(row?.TyUnits, row?.LyUnits), missing);
        }).ToArray();
        var ftd = Find(rows, code, "FTD"); var mtd = Find(rows, code, "MTD"); var ytd = Find(rows, code, "YTD");
        var operational = new[]
        {
            new DsrOperationalMetric("AUPT", ftd?.Upt, SafeDivide(ftd?.LyUnits, ftd?.LyInvoices), engine.Growth(ftd?.Upt, SafeDivide(ftd?.LyUnits, ftd?.LyInvoices)), $"MTD {DsrDisplay.Number(mtd?.Upt)} · YTD {DsrDisplay.Number(ytd?.Upt)}", false),
            new DsrOperationalMetric("AVPT", ftd?.Atv, SafeDivide(ftd?.LySales, ftd?.LyInvoices), engine.Growth(ftd?.Atv, SafeDivide(ftd?.LySales, ftd?.LyInvoices)), $"MTD {DsrDisplay.Currency(mtd?.Atv)}", true)
        };
        return new(code, presentation.Name, presentation.Accent, periods, operational, ftd?.WalkIns);
    }

    private static DsrTargetProgress BuildTarget(string code, (string Name, string Accent) presentation,
        decimal? actual, decimal? target, ManagementMetricEngine engine) =>
        new(code, presentation.Name, presentation.Accent, actual, target, Achievement(actual, target));

    private static DsrServiceSummary BuildService(IReadOnlyList<DsrServiceFact> rows, decimal? wdc)
    {
        var totals = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var period in new[] { "FTD", "MTD", "YTD" })
        {
            var current = rows.Where(x => x.Period.Equals(period, StringComparison.OrdinalIgnoreCase)).ToArray();
            totals[period] = SumIfComplete(current.Select(x => x.Total));
            totals[$"LY {period}"] = SumIfComplete(current.Select(x => x.LastYearTotal));
        }
        var ftd = rows.Where(x => x.Period.Equals("FTD", StringComparison.OrdinalIgnoreCase)).ToArray();
        var cash = SumIfComplete(ftd.Select(x => x.Cash)); var card = SumIfComplete(ftd.Select(x => x.Card)); var upi = SumIfComplete(ftd.Select(x => x.Upi));
        return new(wdc, cash, card, upi, SumIfAny([wdc, cash, card, upi]), totals);
    }

    private static DsrPeriodFact? Find(IReadOnlyList<DsrPeriodFact> rows, string store, string period) =>
        rows.FirstOrDefault(x => x.StoreCode.Equals(store, StringComparison.OrdinalIgnoreCase) && x.Period.Equals(period, StringComparison.OrdinalIgnoreCase));
    private static decimal? SafeDivide(decimal? numerator, int? denominator) => numerator is null || denominator is null or 0 ? null : numerator / denominator.Value;
    private static decimal? SumIfComplete(IEnumerable<decimal?> values) { var a = values.ToArray(); return a.Length > 0 && a.All(x => x is not null) ? a.Sum(x => x!.Value) : null; }
    private static decimal? SumIfAny(IEnumerable<decimal?> values) { var a = values.ToArray(); return a.Any(x => x is not null) ? a.Sum(x => x ?? 0m) : null; }
}

public static class DsrDisplay
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    public const string Missing = "—";
    public static string Currency(decimal? value) => value is null ? Missing : value.Value.ToString("₹#,##0;−₹#,##0;₹0", India);
    public static string Number(decimal? value, int decimals = 2) => value is null ? Missing : value.Value.ToString(decimals == 0 ? "#,##0" : "0.00", India);
    public static string Percent(CalculatedMetric metric) => metric.Value is null ? "N/A" : $"{metric.Value.Value.ToString("+0.0;-0.0;+0.0", India)}%";
    public static string PercentValue(CalculatedMetric metric) => metric.Value is null ? "N/A" : $"{metric.Value:+0.0;-0.0;+0.0}%";
    public static string ValueQuantity(decimal? value, decimal? quantity) => value is null || quantity is null ? "— / —" : $"{Currency(value)} / {quantity.Value.ToString("#,##0.##", India)}";
    public static string CompactCurrency(decimal? value)
    {
        if (value is null) return Missing;
        var absolute = Math.Abs(value.Value); var sign = value < 0 ? "−" : string.Empty;
        if (absolute >= 10_000_000m) return $"{sign}₹{absolute / 10_000_000m:0.00} Cr";
        if (absolute >= 100_000m) return $"{sign}₹{absolute / 100_000m:0.00} L";
        return Currency(value);
    }
}
