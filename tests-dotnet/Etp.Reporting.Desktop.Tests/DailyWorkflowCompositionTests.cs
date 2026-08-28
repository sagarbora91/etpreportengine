namespace Etp.Reporting.Desktop.Tests;

public sealed class DailyWorkflowCompositionTests
{
    [Fact]
    public void Daily_workflow_uses_injected_application_ports()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "DailyWorkflow", "DailyWorkflowWorkspaceView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("DailyWorkflowWorkspaceView dailyWorkflowWorkspace", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new DailyWorkflowWorkspaceView(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new DailyWorkflowWorkspaceView(", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("query.LoadAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveManualInputAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveStockCountAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveStaffTargetAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("FinaliseAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ReopenAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("var stateTask = query.LoadAsync(scope);", workspace, StringComparison.Ordinal);
        Assert.Contains("commandsFactory(connectionString()).SaveManualInputAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("commandsFactory(connectionString()).SaveStockCountAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("commandsFactory(connectionString()).SaveStaffTargetAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("commandsFactory(connectionString()).FinaliseAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("commandsFactory(connectionString()).ReopenAsync", workspace, StringComparison.Ordinal);
        Assert.Contains("packGeneratorFactory(connectionString())", workspace, StringComparison.Ordinal);
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
