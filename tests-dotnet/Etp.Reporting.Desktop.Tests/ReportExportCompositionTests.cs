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
        var dashboard = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Dashboard", "DashboardView.cs"));

        Assert.DoesNotContain("IReportExportCoordinator", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new SaveFileDialog", mainWindow, StringComparison.Ordinal);
        Assert.Contains("exportCoordinator.ExportReportExcel", reports, StringComparison.Ordinal);
        Assert.Contains("exportCoordinator.ExportReportPdf", reports, StringComparison.Ordinal);
        Assert.Contains("new SaveFileDialog", reports, StringComparison.Ordinal);
        Assert.Contains("exportManagementSummaryPdf", dashboard, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportPackExcel", composition, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportPackPdf", composition, StringComparison.Ordinal);
        Assert.Contains("reportExportCoordinator.ExportManagementSummaryPdf", composition, StringComparison.Ordinal);

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
