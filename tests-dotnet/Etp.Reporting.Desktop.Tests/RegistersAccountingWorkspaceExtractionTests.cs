using System.Xml.Linq;

namespace Etp.Reporting.Desktop.Tests;

public sealed class RegistersAccountingWorkspaceExtractionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

    [Theory]
    [InlineData("RegistersPanel", "RegistersHost", "AccountingPanel")]
    [InlineData("AccountingPanel", "AccountingHost", "MastersPanel")]
    public void MainWindow_feature_panel_is_only_a_compact_workspace_host(
        string panelName,
        string hostName,
        string nextPanelName)
    {
        var xaml = Read("MainWindow.xaml");
        var start = xaml.IndexOf($"<Border x:Name=\"{panelName}\"", StringComparison.Ordinal);
        var end = xaml.IndexOf($"<Border x:Name=\"{nextPanelName}\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var panel = xaml[start..end];

        Assert.Contains($"<ContentControl x:Name=\"{hostName}\"/>", panel, StringComparison.Ordinal);
        Assert.Equal(2, Count(panel, "x:Name=\""));
        Assert.True(panel.Split('\n').Length <= 5, $"{panelName} must remain a compact physical host.");
    }

    [Fact]
    public void MainWindow_has_no_register_or_accounting_control_and_handler_ownership()
    {
        var main = Read("MainWindow.xaml.cs");
        Assert.False(File.Exists(Path.Combine(DesktopRoot, "MainWindow.Productisation.cs")));

        foreach (var obsolete in new[]
                 {
                     "RegisterGrid", "RegisterSearchInput", "SaveRegisterEntry_Click", "RefreshRegistersAsync",
                     "AccountingBatchGrid", "AccountingEntryGrid", "AccountingScope()", "PreviewAccountingBatch_Click",
                     "SaveAccountingBatch_Click", "ApproveAccountingBatch_Click", "ExportTallyXml_Click",
                     "ApproveAccountingMapping_Click"
                 })
            Assert.DoesNotContain(obsolete, main, StringComparison.Ordinal);

        Assert.Contains("RegistersHost.Content = registersWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("AccountingHost.Content = accountingWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("sourceInboxWorkspaceView.SelectedDocumentIdChanged += (_, documentId) => registersWorkspaceView.LinkedSourceDocumentId = documentId", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Registers_workspace_preserves_linkage_access_audit_reason_and_locked_day_error_relay()
    {
        var source = Read("Modules", "Registers", "RegistersWorkspaceView.xaml.cs");
        var main = Read("MainWindow.xaml.cs");

        Assert.Contains("LinkedSourceDocumentId", source, StringComparison.Ordinal);
        Assert.Contains("RegisterReasonInput.Text", source, StringComparison.Ordinal);
        Assert.Contains("accessProvider().DisplayName", source, StringComparison.Ordinal);
        Assert.Contains("\"DRAFT\"", source, StringComparison.Ordinal);
        Assert.Contains("RequireViewAccess();", source, StringComparison.Ordinal);
        Assert.Contains("RequireImportAccess();", source, StringComparison.Ordinal);
        Assert.Contains("Register entry {id:N0} saved with audit history.", source, StringComparison.Ordinal);
        Assert.Contains("registersWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe)", main, StringComparison.Ordinal);
        Assert.Contains("Number: 51210", Read("DesktopFriendlyError.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Accounting_workspace_preserves_preview_approval_mapping_and_tally_workflow()
    {
        var source = Read("Modules", "Accounting", "AccountingWorkspaceView.xaml.cs");
        var session = Read("Modules", "Accounting", "AccountingPresentationSession.cs");

        Assert.Contains("RequireViewAccess();", source, StringComparison.Ordinal);
        Assert.Contains("RequireImportAccess();", source, StringComparison.Ordinal);
        Assert.Contains("RequireOwnerAccess();", source, StringComparison.Ordinal);
        Assert.Contains("Balanced preview: debit", source, StringComparison.Ordinal);
        Assert.Contains("Preview blocked. Missing approved mappings", source, StringComparison.Ordinal);
        Assert.Contains("Accounting batch {id:N0} saved for Owner review.", source, StringComparison.Ordinal);
        Assert.Contains("AccountingMappingReasonInput.Text", source, StringComparison.Ordinal);
        Assert.Contains("Tally XML (*.xml)|*.xml", source, StringComparison.Ordinal);
        Assert.Contains("Saagar Traders", source, StringComparison.Ordinal);
        Assert.Contains("Approved Tally XML exported with SHA-256", source, StringComparison.Ordinal);
        Assert.Contains("AccountingBatchControls.EnsureBalancedAndComplete", session, StringComparison.Ordinal);
        Assert.Contains("Approve the accounting batch before exporting it.", session, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Modules/Registers/RegistersWorkspaceView.xaml", 13, 70)]
    [InlineData("Modules/Accounting/AccountingWorkspaceView.xaml", 10, 60)]
    public void Workspace_controls_are_accessibly_named_and_layout_is_ratcheted(
        string relativePath,
        int expectedNamedControls,
        int maximumLines)
    {
        var path = relativePath.Split('/');
        var xaml = Read(path);
        Assert.Equal(expectedNamedControls, Count(xaml, "x:Name=\""));
        Assert.True(xaml.Split('\n').Length <= maximumLines);
        var document = XDocument.Parse(xaml);
        var interactive = document.Descendants().Where(element =>
            element.Name.LocalName is "TextBox" or "ComboBox" or "DatePicker" or "DataGrid");

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
    public void Workspace_code_behind_remains_focused_and_modules_do_not_cross_reference()
    {
        var registers = Read("Modules", "Registers", "RegistersWorkspaceView.xaml.cs");
        var accounting = Read("Modules", "Accounting", "AccountingWorkspaceView.xaml.cs");

        Assert.True(registers.Split('\n').Length <= 135);
        Assert.True(accounting.Split('\n').Length <= 185);
        Assert.DoesNotContain("Etp.Reporting.Desktop.Modules.Accounting", registers, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Desktop.Modules.Registers", accounting, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", registers, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindow", accounting, StringComparison.Ordinal);
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
