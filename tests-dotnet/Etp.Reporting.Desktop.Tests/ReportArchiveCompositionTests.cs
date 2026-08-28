namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportArchiveCompositionTests
{
    [Fact]
    public void Archive_workflow_uses_the_injected_application_contract()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));

        Assert.Contains("Func<string, ReportArchiveQuery> reportArchiveQueryFactory", mainWindow, StringComparison.Ordinal);
        Assert.Contains("reportArchiveQueryFactory(connectionState.ConnectionString).SearchAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("reportArchiveQueryFactory(connectionState.ConnectionString).OpenAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("reportArchiveQueryFactory(connectionState.ConnectionString).CompareAsync", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new Phase2OperationsRepository(connectionState.ConnectionString).LoadReportGenerationsAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerReportArchiveQuery(value)", composition, StringComparison.Ordinal);
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
