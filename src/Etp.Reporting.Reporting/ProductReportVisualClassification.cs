namespace Etp.Reporting.Reporting;

public enum ProductReportVisualClass
{
    ExecutiveVisual,
    KpiTable,
    KpiChartTable,
    ExceptionDiagnostic,
    TableOnly
}

public sealed record ProductReportVisualClassification(
    string ReportCode,
    ProductReportVisualClass Classification);

public static class ProductReportVisualClassificationRegistry
{
    public static IReadOnlyList<ProductReportVisualClassification> All { get; } =
    [
        Entry("dsr", ProductReportVisualClass.ExecutiveVisual),
        Entry("sales-titan", ProductReportVisualClass.KpiTable),
        Entry("sales-helios", ProductReportVisualClass.KpiTable),
        Entry("sales-combined", ProductReportVisualClass.KpiChartTable),
        Entry("invoice", ProductReportVisualClass.KpiTable),
        Entry("sales-returns", ProductReportVisualClass.KpiTable),
        Entry("sales-brand", ProductReportVisualClass.KpiChartTable),
        Entry("sales-segment", ProductReportVisualClass.KpiChartTable),
        Entry("sales-item", ProductReportVisualClass.KpiTable),
        Entry("stock-closing", ProductReportVisualClass.KpiChartTable),
        Entry("stock-physical", ProductReportVisualClass.KpiTable),
        Entry("stock-variance", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("stock-movement", ProductReportVisualClass.KpiChartTable),
        Entry("stock-group", ProductReportVisualClass.KpiChartTable),
        Entry("stock-brand", ProductReportVisualClass.KpiChartTable),
        Entry("stock-slow", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("staff", ProductReportVisualClass.ExecutiveVisual),
        Entry("tender", ProductReportVisualClass.ExecutiveVisual),
        Entry("cash", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("tender-diagnostic", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("service", ProductReportVisualClass.KpiChartTable),
        Entry("exceptions", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("exception-source", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("exception-unmapped", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("exception-stock", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("exception-staff", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("exception-tender", ProductReportVisualClass.ExceptionDiagnostic),
        Entry("management-trend", ProductReportVisualClass.ExecutiveVisual),
        Entry("invoice-lineage", ProductReportVisualClass.TableOnly)
    ];

    private static readonly IReadOnlyDictionary<string, ProductReportVisualClassification> ByCode =
        All.ToDictionary(entry => entry.ReportCode, StringComparer.OrdinalIgnoreCase);

    public static ProductReportVisualClassification? Find(string reportCode) =>
        string.IsNullOrWhiteSpace(reportCode) ? null : ByCode.GetValueOrDefault(reportCode.Trim());

    public static ProductReportVisualClassification ForReport(string reportCode) =>
        Find(reportCode) ?? throw new KeyNotFoundException($"Report code '{reportCode}' has no visual classification.");

    private static ProductReportVisualClassification Entry(string reportCode, ProductReportVisualClass classification) =>
        new(reportCode, classification);
}
