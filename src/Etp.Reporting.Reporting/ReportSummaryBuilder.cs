using System.Globalization;

namespace Etp.Reporting.Reporting;

public sealed record ReportSummary(IReadOnlyList<ReportKpi> Kpis, IReadOnlyList<ReportVisual> Visuals)
{
    /// <summary>True when the summary says more than the row count.</summary>
    public bool IsInformative => Visuals.Count > 0 || Kpis.Count > 1;
}

/// <summary>
/// Builds the 3-5 KPIs and the one visual of a report family from the export table already loaded for the report
/// (RA-EXPORT-05, 1.9.8). Columns are found by their export header, so a table that lacks the family's measures
/// (a daily-pack table with other headers, a synthetic test table) gets the "Rows" fallback instead of wrong numbers.
/// A Totals row is authoritative when it carries the measure; otherwise the rows are summed.
/// </summary>
public static class ReportSummaryBuilder
{
    private const int TopCount = 5;

    public static ReportSummary Build(ReportSummaryFamily family, ExcelReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var table = new Table(data);
        var summary = family switch
        {
            ReportSummaryFamily.Sales => Sales(table),
            ReportSummaryFamily.Invoice => Invoice(table),
            ReportSummaryFamily.Stock => Stock(table, slow: false),
            ReportSummaryFamily.StockSlow => Stock(table, slow: true),
            ReportSummaryFamily.StockBrand => StockBrand(table),
            ReportSummaryFamily.StockMovement => StockMovement(table),
            ReportSummaryFamily.StockVariance => StockVariance(table),
            ReportSummaryFamily.StockPhysical => StockPhysical(table),
            ReportSummaryFamily.Staff => Staff(table),
            ReportSummaryFamily.Tender => Tender(table, diagnostic: false),
            ReportSummaryFamily.TenderDiagnostic => Tender(table, diagnostic: true),
            ReportSummaryFamily.Cash => Cash(table),
            ReportSummaryFamily.Service => Service(table),
            ReportSummaryFamily.Exceptions => Exceptions(table),
            ReportSummaryFamily.Trend => Trend(table),
            _ => null
        };
        return summary ?? Fallback(data);
    }

    public static ReportSummary Fallback(ExcelReportData data) => new([new("Rows", data.Rows.Count, "integer")], []);

    private static ReportSummary? Sales(Table t)
    {
        if (t.Find("Net Sales") is not { } sales) return null;
        var total = t.Total(sales);
        var invoices = t.Find("Invoices") is { } inv ? t.Total(inv) : null;
        var kpis = new List<ReportKpi>
        {
            new("Sales incl. GST", total, "currency"),
            new("Units", t.Find("Units") is { } units ? t.Total(units) : null, "number"),
            new("Invoices", invoices, "integer"),
            Average("Average invoice", total, invoices)
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Group") is { } group)
            visuals.Add(Bar("Top 5 by sales incl. GST", t.Points(group, sales), "currency", "Other = remaining groups; values are the report's own rows."));
        return new(kpis, visuals);
    }

    private static ReportSummary? Invoice(Table t)
    {
        if (t.Find("Value incl. GST") is not { } value) return null;
        var total = t.Total(value);
        decimal? invoices = t.Find("Invoice", "Document") is { } document ? t.Distinct(document) : t.Rows.Count;
        var kpis = new List<ReportKpi>
        {
            new("Value incl. GST", total, "currency"),
            new("Invoice quantity", t.Find("Invoice quantity", "Quantity") is { } quantity ? t.Total(quantity) : null, "number"),
            new("Invoices", invoices, "integer"),
            Average("Average invoice", total, invoices)
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Store") is { } store) visuals.Add(Bar("Value incl. GST by store", t.Points(store, value), "currency"));
        return new(kpis, visuals);
    }

    private static ReportSummary? Stock(Table t, bool slow)
    {
        if (t.Find("Quantity") is not { } quantity) return null;
        var mrp = t.FindContaining("MRP value");
        var status = t.Find("Movement Status");
        var kpis = new List<ReportKpi>
        {
            new("Items", t.Rows.Count, "integer"),
            new("Units", t.Total(quantity), "number"),
            new("MRP value (GST incl.)", mrp is { } m ? t.Total(m) : null, "currency", mrp is null ? VisualValueState.NotApplicable : VisualValueState.Available)
        };
        if (status is { } s)
            kpis.Add(slow
                ? new ReportKpi("Exception items (90+ days)", t.Rows.Count(r => t.Text(r, s).StartsWith("SLOW", StringComparison.OrdinalIgnoreCase)), "integer")
                : new ReportKpi("Active items", t.Rows.Count(r => t.Text(r, s).Equals(StockAgeing.Active, StringComparison.OrdinalIgnoreCase)), "integer"));
        var visuals = new List<ReportVisual>();
        if (slow && status is { } band)
            visuals.Add(Bar("Units by ageing band", t.Points(band, quantity), "number"));
        else if (!slow && t.Find("Brand") is { } brand)
            visuals.Add(mrp is { } value
                ? Bar("Top 5 brands by MRP value", t.Points(brand, value), "currency", "Other = remaining brands.")
                : Bar("Top 5 brands by units", t.Points(brand, quantity), "number", "Other = remaining brands."));
        return new(kpis, visuals);
    }

    private static ReportSummary? StockBrand(Table t)
    {
        if (t.Find("Quantity") is not { } quantity) return null;
        var mrp = t.FindContaining("MRP value");
        var kpis = new List<ReportKpi>
        {
            new("Items", t.Find("Items") is { } items ? t.Total(items) : null, "integer"),
            new("Units", t.Total(quantity), "number"),
            new("MRP value (GST incl.)", mrp is { } m ? t.Total(m) : null, "currency", mrp is null ? VisualValueState.NotApplicable : VisualValueState.Available),
            new("Slow items", t.Find("Slow Items") is { } slowItems ? t.Total(slowItems) : null, "integer")
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Brand row", "Brand") is { } brand)
            visuals.Add(mrp is { } value
                ? Bar("Top 5 brand rows by MRP value", t.Points(brand, value), "currency", "Other = remaining brand rows.")
                : Bar("Top 5 brand rows by units", t.Points(brand, quantity), "number", "Other = remaining brand rows."));
        return new(kpis, visuals);
    }

    private static ReportSummary? StockMovement(Table t)
    {
        if (t.Find("Signed Quantity") is not { } signed) return null;
        var values = t.Rows.Select(r => t.Number(r, signed)).Where(v => v is not null).Select(v => v!.Value).ToArray();
        var kpis = new List<ReportKpi>
        {
            new("Movement groups", t.Rows.Count, "integer"),
            new("Net movement", t.Total(signed), "number"),
            new("Inbound units", values.Where(v => v > 0).Sum(), "number"),
            new("Outbound units", -values.Where(v => v < 0).Sum(), "number")
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Movement Type") is { } type) visuals.Add(Bar("Net quantity by movement type", t.Points(type, signed), "number"));
        return new(kpis, visuals);
    }

    private static ReportSummary? StockVariance(Table t)
    {
        if (t.Find("Variance") is not { } variance) return null;
        var kpis = new List<ReportKpi>
        {
            new("Items", t.Rows.Count, "integer"),
            new("Expected closing", t.FindStarting("Expected") is { } expected ? t.Total(expected) : null, "number"),
            new("Reported closing", t.Find("Reported Closing", "System Closing") is { } reported ? t.Total(reported) : null, "number"),
            new("Variance", t.Total(variance), "number"),
            new("Items with variance", t.Rows.Count(r => t.Number(r, variance) is { } v && v != 0), "integer")
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Item") is { } item) visuals.Add(Bar("Largest variances", t.Points(item, variance), "number", "Top 5 items by absolute variance."));
        return new(kpis, visuals);
    }

    private static ReportSummary? StockPhysical(Table t)
    {
        if (t.Find("Physical") is not { } physical || t.Find("System") is not { } system) return null;
        var variance = t.Find("System Variance");
        var kpis = new List<ReportKpi>
        {
            new("Brands", t.Rows.Count, "integer"),
            new("Physical units", t.Total(physical), "number"),
            new("System units", t.Total(system), "number"),
            new("Variance", variance is { } v ? t.Total(v) : null, "number")
        };
        var visuals = new List<ReportVisual>();
        if (variance is { } col && t.Find("Brand") is { } brand) visuals.Add(Bar("System variance by brand", t.Points(brand, col), "number"));
        return new(kpis, visuals);
    }

    private static ReportSummary? Staff(Table t)
    {
        if (t.Find("Value incl. GST") is not { } sales) return null;
        var attributed = t.Total(sales);
        var target = t.Find("Target") is { } tg ? t.Total(tg) : null;
        // The staff export's "Control" row carries recorded-minus-attributed variance in the Contribution % slot
        // (ReportsWorkspaceView.SetExport and the daily pack use the same layout).
        decimal? variance = t.Totals is { } totals && t.Text(totals, 0).Equals("Control", StringComparison.OrdinalIgnoreCase)
                            && t.Find("Contribution %") is { } slot ? t.Number(totals, slot) : null;
        var kpis = new List<ReportKpi>
        {
            new("Attributed sales", attributed, "currency"),
            new("Variance vs recorded", variance, "currency", variance is null ? VisualValueState.NotApplicable : VisualValueState.Available),
            new("Unique invoices", t.Find("Unique invoices") is { } inv ? t.Total(inv) : null, "integer"),
            new("Target achievement", target is > 0 && attributed is { } a ? Math.Round(a / target.Value * 100, 2) : null, "percent", target is > 0 ? VisualValueState.Available : VisualValueState.NotApplicable)
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("CRO name", "CRO") is { } cro) visuals.Add(Bar("Top 5 CROs by sales", t.Points(cro, sales), "currency", "Other = remaining CROs."));
        return new(kpis, visuals);
    }

    private static ReportSummary? Tender(Table t, bool diagnostic)
    {
        if (t.Find("Variance") is not { } variance || t.Find("Tender") is not { } tender) return null;
        var invoice = t.Find("Invoice", "Revenue");
        var invoiceTotal = invoice is { } i ? t.Total(i) : null;
        var tenderTotal = t.Total(tender);
        var kpis = new List<ReportKpi>
        {
            new("Invoice total", invoiceTotal, "currency"),
            new("Tender total", tenderTotal, "currency"),
            new("Variance", t.Total(variance), "currency"),
            new("Documents with variance", t.Rows.Count(r => t.Number(r, variance) is not 0m), "integer")
        };
        var visuals = new List<ReportVisual>();
        if (diagnostic && t.Find("Likely Cause") is { } cause)
            visuals.Add(Bar("Absolute variance by likely cause", t.Points(cause, variance, absolute: true), "currency"));
        else if (!diagnostic && t.Find("Status") is { } status)
            visuals.Add(Bar("Documents by status", t.Counts(status), "integer"));
        return new(kpis, visuals);
    }

    private static ReportSummary? Cash(Table t)
    {
        if (t.Find("Dr — Particular") is not { } drName || t.Find("Dr — Amount") is not { } dr
            || t.Find("Cr — Particular") is not { } crName || t.Find("Cr — Amount") is not { } cr) return null;
        var opening = t.Rows.FirstOrDefault(r => t.Text(r, crName) == "Opening balance");
        var closingRows = t.Rows.Where(r => t.Text(r, drName) == "Closing balance").ToArray();
        var closing = closingRows.LastOrDefault();
        decimal? Sum(int name, int amount, string particular) => t.Rows.Where(r => t.Text(r, name) == particular).Select(r => t.Number(r, amount)).Any(v => v is not null)
            ? t.Rows.Where(r => t.Text(r, name) == particular).Sum(r => t.Number(r, amount) ?? 0) : null;
        var date = t.Find("Date");
        var kpis = new List<ReportKpi>
        {
            new("Opening balance", opening is null ? null : t.Number(opening, cr), "currency"),
            new("Closing balance", closing is null ? null : t.Number(closing, dr), "currency"),
            new("Bank deposits", Sum(drName, dr, "Bank cash deposit"), "currency"),
            new("Expenses", Sum(drName, dr, "Expenses"), "currency"),
            new("Days", date is { } d ? t.Distinct(d) : closingRows.Length, "integer")
        };
        var visuals = new List<ReportVisual>();
        if (date is { } dateColumn && closingRows.Length > 0)
        {
            var points = closingRows.Select(r => new ReportVisualPoint(t.Text(r, dateColumn), t.Number(r, dr), t.Number(r, dr) is null ? VisualValueState.Missing : VisualValueState.Available)).ToArray();
            visuals.Add(new("Closing balance by day", ReportVisualType.Line, [new("Closing balance", points, VisualReportTheme.Blue)], "currency", "A missing closing balance is a day without all three service modes or without a counted cash entry."));
        }
        return new(kpis, visuals);
    }

    private static ReportSummary? Service(Table t)
    {
        if (t.Find("Period") is not { } period || t.Find("Total") is not { } total) return null;
        var first = t.Rows.Select(r => t.Text(r, period)).FirstOrDefault(p => p.Length > 0);
        IReadOnlyList<object?>[] rows = first is null ? [] : t.Rows.Where(r => t.Text(r, period) == first).ToArray();
        ReportKpi Mode(string label, string header)
        {
            if (t.Find(header) is not { } column) return new(label, null, "currency", VisualValueState.NotApplicable);
            var values = rows.Select(r => t.Number(r, column)).ToArray();
            return values.Length == 0 || values.All(v => v is null)
                ? new(label, null, "currency", VisualValueState.Missing)
                : new(label, values.Sum(v => v ?? 0), "currency", VisualValueState.Available, first);
        }
        var suffix = first is null ? "" : $" ({first})";
        var kpis = new List<ReportKpi> { Mode("Cash" + suffix, "Cash"), Mode("Card" + suffix, "Card"), Mode("UPI" + suffix, "UPI"), Mode("Total" + suffix, "Total") };
        var visuals = new List<ReportVisual> { Bar("Service total by period", t.Points(period, total, keepOrder: true), "currency", "Each period is summed over the stores in scope.") };
        return new(kpis, visuals);
    }

    private static ReportSummary? Exceptions(Table t)
    {
        if (t.Find("Severity") is not { } severity) return null;
        bool Blocking(IReadOnlyList<object?> r) => t.Text(r, severity).ToUpperInvariant() is "BLOCKER" or "FAIL";
        var kpis = new List<ReportKpi>
        {
            new("Exceptions", t.Rows.Count, "integer"),
            new("Blockers and failures", t.Rows.Count(Blocking), "integer"),
            new("Warnings and others", t.Rows.Count(r => !Blocking(r)), "integer"),
            new("Variance total", t.Find("Variance") is { } variance ? t.Total(variance) : null, "currency")
        };
        var visuals = new List<ReportVisual>();
        if (t.Find("Area") is { } area) visuals.Add(Bar("Exceptions by area", t.Counts(area), "integer"));
        return new(kpis, visuals);
    }

    private static ReportSummary? Trend(Table t)
    {
        if (t.Find("Date") is not { } date || t.Find("Net Sales") is not { } sales) return null;
        var days = t.Rows.Select(r => (Date: t.Date(r, date), Sales: t.Number(r, sales))).Where(x => x.Date is not null)
            .GroupBy(x => x.Date!.Value).Select(g => (Date: g.Key, Sales: g.Sum(x => x.Sales ?? 0))).OrderBy(x => x.Date).ToArray();
        var total = days.Sum(x => x.Sales);
        var kpis = new List<ReportKpi>
        {
            new("Latest day sales", days.Length == 0 ? null : days[^1].Sales, "currency", days.Length == 0 ? VisualValueState.Missing : VisualValueState.Available, days.Length == 0 ? null : days[^1].Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
            new("Daily average", days.Length == 0 ? null : Math.Round(total / days.Length, 2), "currency", days.Length == 0 ? VisualValueState.Missing : VisualValueState.Available),
            new("Total sales", total, "currency"),
            new("Days", days.Length, "integer")
        };
        var points = days.TakeLast(31).Select(x => new ReportVisualPoint(x.Date.ToString("dd MMM", CultureInfo.InvariantCulture), x.Sales)).ToArray();
        var visuals = new List<ReportVisual> { new("Daily sales incl. GST (last 31 days)", ReportVisualType.Line, [new("Net sales", points, VisualReportTheme.Blue)], "currency", "Sales are additive across stores for the same date.") };
        return new(kpis, visuals);
    }

    private static ReportKpi Average(string label, decimal? total, decimal? count) =>
        count is > 0 && total is { } t ? new(label, Math.Round(t / count.Value, 2), "currency") : new(label, null, "currency", VisualValueState.NotApplicable);

    private static ReportVisual Bar(string title, IReadOnlyList<ReportVisualPoint> points, string format, string? footnote = null) =>
        new(title, ReportVisualType.Bar, [new(title, points, VisualReportTheme.Blue)], format, footnote);

    /// <summary>Header-addressed access to an export table.</summary>
    private sealed class Table(ExcelReportData data)
    {
        public IReadOnlyList<IReadOnlyList<object?>> Rows { get; } = data.Rows;
        public IReadOnlyList<object?>? Totals { get; } = data.Totals;

        public int? Find(params string[] headers)
        {
            foreach (var header in headers)
                for (var i = 0; i < data.Columns.Count; i++)
                    if (string.Equals(data.Columns[i].Header, header, StringComparison.OrdinalIgnoreCase)) return i;
            return null;
        }

        public int? FindContaining(string fragment) => Index(h => h.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        public int? FindStarting(string prefix) => Index(h => h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        private int? Index(Func<string, bool> match)
        {
            for (var i = 0; i < data.Columns.Count; i++) if (match(data.Columns[i].Header)) return i;
            return null;
        }

        public decimal? Total(int column) => VisualReportComposer.Total(data, column);
        public decimal? Number(IReadOnlyList<object?> row, int column) => column < row.Count && VisualReportComposer.TryDecimal(row[column], out var value) ? value : null;
        public string Text(IReadOnlyList<object?> row, int column) => column < row.Count ? row[column] switch
        {
            null => "",
            DateOnly d => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            var v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
        } : "";
        public DateOnly? Date(IReadOnlyList<object?> row, int column) => column < row.Count ? row[column] switch
        {
            DateOnly d => d,
            DateTime d => DateOnly.FromDateTime(d),
            string s when DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        } : null;
        public int Distinct(int column) => Rows.Select(r => Text(r, column)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        /// <summary>Top 5 categories by absolute value with an "Other" bucket (or every category in row order when <paramref name="keepOrder"/>).</summary>
        public IReadOnlyList<ReportVisualPoint> Points(int category, int value, bool absolute = false, bool keepOrder = false)
        {
            var points = Rows.Select(r => new ReportVisualPoint(Category(r, category), absolute ? Math.Abs(Number(r, value) ?? 0) : Number(r, value), Number(r, value) is null ? VisualValueState.Missing : VisualValueState.Available)).ToArray();
            if (!keepOrder) return VisualReportComposer.TopN(points, TopCount);
            return points.Where(p => p.State == VisualValueState.Available).GroupBy(p => p.Category)
                .Select(g => new ReportVisualPoint(g.Key, g.Sum(p => p.Value ?? 0))).ToArray();
        }

        public IReadOnlyList<ReportVisualPoint> Counts(int category) =>
            VisualReportComposer.TopN(Rows.Select(r => new ReportVisualPoint(Category(r, category), 1m)).ToArray(), TopCount);

        private string Category(IReadOnlyList<object?> row, int column) => Text(row, column) is { Length: > 0 } text ? text : "Not available";
    }
}
