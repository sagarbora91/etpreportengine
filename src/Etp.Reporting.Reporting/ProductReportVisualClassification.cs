namespace Etp.Reporting.Reporting;

public enum ProductReportVisualClass
{
    ExecutiveVisual,
    KpiTable,
    KpiChartTable,
    ExceptionDiagnostic,
    TableOnly
}

/// <summary>
/// Which summary a report's Summary tab and PDF summary page show (RA-EXPORT-05, 1.9.8). Every family reads the
/// report's own export columns by header; a report whose columns do not carry the family's measures falls back to
/// <see cref="None"/> (the "Rows" card). The detail rows are never recalculated.
/// </summary>
public enum ReportSummaryFamily
{
    None,
    Sales,
    Invoice,
    Stock,
    StockSlow,
    StockBrand,
    StockMovement,
    StockVariance,
    StockPhysical,
    Staff,
    Tender,
    TenderDiagnostic,
    Cash,
    Service,
    Exceptions,
    Trend
}

/// <param name="ReportNames">
/// The export names this report has used (<c>SetExport</c> in the reports workspace, the daily pack tables), so a
/// composer given only an <see cref="ExcelReportMetadata.ReportName"/> can still find the family.
/// </param>
public sealed record ProductReportVisualClassification(
    string ReportCode,
    ProductReportVisualClass Classification,
    ReportSummaryFamily Family,
    IReadOnlyList<string> ReportNames);

public static class ProductReportVisualClassificationRegistry
{
    public static IReadOnlyList<ProductReportVisualClassification> All { get; } =
    [
        Entry("dsr", ProductReportVisualClass.ExecutiveVisual, ReportSummaryFamily.None, "Daily Sales Report", "DSR"),
        Entry("sales-store", ProductReportVisualClass.KpiTable, ReportSummaryFamily.Sales, "Daily Sales", "Store Sales Summary"),
        Entry("sales-combined", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.Sales, "Store Sales", "Combined Sales Summary"),
        Entry("invoice", ProductReportVisualClass.KpiTable, ReportSummaryFamily.Invoice, "Customer-wise Invoices", "Invoice Summary"),
        Entry("sales-returns", ProductReportVisualClass.KpiTable, ReportSummaryFamily.Sales, "Returns Sales", "Returns"),
        Entry("sales-brand", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.Sales, "Brand Sales", "Brand-wise Sales"),
        Entry("sales-segment", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.Sales, "BrandSegment Sales", "Brand-Segment Sales"),
        Entry("sales-item", ProductReportVisualClass.KpiTable, ReportSummaryFamily.Sales, "Item Sales", "Item-wise Sales"),
        Entry("stock-closing", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.Stock, "Closing Stock"),
        Entry("stock-physical", ProductReportVisualClass.KpiTable, ReportSummaryFamily.StockPhysical, "Physical Closing Stock", "Physical Stock"),
        Entry("stock-variance", ProductReportVisualClass.ExceptionDiagnostic, ReportSummaryFamily.StockVariance, "Stock Reconciliation", "Stock Variance", "System Stock"),
        Entry("stock-movement", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.StockMovement, "Stock Movement"),
        Entry("stock-brand", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.StockBrand, "Brand Stock"),
        Entry("stock-slow", ProductReportVisualClass.ExceptionDiagnostic, ReportSummaryFamily.StockSlow, "Slow / Exception Stock"),
        Entry("staff", ProductReportVisualClass.ExecutiveVisual, ReportSummaryFamily.Staff, "Staff CRO Performance", "Staff/CRO Performance", "Staff Performance"),
        Entry("tender", ProductReportVisualClass.ExecutiveVisual, ReportSummaryFamily.Tender, "Invoice Tender Reconciliation", "Tender Reconciliation"),
        Entry("cash", ProductReportVisualClass.ExceptionDiagnostic, ReportSummaryFamily.Cash, "Cash Book"),
        Entry("tender-diagnostic", ProductReportVisualClass.ExceptionDiagnostic, ReportSummaryFamily.TenderDiagnostic, "Tender Variance Diagnostics", "Tender Diagnostics"),
        Entry("service", ProductReportVisualClass.KpiChartTable, ReportSummaryFamily.Service, "Service Sales"),
        Entry("exceptions", ProductReportVisualClass.ExceptionDiagnostic, ReportSummaryFamily.Exceptions, "Daily Exceptions", "Daily Exception Report", "Exceptions"),
        Entry("management-trend", ProductReportVisualClass.ExecutiveVisual, ReportSummaryFamily.Trend, "Management Trend"),
        Entry("invoice-lineage", ProductReportVisualClass.TableOnly, ReportSummaryFamily.None, "Invoice Sales source history", "Invoice Source Drill-down", "Invoice Lineage")
    ];

    private static readonly IReadOnlyDictionary<string, ProductReportVisualClassification> ByCode =
        All.ToDictionary(entry => entry.ReportCode, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, ProductReportVisualClassification> ByName =
        All.SelectMany(entry => entry.ReportNames.Select(name => (name, entry)))
            .ToDictionary(pair => pair.name, pair => pair.entry, StringComparer.OrdinalIgnoreCase);

    public static ProductReportVisualClassification? Find(string reportCode) =>
        string.IsNullOrWhiteSpace(reportCode) ? null : ByCode.GetValueOrDefault(reportCode.Trim());

    public static ProductReportVisualClassification ForReport(string reportCode) =>
        Find(reportCode) ?? throw new KeyNotFoundException($"Report code '{reportCode}' has no visual classification.");

    /// <summary>The entry whose export names include <paramref name="reportName"/>; null for an unknown name.</summary>
    public static ProductReportVisualClassification? FindByReportName(string? reportName) =>
        string.IsNullOrWhiteSpace(reportName) ? null : ByName.GetValueOrDefault(reportName.Trim());

    /// <summary>The summary family for a report: by code first, then by export name, else <see cref="ReportSummaryFamily.None"/>.</summary>
    public static ReportSummaryFamily FamilyFor(string? reportCode, string? reportName) =>
        (reportCode is null ? null : Find(reportCode))?.Family ?? FindByReportName(reportName)?.Family ?? ReportSummaryFamily.None;

    private static ProductReportVisualClassification Entry(string reportCode, ProductReportVisualClass classification, ReportSummaryFamily family, params string[] reportNames) =>
        new(reportCode, classification, family, reportNames);
}
