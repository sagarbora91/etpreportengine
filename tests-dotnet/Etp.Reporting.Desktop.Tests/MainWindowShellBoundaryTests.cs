using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

public sealed class MainWindowShellBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

    [Fact]
    public void MainWindow_partials_are_shell_only_and_cannot_regrow_feature_implementations()
    {
        var files = Directory.EnumerateFiles(DesktopRoot, "MainWindow*.cs", SearchOption.TopDirectoryOnly).ToArray();
        var source = string.Join("\n", files.Select(File.ReadAllText));

        Assert.Equal(3, files.Length);
        Assert.True(files.Sum(path => File.ReadLines(path).Count()) <= 907, "MainWindow shell source exceeded the final line ratchet.");
        Assert.True(Count(source, @"(?m)^\s*private\s+(?:readonly\s+)?[^\r\n();=]+\s+\w+\s*(?:=[^;]*)?;\s*$") <= 29,
            "MainWindow field ownership increased.");
        Assert.True(Count(source, @"(?m)^\s*(?:private|protected|public)\s+(?:static\s+|async\s+|override\s+|readonly\s+)*(?:[\w<>,?\[\].]+)\s+\w+\s*\(") <= 62,
            "MainWindow method ownership increased.");

        string[] forbidden =
        [
            "Etp.Reporting.Infrastructure", "Microsoft.Data.SqlClient", "IReportExportCoordinator",
            "SaveFileDialog", "OpenXmlWorkbookReader", "ImportPreflight", "ImportRowStager",
            "RetailReportingPolicy", "ExcelReportColumn", "SetExport(", "ToReportingStatus(",
            "ReportGrid", "WorkbookPathInput", "AccountingBatch", "ProductisationRepository",
            "Phase2OperationsRepository", "new DailyWorkflowWorkspaceView(", "new ReportsWorkspaceView("
        ];
        foreach (var value in forbidden) Assert.DoesNotContain(value, source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_xaml_contains_only_shell_state_and_compact_feature_hosts()
    {
        var xaml = File.ReadAllText(Path.Combine(DesktopRoot, "MainWindow.xaml"));
        Assert.True(File.ReadLines(Path.Combine(DesktopRoot, "MainWindow.xaml")).Count() <= 93,
            "MainWindow XAML exceeded the final shell-host line ratchet.");
        Assert.Equal(58, Regex.Matches(xaml, "x:Name=").Count);
        Assert.True(Regex.Matches(xaml, "(?:Click|SelectionChanged|KeyDown|TextChanged|MouseDoubleClick)=\\\"").Count <= 13,
            "Feature event handlers have regrown in MainWindow XAML.");

        string[] featureHosts =
        [
            "ImportPanel|ImportHost", "SourceInboxPanel|SourceInboxHost", "ReportsPanel|ReportsHost",
            "OperationsPanel|OperationsHost", "InvestigationPanel|InvestigationHost",
            "ReportArchivePanel|ReportArchiveHost", "RegistersPanel|RegistersHost",
            "AccountingPanel|AccountingHost", "MastersPanel|AdministrationHost"
        ];
        foreach (var definition in featureHosts)
        {
            var parts = definition.Split('|');
            var start = xaml.IndexOf($"x:Name=\"{parts[0]}\"", StringComparison.Ordinal);
            var host = xaml.IndexOf($"x:Name=\"{parts[1]}\"", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && host > start, $"{parts[0]} must contain {parts[1]}.");
            Assert.True(host - start < 260, $"{parts[0]} is no longer a compact host.");
        }

        string[] forbiddenControls =
        [
            "ReportGrid", "ReportFrom", "WorkbookPathInput", "RegisterGrid", "AccountingPreviewGrid",
            "ReportGenerationGrid", "OperationsFromInput", "GlobalSearchInput", "MasterTypeInput",
            "ConnectionStringInput", "ManualFieldInput"
        ];
        foreach (var value in forbiddenControls) Assert.DoesNotContain(value, xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RunCatalogueReport_Click", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportPdf_Click", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_feature_partials_remain_deleted()
    {
        Assert.False(File.Exists(Path.Combine(DesktopRoot, "MainWindow.Productisation.cs")));
        Assert.False(File.Exists(Path.Combine(DesktopRoot, "MainWindow.VisualReporting.cs")));
    }

    private static int Count(string source, string pattern) => Regex.Matches(source, pattern).Count;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
