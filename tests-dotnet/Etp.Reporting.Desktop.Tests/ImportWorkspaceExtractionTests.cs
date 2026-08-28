using System.Xml.Linq;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ImportWorkspaceExtractionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

    [Fact]
    public void MainWindow_import_panel_is_only_a_workspace_host()
    {
        var xaml = Read("MainWindow.xaml");
        var source = Read("MainWindow.xaml.cs") + Read("MainWindow.Shell.cs");
        var start = xaml.IndexOf("<Border x:Name=\"ImportPanel\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("<Border x:Name=\"SourceInboxPanel\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var importPanel = xaml[start..end];

        Assert.Contains("<ContentControl x:Name=\"ImportHost\"/>", importPanel, StringComparison.Ordinal);
        Assert.Equal(2, Count(importPanel, "x:Name=\""));
        Assert.True(importPanel.Split('\n').Length <= 5, "The MainWindow import host must remain a compact physical boundary.");
        Assert.DoesNotContain("WorkbookPathInput", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BatchResultsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BrowseWorkbook_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateWorkbook_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RunBatchAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_workspace_owns_manual_and_batch_workflow_with_access_and_audit_boundaries()
    {
        var xaml = Read("Modules", "Imports", "ImportWorkspaceView.xaml");
        var source = Read("Modules", "Imports", "ImportWorkspaceView.xaml.cs");

        Assert.Equal(14, Count(xaml, "x:Name=\""));
        Assert.True(xaml.Split('\n').Length <= 55, "Import workspace XAML exceeded its focused layout ratchet.");
        Assert.True(source.Split('\n').Length <= 310, "Import workspace code-behind exceeded its focused workflow ratchet.");
        Assert.Contains("coordinator.ValidateAsync", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.PersistValidatedAsync", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.RetainValidatedEvidenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.OpenBatchSourceAsync", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.RunBatchAsync", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.CancelBatch()", source, StringComparison.Ordinal);
        Assert.Contains("Owner or Store Manager permission is required.", source, StringComparison.Ordinal);
        Assert.Contains("Owner permission is required.", source, StringComparison.Ordinal);
        Assert.Contains("Controlled source restatement applied", source, StringComparison.Ordinal);
        Assert.Contains("Batch import completed", source, StringComparison.Ordinal);
        Assert.Contains("dashboardRefresher", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_workspace_preserves_staging_retry_cancellation_and_safe_failure_order()
    {
        var source = Read("Modules", "Imports", "ImportWorkspaceView.xaml.cs");
        var open = source.IndexOf("coordinator.OpenBatchSourceAsync", StringComparison.Ordinal);
        var run = source.IndexOf("RunBatchAsync(paths)", open, StringComparison.Ordinal);
        var failedPaths = source.IndexOf("coordinator.FailedBatchPaths", StringComparison.Ordinal);
        var retry = source.IndexOf("RunBatchAsync(coordinator.FailedBatchPaths)", failedPaths, StringComparison.Ordinal);

        Assert.True(open >= 0 && run > open, "Batch sources must be staged before processing starts.");
        Assert.True(failedPaths >= 0 && retry > failedPaths, "Retry must be limited to the coordinator's failed paths.");
        Assert.Contains("CancelBatchButton.IsEnabled = true", source, StringComparison.Ordinal);
        Assert.Contains("ImportProgressBar.Value = x.Completed", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.DescribeFailure(ex).SafeMessage", source, StringComparison.Ordinal);
        Assert.Contains("Batch blocked ({ex.Code})", source, StringComparison.Ordinal);
        Assert.Contains("summary.ExactDuplicates", source, StringComparison.Ordinal);
        Assert.Contains("summary.Conflicts", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_inputs_actions_and_results_are_accessibly_named()
    {
        var document = XDocument.Parse(Read("Modules", "Imports", "ImportWorkspaceView.xaml"));
        var interactive = document.Descendants().Where(element =>
            element.Name.LocalName is "Button" or "TextBox" or "ComboBox" or "DatePicker" or "CheckBox" or "DataGrid" or "ProgressBar");

        foreach (var element in interactive)
        {
            var accessibleName = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.EndsWith(".Name", StringComparison.Ordinal) &&
                attribute.Name.NamespaceName.Contains("System.Windows.Automation", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(accessibleName?.Value), $"{element.Name.LocalName} is missing an automation name.");
        }

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_view_is_dependency_neutral_and_composed_outside_MainWindow()
    {
        var view = Read("Modules", "Imports", "ImportWorkspaceView.xaml.cs");
        var main = Read("MainWindow.xaml.cs");
        var composition = Read("Composition", "DesktopCompositionRoot.cs");

        Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Desktop.Modules.Settings", view, StringComparison.Ordinal);
        Assert.Contains("ImportWorkspaceView", main, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopImportCoordinator importCoordinator", main, StringComparison.Ordinal);
        Assert.Contains("new ImportWorkspaceView(", composition, StringComparison.Ordinal);
        Assert.Contains("importWorkspaceView", composition, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var offset = 0; (offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0; offset += value.Length) count++;
        return count;
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([DesktopRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
