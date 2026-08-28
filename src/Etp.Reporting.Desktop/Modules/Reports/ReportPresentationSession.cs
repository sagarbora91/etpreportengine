using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed record ReportPresentationSnapshot(
    string? ReportCode,
    ExcelReportMetadata? ExportMetadata,
    ExcelReportData? ExportData,
    VisualReportModel? VisualReport,
    DailySalesReportDocument? DailySalesReport,
    ReportPackDocument? DailyPackDocument)
{
    public bool CanExportReport => ExportMetadata is not null && ExportData is not null;
}

/// <summary>
/// Owns the mutable presentation state for the active report and daily pack.
/// Report queries remain outside this type; it only establishes consistent UI transitions.
/// </summary>
public sealed class ReportPresentationSession
{
    public ReportPresentationSnapshot Current { get; private set; } = new(null, null, null, null, null, null);

    public ReportPresentationSnapshot BeginReport(string reportCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        Current = Current with
        {
            ReportCode = reportCode,
            ExportMetadata = null,
            ExportData = null,
            VisualReport = null,
            DailySalesReport = null
        };
        return Current;
    }

    public ReportPresentationSnapshot SetReport(
        ExcelReportMetadata metadata,
        ExcelReportData data,
        DailySalesReportDocument? dailySalesReport = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        var reportCode = dailySalesReport is null &&
                         string.Equals(Current.ReportCode, "dsr", StringComparison.Ordinal)
            ? null
            : Current.ReportCode;
        Current = Current with
        {
            ReportCode = reportCode,
            ExportMetadata = metadata,
            ExportData = data,
            VisualReport = VisualReportComposer.Compose(metadata, data),
            DailySalesReport = dailySalesReport
        };
        return Current;
    }

    public ReportPresentationSnapshot SetDailyPack(ReportPackDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Current = Current with { DailyPackDocument = document };
        return Current;
    }

    public ReportPresentationSnapshot SetDailyPack(
        ReportPackDocument document,
        ExcelReportMetadata metadata,
        ExcelReportData data)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        Current = Current with
        {
            DailyPackDocument = document,
            ExportMetadata = metadata,
            ExportData = data
        };
        return Current;
    }
}
