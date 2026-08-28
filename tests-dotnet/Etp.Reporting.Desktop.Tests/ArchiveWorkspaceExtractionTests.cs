using System.Xml.Linq;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ArchiveWorkspaceExtractionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

    [Fact]
    public void MainWindow_archive_panel_is_only_a_workspace_host()
    {
        var xaml = Read("MainWindow.xaml");
        var source = Read("MainWindow.xaml.cs");
        Assert.False(File.Exists(Path.Combine(DesktopRoot, "MainWindow.Productisation.cs")));
        var start = xaml.IndexOf("<Border x:Name=\"ReportArchivePanel\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("<Border x:Name=\"RegistersPanel\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var archivePanel = xaml[start..end];

        Assert.Contains("<ContentControl x:Name=\"ReportArchiveHost\"/>", archivePanel, StringComparison.Ordinal);
        Assert.Equal(2, Count(archivePanel, "x:Name=\""));
        Assert.True(archivePanel.Split('\n').Length <= 5, "The MainWindow archive host must remain a compact physical boundary.");
        Assert.DoesNotContain("ReportGenerationGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SharingContactsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshReportArchive_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportArchivedZip_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShareArchivedEmail_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveSharingContact_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_workspace_retains_controls_handlers_access_and_audit_wording()
    {
        var xaml = Read("Modules", "Archive", "ArchiveWorkspaceView.xaml");
        var source = Read("Modules", "Archive", "ArchiveWorkspaceView.xaml.cs");

        Assert.Equal(17, Count(xaml, "x:Name=\""));
        Assert.True(xaml.Split('\n').Length <= 90, "Archive workspace XAML exceeded its focused layout ratchet.");
        Assert.True(source.Split('\n').Length <= 280, "Archive workspace code-behind exceeded its focused workflow ratchet.");
        Assert.Contains("RequireViewAccess();", source, StringComparison.Ordinal);
        Assert.Contains("RequireOwnerAccess();", source, StringComparison.Ordinal);
        Assert.Contains("Archived report opened", source, StringComparison.Ordinal);
        Assert.Contains("Archived generations compared", source, StringComparison.Ordinal);
        Assert.Contains("Archived report pack exported", source, StringComparison.Ordinal);
        Assert.Contains("Immutable ZIP package created. SHA-256", source, StringComparison.Ordinal);
        Assert.Contains("WhatsApp opened; the user must attach and send the prepared file.", source, StringComparison.Ordinal);
        Assert.Contains("Email draft opened; delivery is not claimed.", source, StringComparison.Ordinal);
        Assert.Contains("Sharing contact {id:N0} saved with audit history.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_attachment_policy_is_enforced_before_the_shell_draft_is_opened_and_audited()
    {
        var source = Read("Modules", "Archive", "ArchiveWorkspaceView.xaml.cs");
        var validate = source.IndexOf("ValidateEmailAttachmentAsync", StringComparison.Ordinal);
        var launch = source.IndexOf("shareLauncher.OpenEmailDraft", validate, StringComparison.Ordinal);
        var audit = source.IndexOf("new RecordDistributionAttempt", launch, StringComparison.Ordinal);

        Assert.True(validate >= 0, "Email attachment validation is missing.");
        Assert.True(launch > validate, "The email draft must open only after attachment validation.");
        Assert.True(audit > launch, "An initiated share attempt must be recorded only after the draft opens.");
    }

    [Fact]
    public void Archive_inputs_and_results_remain_accessibly_named()
    {
        var document = XDocument.Parse(Read("Modules", "Archive", "ArchiveWorkspaceView.xaml"));
        var interactive = document.Descendants().Where(element =>
            element.Name.LocalName is "TextBox" or "ComboBox" or "DatePicker" or "CheckBox" or "DataGrid");

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
    public void Archive_view_uses_injected_shell_boundaries()
    {
        var view = Read("Modules", "Archive", "ArchiveWorkspaceView.xaml.cs");
        var launcher = Read("Modules", "Archive", "ArchiveShareLauncher.cs");
        var composition = Read("Composition", "DesktopCompositionRoot.cs");

        Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Desktop.Modules.Settings", view, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Desktop.Modules.Reports", view, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetText", launcher, StringComparison.Ordinal);
        Assert.Contains("Process.Start", launcher, StringComparison.Ordinal);
        Assert.Contains("new ArchiveShareLauncher()", composition, StringComparison.Ordinal);
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
