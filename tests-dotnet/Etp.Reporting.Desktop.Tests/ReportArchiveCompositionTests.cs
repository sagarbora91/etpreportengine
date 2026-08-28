namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportArchiveCompositionTests
{
    [Fact]
    public void Archive_workflow_uses_the_injected_application_contract()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Archive", "ArchiveWorkspaceView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("ArchiveWorkspaceView archiveWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ReportArchiveHost.Content = archiveWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"ReportArchiveHost\"/>", mainXaml, StringComparison.Ordinal);
        Assert.Contains("session.SearchAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.Contains("session.OpenAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.Contains("session.CompareAsync(connectionStringProvider()", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("private ReportPackDocument? currentArchivedDocument", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, ReportArchiveQuery>", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshReportArchive_Click", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportGenerationGrid", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new Phase2OperationsRepository(connectionState.ConnectionString).LoadReportGenerationsAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerReportArchiveQuery(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new ArchiveDistributionPresentationSession(", composition, StringComparison.Ordinal);
        Assert.Contains("new ArchiveWorkspaceView(", composition, StringComparison.Ordinal);
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
