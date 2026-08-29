namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportExportCompositionTests
{
    [Fact]
    public void Feature_views_own_export_execution_and_composition_owns_exporters()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var reports = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportsWorkspaceView.xaml.cs"));
        var coordinator = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportExportCoordinator.cs"));
        var dailyWorkflow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "DailyWorkflow", "DailyWorkflowWorkspaceView.xaml.cs"));
        var archive = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Archive", "ArchiveWorkspaceView.xaml.cs"));
        var dashboard = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Dashboard", "DashboardView.cs"));

        Assert.DoesNotContain("IReportExportCoordinator", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new SaveFileDialog", mainWindow, StringComparison.Ordinal);
        Assert.Contains("await exportCoordinator.ExportReportExcelAsync", reports, StringComparison.Ordinal);
        Assert.Contains("await exportCoordinator.ExportReportPdfAsync", reports, StringComparison.Ordinal);
        Assert.Contains("new SaveFileDialog", reports, StringComparison.Ordinal);
        Assert.Contains("exportManagementSummaryPdfAsync", dashboard, StringComparison.Ordinal);
        Assert.Contains("Task.Run(export", coordinator, StringComparison.Ordinal);
        Assert.Contains("await exportPackExcelAsync", dailyWorkflow, StringComparison.Ordinal);
        Assert.Contains("await exportPackPdfAsync", dailyWorkflow, StringComparison.Ordinal);
        Assert.Contains("await exportExcelAsync", archive, StringComparison.Ordinal);
        Assert.Contains("await exportPdfAsync", archive, StringComparison.Ordinal);
        Assert.Contains("await auditRecorder(\"ExportExcel\"", reports, StringComparison.Ordinal);
        Assert.Contains("await auditRecorder(\"ExportPdf\"", reports, StringComparison.Ordinal);
        Assert.Contains("await recordAuditAsync(excel ? \"ExportExcel\" : \"ExportPdf\"", dailyWorkflow, StringComparison.Ordinal);
        Assert.Contains("await auditRecorder(\"ExportExcel\"", archive, StringComparison.Ordinal);
        Assert.Contains("await auditRecorder(\"ExportPdf\"", archive, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = auditRecorder(\"ExportExcel\"", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = auditRecorder(\"ExportPdf\"", reports, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = recordAuditAsync(excel ? \"ExportExcel\" : \"ExportPdf\"", dailyWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = auditRecorder(\"ExportExcel\"", archive, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = auditRecorder(\"ExportPdf\"", archive, StringComparison.Ordinal);
        Assert.Contains("packExportInProgress", dailyWorkflow, StringComparison.Ordinal);
        Assert.Contains("exportInProgress", reports, StringComparison.Ordinal);
        Assert.Contains("exportInProgress", archive, StringComparison.Ordinal);
        Assert.Contains("exportInProgress", dashboard, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportPackExcelAsync", composition, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportPackPdfAsync", composition, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportManagementSummaryPdfAsync", composition, StringComparison.Ordinal);

        string[] forbidden =
        [
            "new OpenXmlReportPackExporter",
            "new SimplePdfReportPackExporter",
            "new OpenXmlVisualReportExporter",
            "new OpenXmlReportExporter",
            "new DailySalesReportPdfExporter",
            "new SimplePdfVisualReportExporter",
            "new SimplePdfReportExporter",
            "new TenderVarianceDiagnosticService"
        ];
        foreach (var construction in forbidden)
            Assert.DoesNotContain(construction, mainWindow, StringComparison.Ordinal);

        Assert.Contains("new ReportExportCoordinator()", composition, StringComparison.Ordinal);
        Assert.Contains("new ReportingTenderVarianceDiagnostic()", composition, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
