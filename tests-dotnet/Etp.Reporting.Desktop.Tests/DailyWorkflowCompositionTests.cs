namespace Etp.Reporting.Desktop.Tests;

public sealed class DailyWorkflowCompositionTests
{
    [Fact]
    public void Daily_workflow_uses_injected_application_ports()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("dailyWorkflowQueryFactory(connectionState.ConnectionString).LoadAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyWorkflowCommandsFactory(connectionState.ConnectionString).SaveManualInputAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyWorkflowCommandsFactory(connectionState.ConnectionString).SaveStockCountAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyWorkflowCommandsFactory(connectionState.ConnectionString).SaveStaffTargetAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyWorkflowCommandsFactory(connectionState.ConnectionString).FinaliseAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyWorkflowCommandsFactory(connectionState.ConnectionString).ReopenAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("dailyReportPackGeneratorFactory(connectionState.ConnectionString)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new DailyReportingWorkflowRepository", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new DailyReportingPackService", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerDailyWorkflowService(value)", composition, StringComparison.Ordinal);
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
