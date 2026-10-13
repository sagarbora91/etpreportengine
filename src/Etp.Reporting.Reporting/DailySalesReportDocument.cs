using System.Globalization;

namespace Etp.Reporting.Reporting;

public sealed record DsrPeriodFact(string Period, string StoreCode, decimal? TySales, decimal? LySales,
    decimal? TyUnits, decimal? LyUnits, int? TyInvoices, int? LyInvoices, decimal? Upt, decimal? Atv,
    decimal? WalkIns, decimal? ConversionPercent, decimal? TyGiftCards = null, decimal? LyGiftCards = null);

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
    public IReadOnlyList<EveningStoreSheet> EveningSheets { get; init; } = [];
    /// <summary>Combined FTD gift-card sales, shown on their own line; never part of VALUE, VOL or INVOICE (owner decision 13).</summary>
    public decimal? CombinedFtdGiftCards { get; init; }
    /// <summary>
    /// 1.12 N1 (decision 52): the stores of this report whose business date is marked as a no-sale day and that hold no sales
    /// for it. Each carries the one line the DSR, its PDF and the pack print ("No sales (marked by ... on dd MMM yyyy)").
    /// </summary>
    public IReadOnlyList<DsrNoSaleDay> NoSaleDays { get; init; } = [];
    /// <summary>The no-sale line for a store, or null when the store's day is not marked.</summary>
    public string? NoSaleLine(string storeCode) =>
        NoSaleDays.FirstOrDefault(x => string.Equals(x.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))?.Line;
    public string Weekday(CultureInfo? culture = null) => BusinessDate.ToString("dddd", culture ?? CultureInfo.GetCultureInfo("en-IN"));
}

public sealed record DsrStoreDefinition(string Code, string Name);

/// <summary>One store-day marked as a no-sale day, with the line printed for it (built by the caller from the mark).</summary>
public sealed record DsrNoSaleDay(string StoreCode, string Line);

public static class DailySalesReportBuilder
{

    /// <param name="serviceMoneyStores">
    /// The store codes expected to enter Service money (decision 16: the Titan World shop only, as dbo.v_service_manual_money
    /// marks it). When given, the Service card sums only those stores' facts; every other store is not applicable, not missing
    /// (RA-OPS-08). When null, every store's fact counts, as before.
    /// </param>
    public static DailySalesReportDocument Build(DateOnly businessDate, IReadOnlyList<DsrPeriodFact> sales,
        IReadOnlyList<DsrServiceFact> service, IReadOnlyDictionary<string, decimal?> monthlyTargets,
        decimal? serviceWdc = null, string metricPolicy = "DSR_INVOICE_DENOMINATOR_SOURCE_EVIDENCE_V1",
        IReadOnlyList<DsrStoreDefinition>? stores = null, IReadOnlyCollection<string>? serviceMoneyStores = null,
        DateOnly? firstImportedSalesDate = null)
    {
        ArgumentNullException.ThrowIfNull(sales); ArgumentNullException.ThrowIfNull(service); ArgumentNullException.ThrowIfNull(monthlyTargets);
        if (serviceMoneyStores is not null)
            service = service.Where(x => serviceMoneyStores.Contains(x.StoreCode, StringComparer.OrdinalIgnoreCase)).ToArray();
        var engine = new ManagementMetricEngine();
        var definitions = stores ?? sales.Where(x=>x.StoreCode!="COMBINED").Select(x=>x.StoreCode).Distinct(StringComparer.OrdinalIgnoreCase).Select(x=>new DsrStoreDefinition(x,x)).ToArray();
        var storePresentation = definitions.Select((store,index)=>new { Key=store.Code, Value=(Name:store.Name,Accent:index%2==0?"#2269E8":"#7137D4") }).ToArray();
        var storeCards = storePresentation.Select(store => BuildStore(store.Key, store.Value, sales, engine, businessDate, firstImportedSalesDate)).ToArray();
        var combinedFtd = Find(sales, "COMBINED", "FTD");
        var combinedMtd = Find(sales, "COMBINED", "MTD");
        var combinedYtd = Find(sales, "COMBINED", "YTD");
        var targets = storePresentation.Select(store => BuildTarget(store.Key, store.Value, Find(sales, store.Key, "MTD")?.TySales,
            monthlyTargets.GetValueOrDefault(store.Key), engine)).ToList();
        var combinedTarget = SumIfComplete(targets.Select(x => x.MonthlyTarget));
        targets.Add(BuildTarget("COMBINED", ("Combined", "#162034"), combinedMtd?.TySales, combinedTarget, engine));
        var serviceSummary = BuildService(service, serviceWdc);
        // Missing is not zero: combined walk-ins (and the conversion built on them) stay unavailable
        // unless every store has an entered FTD walk-in figure.
        var combinedWalkIns = storeCards.All(x => x.FtdWalkIns is not null) ? combinedFtd?.WalkIns : null;
        var conversion = engine.Conversion(combinedFtd?.TyInvoices, combinedWalkIns);
        return new(businessDate, "Daily Sales Report (DSR)", "Executive summary · " + string.Join(" + ", definitions.Select(x=>x.Name)),
            combinedFtd?.TySales, engine.Growth(combinedFtd?.TySales, combinedFtd?.LySales), combinedFtd?.TyUnits,
            combinedWalkIns, combinedFtd?.TyInvoices, conversion, combinedMtd?.TySales,
            Achievement(combinedMtd?.TySales, combinedTarget), combinedYtd?.TySales,
            engine.Growth(combinedYtd?.TySales, combinedYtd?.LySales), storeCards, serviceSummary, targets, metricPolicy)
        { CombinedFtdGiftCards = combinedFtd?.TyGiftCards };
    }

    /// <summary>The evening-matrix metrics that are a real zero on a day with no sales (never walk-ins or targets).</summary>
    internal static readonly string[] NoSaleZeroMetrics = ["VALUE", "VOL", "INVOICE", "GIFT CARD"];

    /// <summary>
    /// 1.12 N1 (decision 52): applies the store-days marked as no-sale days to a built DSR. A marked store whose FTD holds no
    /// sales gets: FTD sales, units, invoices and gift cards of 0 (a real zero, not "not imported"), the no-sale line as
    /// the first row of its evening matrix (so the screen, the PDF, Excel and the pack carry the same line), and an entry in
    /// <see cref="DailySalesReportDocument.NoSaleDays"/>. MTD and YTD are left as they are. A marked store that does hold
    /// sales for the day (a file imported after the mark) is shown with its sales and no line. Combined figures are summed
    /// again only where every store is now known.
    /// </summary>
    public static DailySalesReportDocument ApplyNoSaleDays(DailySalesReportDocument document, IReadOnlyCollection<DsrNoSaleDay> marks)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(marks);
        var lines = marks.Where(x => !string.IsNullOrWhiteSpace(x.StoreCode) && !string.IsNullOrWhiteSpace(x.Line))
            .GroupBy(x => x.StoreCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Line.Trim(), StringComparer.OrdinalIgnoreCase);
        if (lines.Count == 0) return document;

        var applied = new List<DsrNoSaleDay>();
        var stores = document.Stores.Select(card =>
        {
            if (!lines.TryGetValue(card.StoreCode, out var line) || card.Periods.Count == 0 || card.Periods[0].TyValue is not null) return card;
            applied.Add(new(card.StoreCode, line));
            var ftd = card.Periods[0] with { TyValue = 0m, TyQuantity = card.Periods[0].TyQuantity ?? 0m };
            return card with { Periods = [ftd, .. card.Periods.Skip(1)] };
        }).ToArray();
        var sheets = document.EveningSheets.Select(sheet =>
        {
            if (sheet.StoreCode == "COMBINED" || !lines.TryGetValue(sheet.StoreCode, out var line)) return sheet;
            if (sheet.Rows.Any(row => row.Metric == "VALUE" && row.Ftd is not null)) return sheet;
            if (!applied.Any(x => string.Equals(x.StoreCode, sheet.StoreCode, StringComparison.OrdinalIgnoreCase))) applied.Add(new(sheet.StoreCode, line));
            var rows = sheet.Rows.Select(row => NoSaleZeroMetrics.Contains(row.Metric) && row.Ftd is null ? row with { Ftd = 0m } : row);
            return sheet with { Rows = [new EveningMetricRow(line, null, null, null, null, null, null, "text", "No-sale day"), .. rows] };
        }).ToArray();
        if (applied.Count == 0) return document;

        // The combined FTD is known again when every store now has a value (a marked store counts as 0).
        var combinedFtd = document.CombinedFtd ?? (stores.Length > 0 && stores.All(x => x.Periods.Count > 0 && x.Periods[0].TyValue is not null)
            ? stores.Sum(x => x.Periods[0].TyValue!.Value) : null);
        var units = document.Units ?? (stores.Length > 0 && stores.All(x => x.Periods.Count > 0 && x.Periods[0].TyQuantity is not null)
            ? stores.Sum(x => x.Periods[0].TyQuantity!.Value) : null);
        return document with
        {
            Stores = stores,
            CombinedFtd = combinedFtd,
            Units = units,
            EveningSheets = sheets,
            NoSaleDays = applied.OrderBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static CalculatedMetric Achievement(decimal? actual, decimal? target)
    {
        if (actual is null || target is null) return new(null, MetricAvailability.MissingSource, "MTD actual and monthly target are required.");
        if (target == 0) return new(null, MetricAvailability.NotApplicable, "Target achievement is not applicable when the monthly target is zero.");
        return CalculatedMetric.Available(actual.Value / target.Value * 100m, "MTD actual / monthly target × 100");
    }

    private static DsrStoreCard BuildStore(string code, (string Name, string Accent) presentation,
        IReadOnlyList<DsrPeriodFact> rows, ManagementMetricEngine engine, DateOnly businessDate = default, DateOnly? firstImportedSalesDate = null)
    {
        var periods = new[] { "FTD", "MTD", "YTD" }.Select(period =>
        {
            var row = Find(rows, code, period);
            // RA-SALES-15 (1.10.1): before the first import the last-year month cannot exist; say so with the date.
            var lyMonthStart = businessDate == default ? (DateOnly?)null : new DateOnly(businessDate.Year - 1, businessDate.Month, 1);
            var missing = period == "MTD" && row?.LySales is null
                ? firstImportedSalesDate is { } first && lyMonthStart is { } lyStart && lyStart < first
                    ? $"Last-year period starts before the first import ({first:dd MMM yyyy})"
                    : "LY MTD source required"
                : null;
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
        return new(wdc, cash, card, upi, totals.GetValueOrDefault("FTD"), totals);
    }

    private static DsrPeriodFact? Find(IReadOnlyList<DsrPeriodFact> rows, string store, string period) =>
        rows.FirstOrDefault(x => x.StoreCode.Equals(store, StringComparison.OrdinalIgnoreCase) && x.Period.Equals(period, StringComparison.OrdinalIgnoreCase));
    private static decimal? SafeDivide(decimal? numerator, int? denominator) => numerator is null || denominator is null or 0 ? null : numerator / denominator.Value;
    private static decimal? SumIfComplete(IEnumerable<decimal?> values) { var a = values.ToArray(); return a.Length > 0 && a.All(x => x is not null) ? a.Sum(x => x!.Value) : null; }
    private static decimal? SumIfAny(IEnumerable<decimal?> values) { var a = values.ToArray(); return a.Any(x => x is not null) ? a.Sum(x => x ?? 0m) : null; }
}

public sealed record DsrKpiCard(string Label, string Value, string Secondary);

/// <summary>The six DSR KPI cards, shared by the desktop visual summary and the fallback PDF.</summary>
public static class DsrKpiCards
{
    public const string NotAvailable = "Data not available";

    public static IReadOnlyList<DsrKpiCard> For(DailySalesReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return
        [
            new("COMBINED FTD", DsrDisplay.Currency(report.CombinedFtd), $"{DsrDisplay.Percent(report.CombinedFtdGrowth)} vs LY"),
            new("UNITS", DsrDisplay.Number(report.Units, 0), report.Units is null ? NotAvailable : StoreSplit(report, x => x.Periods[0].TyQuantity)),
            new("WALK-INS", DsrDisplay.Number(report.WalkIns, 0), report.WalkIns is null ? NotAvailable : StoreSplit(report, x => x.FtdWalkIns)),
            new("CONVERSION", DsrDisplay.Percent(report.Conversion).TrimStart('+'), report.Conversion.Value is null || report.WalkIns is null ? NotAvailable : $"{DsrDisplay.Number(report.CombinedInvoices, 0)} invoices / {DsrDisplay.Number(report.WalkIns, 0)} walk-ins"),
            new("MTD SALES", DsrDisplay.CompactCurrency(report.MtdSales), $"{DsrDisplay.Percent(report.MtdTargetAchievement).TrimStart('+')} of target"),
            new("YTD SALES", DsrDisplay.CompactCurrency(report.YtdSales), $"{DsrDisplay.Percent(report.YtdGrowth)} vs LY YTD")
        ];
    }

    private static string StoreSplit(DailySalesReportDocument report, Func<DsrStoreCard, decimal?> selector) =>
        string.Join(" · ", report.Stores.Select(x => $"{x.DisplayName} {DsrDisplay.Number(selector(x), 0)}"));
}

public static class DsrDisplay
{
    public const string Missing = IndianFormat.Missing;
    public static string Currency(decimal? value) => IndianFormat.Money(value);
    public static string Number(decimal? value, int decimals = 2) => IndianFormat.Quantity(value, decimals);
    public static string Percent(CalculatedMetric metric) => IndianFormat.Percent(metric.Value, signed: true);
    public static string PercentValue(CalculatedMetric metric) => IndianFormat.Percent(metric.Value, signed: true);
    public static string ValueQuantity(decimal? value, decimal? quantity) => value is null || quantity is null ? "— / —" : $"{Currency(value)} / {quantity.Value.ToString("#,##0.##", IndianFormat.Culture)}";
    public static string CompactCurrency(decimal? value) => IndianFormat.MoneyCompact(value);
}
