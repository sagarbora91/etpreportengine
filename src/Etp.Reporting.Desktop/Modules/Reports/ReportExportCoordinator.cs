using Etp.Reporting.Reporting;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Etp.Reporting.Desktop.Tests")]

namespace Etp.Reporting.Desktop.Modules.Reports;

public enum ReportExcelExportRoute { Tabular, Visual }
public enum ReportPdfExportRoute { Tabular, Visual, DailySalesReport }

public interface IReportExportCoordinator
{
    Task ExportPackExcelAsync(string filePath, ReportPackDocument document, CancellationToken cancellationToken = default);
    Task ExportPackPdfAsync(string filePath, ReportPackDocument document, CancellationToken cancellationToken = default);
    Task ExportReportExcelAsync(string filePath, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visualReport, CancellationToken cancellationToken = default);
    Task ExportReportPdfAsync(string filePath, ExcelReportMetadata metadata, ExcelReportData data, VisualReportModel? visualReport, DailySalesReportDocument? dailySalesReport, CancellationToken cancellationToken = default);
    Task ExportManagementSummaryPdfAsync(string filePath, ExcelReportMetadata metadata, ExcelReportData data, CancellationToken cancellationToken = default);
}

public sealed class ReportExportCoordinator : IReportExportCoordinator
{
    private readonly Action<string, ReportPackDocument> exportPackExcel;
    private readonly Action<string, ReportPackDocument> exportPackPdf;
    private readonly Action<string, ExcelReportMetadata, ExcelReportData> exportTabularExcel;
    private readonly Action<string, VisualReportModel> exportVisualExcel;
    private readonly Action<string, ExcelReportMetadata, ExcelReportData> exportTabularPdf;
    private readonly Action<string, VisualReportModel> exportVisualPdf;
    private readonly Action<string, DailySalesReportDocument> exportDailySalesPdf;

    public ReportExportCoordinator()
        : this(
            static (path, document) => new OpenXmlReportPackExporter().Export(path, document),
            static (path, document) => new SimplePdfReportPackExporter().Export(path, document),
            static (path, metadata, data) => new OpenXmlReportExporter().Export(path, metadata, data),
            static (path, visual) => new OpenXmlVisualReportExporter().Export(path, visual),
            static (path, metadata, data) => new SimplePdfReportExporter().Export(path, metadata, data),
            static (path, visual) => new SimplePdfVisualReportExporter().Export(path, visual),
            static (path, document) => new DailySalesReportPdfExporter().Export(path, document))
    {
    }

    internal ReportExportCoordinator(
        Action<string, ReportPackDocument> exportPackExcel,
        Action<string, ReportPackDocument> exportPackPdf,
        Action<string, ExcelReportMetadata, ExcelReportData> exportTabularExcel,
        Action<string, VisualReportModel> exportVisualExcel,
        Action<string, ExcelReportMetadata, ExcelReportData> exportTabularPdf,
        Action<string, VisualReportModel> exportVisualPdf,
        Action<string, DailySalesReportDocument> exportDailySalesPdf)
    {
        this.exportPackExcel = exportPackExcel ?? throw new ArgumentNullException(nameof(exportPackExcel));
        this.exportPackPdf = exportPackPdf ?? throw new ArgumentNullException(nameof(exportPackPdf));
        this.exportTabularExcel = exportTabularExcel ?? throw new ArgumentNullException(nameof(exportTabularExcel));
        this.exportVisualExcel = exportVisualExcel ?? throw new ArgumentNullException(nameof(exportVisualExcel));
        this.exportTabularPdf = exportTabularPdf ?? throw new ArgumentNullException(nameof(exportTabularPdf));
        this.exportVisualPdf = exportVisualPdf ?? throw new ArgumentNullException(nameof(exportVisualPdf));
        this.exportDailySalesPdf = exportDailySalesPdf ?? throw new ArgumentNullException(nameof(exportDailySalesPdf));
    }

    public Task ExportPackExcelAsync(string filePath, ReportPackDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = RequiredPath(filePath);
        return RunExporterAsync(() => exportPackExcel(path, document), cancellationToken);
    }

    public Task ExportPackPdfAsync(string filePath, ReportPackDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = RequiredPath(filePath);
        return RunExporterAsync(() => exportPackPdf(path, document), cancellationToken);
    }

    public Task ExportReportExcelAsync(
        string filePath,
        ExcelReportMetadata metadata,
        ExcelReportData data,
        VisualReportModel? visualReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        var path = RequiredPath(filePath);
        if (SelectExcelRoute(visualReport) == ReportExcelExportRoute.Visual)
            return RunExporterAsync(() => exportVisualExcel(path, visualReport!), cancellationToken);
        return RunExporterAsync(() => exportTabularExcel(path, metadata, data), cancellationToken);
    }

    public Task ExportReportPdfAsync(
        string filePath,
        ExcelReportMetadata metadata,
        ExcelReportData data,
        VisualReportModel? visualReport,
        DailySalesReportDocument? dailySalesReport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        var path = RequiredPath(filePath);
        return SelectPdfRoute(dailySalesReport, visualReport) switch
        {
            ReportPdfExportRoute.DailySalesReport => RunExporterAsync(() => exportDailySalesPdf(path, dailySalesReport!), cancellationToken),
            ReportPdfExportRoute.Visual => RunExporterAsync(() => exportVisualPdf(path, visualReport!), cancellationToken),
            _ => RunExporterAsync(() => exportTabularPdf(path, metadata, data), cancellationToken)
        };
    }

    public Task ExportManagementSummaryPdfAsync(string filePath, ExcelReportMetadata metadata, ExcelReportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        var path = RequiredPath(filePath);
        return RunExporterAsync(() => exportTabularPdf(path, metadata, data), cancellationToken);
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

    private static Task RunExporterAsync(Action export, CancellationToken cancellationToken) =>
        Task.Run(export, cancellationToken);
}
