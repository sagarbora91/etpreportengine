namespace Etp.Reporting.Desktop.Modules.Reports;

public partial class ReportsWorkspaceView
{
    public event EventHandler<string>? NotificationRequested;
    public bool IsExportInProgress => exportInProgress;
    // Capture the report before yielding. A later query owns its own status and preview.
    internal async Task ExportReportToPathAsync(string path, bool pdf)
    {
        var report = presentation.Current;
        if (!report.CanExportReport || exportInProgress) return;
        var revision = reportRevision;
        var format = pdf ? "PDF" : "Excel";
        exportInProgress = true;
        RefreshExportAvailability();
        using var progress = new OperationProgress(this, "Saving " + format);
        try
        {
            await ExportStaging.WriteAsync(path, temporary => pdf
                ? exportCoordinator.ExportReportPdfAsync(temporary, report.ExportMetadata!, report.ExportData!, report.VisualReport, report.DailySalesReport, progress.Token)
                : exportCoordinator.ExportReportExcelAsync(temporary, report.ExportMetadata!, report.ExportData!, report.VisualReport, progress.Token), progress.Token);
            if (revision == reportRevision) ReportResult.Text = $"{format} report saved to {path}";
            try { await auditRecorder(pdf ? "ExportPdf" : "ExportExcel", "Succeeded", $"{report.ReportCode}: report exported"); }
            catch (Exception ex)
            {
                DesktopDiagnostics.Record(ex, "Reports.Workspace", "REPORT_EXPORT_AUDIT_FAILED");
                if (revision == reportRevision) ReportResult.Text = $"{format} report saved to {path}. Activity history could not be updated. The file was saved; do not repeat the export to repair history.";
            }
        }
        catch (OperationCanceledException) { if (revision == reportRevision) ReportResult.Text = "Export cancelled. The destination file was unchanged."; }
        catch (Exception ex)
        {
            if (revision == reportRevision) HandleFailure(ex, pdf ? "REPORT_PDF_EXPORT_FAILED" : "REPORT_EXCEL_EXPORT_FAILED", $"{format} export failed");
            else DesktopDiagnostics.Record(ex, "Reports.Workspace", "PREVIOUS_REPORT_EXPORT_FAILED");
        }
        finally { exportInProgress = false; RefreshExportAvailability(); if (revision == reportRevision) NotificationRequested?.Invoke(this, ReportResult.Text); }
    }
}
