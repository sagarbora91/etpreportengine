using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

public enum ReportExcelExportRoute { Tabular, Visual }
public enum ReportPdfExportRoute { Tabular, Visual, DailySalesReport }

public interface IReportExportCoordinator
{
    void ExportPackExcel(string filePath, ReportPackDocument document);
    void ExportPackPdf(string filePath, ReportPackDocument document);
    void ExportReportExcel(string filePath, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visualReport);
    void ExportReportPdf(string filePath, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visualReport, DailySalesReportDocument? dailySalesReport);
    void ExportManagementSummaryPdf(string filePath, ExcelReportMetadata metadata, ExcelReportData data);
}

public sealed class ReportExportCoordinator : IReportExportCoordinator
{
    public void ExportPackExcel(string filePath, ReportPackDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        new OpenXmlReportPackExporter().Export(RequiredPath(filePath), document);
    }

    public void ExportPackPdf(string filePath, ReportPackDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        new SimplePdfReportPackExporter().Export(RequiredPath(filePath), document);
    }

    public void ExportReportExcel(
        string filePath,
        ExcelReportMetadata metadata,
        ExcelReportData data,
        VisualReportModel? visualReport)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        if (SelectExcelRoute(visualReport) == ReportExcelExportRoute.Visual)
            new OpenXmlVisualReportExporter().Export(RequiredPath(filePath), visualReport!);
        else
            new OpenXmlReportExporter().Export(RequiredPath(filePath), metadata, data);
    }

    public void ExportReportPdf(
        string filePath,
        ExcelReportMetadata metadata,
        ExcelReportData data,
        VisualReportModel? visualReport,
        DailySalesReportDocument? dailySalesReport)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        var path = RequiredPath(filePath);
        switch (SelectPdfRoute(dailySalesReport, visualReport))
        {
            case ReportPdfExportRoute.DailySalesReport:
                new DailySalesReportPdfExporter().Export(path, dailySalesReport!);
                break;
            case ReportPdfExportRoute.Visual:
                new SimplePdfVisualReportExporter().Export(path, visualReport!);
                break;
            default:
                new SimplePdfReportExporter().Export(path, metadata, data);
                break;
        }
    }

    public void ExportManagementSummaryPdf(string filePath, ExcelReportMetadata metadata, ExcelReportData data)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        new SimplePdfReportExporter().Export(RequiredPath(filePath), metadata, data);
    }

    public static ReportExcelExportRoute SelectExcelRoute(VisualReportModel? visualReport) =>
        visualReport is null ? ReportExcelExportRoute.Tabular : ReportExcelExportRoute.Visual;

    public static ReportPdfExportRoute SelectPdfRoute(DailySalesReportDocument? dailySalesReport, VisualReportModel? visualReport) =>
        dailySalesReport is not null
            ? ReportPdfExportRoute.DailySalesReport
            : visualReport is not null
                ? ReportPdfExportRoute.Visual
                : ReportPdfExportRoute.Tabular;

    private static string RequiredPath(string filePath) =>
        string.IsNullOrWhiteSpace(filePath)
            ? throw new ArgumentException("An export file path is required.", nameof(filePath))
            : filePath;
}
