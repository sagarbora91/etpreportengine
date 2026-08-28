namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportsCompositionTests
{
    [Fact]
    public void Report_workflows_use_injected_application_ports()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var reports = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportsWorkspaceView.xaml.cs"));

        Assert.Contains("ReportsWorkspaceView reportsWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, ControlledReportQuery>", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Func<string, ControlledReportQuery> controlledReportQueryFactory", reports, StringComparison.Ordinal);
        Assert.Contains("controlledReportQueryFactory(connectionStringProvider()).RunSalesSummaryAsync", reports, StringComparison.Ordinal);
        Assert.Contains("controlledReportQueryFactory(connectionStringProvider()).LoadStockMovementsAsync", reports, StringComparison.Ordinal);
        Assert.Contains("operationalReportQueryFactory(connectionStringProvider()).LoadInvoiceSummaryAsync", reports, StringComparison.Ordinal);
        Assert.Contains("managementTrendQueryFactory(connectionStringProvider()).LoadAsync", reports, StringComparison.Ordinal);

        Assert.DoesNotContain("OperationalReportRepository", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlServerReportingQueryRepository", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadManagementTrendAsync", mainWindow, StringComparison.Ordinal);

        Assert.Equal(3, CountOccurrences(composition, "new SqlServerApplicationReportQuery(value)"));
    }

    [Fact]
    public void Application_report_status_is_translated_only_at_existing_reporting_boundaries()
    {
        var root = FindRepositoryRoot();
        var reports = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Reports", "ReportsWorkspaceView.xaml.cs"));

        Assert.Contains("ReconciliationStatus ToReportingStatus(ApplicationReportStatus status)", reports, StringComparison.Ordinal);
        Assert.Contains("SetExport(\"Staff CRO Performance\",ToReportingStatus(result.Status)", reports, StringComparison.Ordinal);
        Assert.Contains("SetExport(\"Daily Cash Reconciliation\",ToReportingStatus(result.Status)", reports, StringComparison.Ordinal);
        Assert.Contains("SetExport(\"Tender Variance Diagnostics\",ToReportingStatus(diagnostic.Status)", reports, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
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
