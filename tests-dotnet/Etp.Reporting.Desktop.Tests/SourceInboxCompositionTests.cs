using Etp.Reporting.Application.SourceInbox;
using Etp.Reporting.Desktop.Modules.SourceInbox;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SourceInboxCompositionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

    [Fact]
    public void MainWindow_source_panel_is_only_a_compact_workspace_host()
    {
        var xaml = Read("MainWindow.xaml");
        var start = xaml.IndexOf("<Border x:Name=\"SourceInboxPanel\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("<Border x:Name=\"ReportsPanel\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var panel = xaml[start..end];

        Assert.Contains("<ContentControl x:Name=\"SourceInboxHost\"/>", panel, StringComparison.Ordinal);
        Assert.Equal(2, Count(panel, "x:Name=\""));
        Assert.True(panel.Split('\n').Length <= 5, "Source Inbox must remain a compact physical host.");
    }

    [Fact]
    public void Source_workspace_owns_all_source_controls_handlers_and_application_calls()
    {
        var main = ReadMainWindowCode();
        var view = Read("Modules", "SourceInbox", "SourceInboxWorkspaceView.xaml.cs");
        var xaml = Read("Modules", "SourceInbox", "SourceInboxWorkspaceView.xaml");

        foreach (var obsolete in new[]
                 {
                     "SourceInboxGrid", "DocumentExtractionGrid", "SourceDocumentPathInput", "ExtractionReviewReasonInput",
                     "RefreshSourceInbox_Click", "SourceInbox_SelectionChanged", "VerifyExtraction_Click",
                     "RejectExtraction_Click", "OpenSourceDocument_Click", "IntakeSourceDocument_Click",
                     "sourceInboxServiceFactory"
                 })
            Assert.DoesNotContain(obsolete, main, StringComparison.Ordinal);

        Assert.Contains("LoadDocumentsAsync(status)", view, StringComparison.Ordinal);
        Assert.Contains("LoadExtractionsAsync(document.Id)", view, StringComparison.Ordinal);
        Assert.Contains("ReviewExtractionAsync(extraction.Id, verified, ReviewReasonInput.Text)", view, StringComparison.Ordinal);
        Assert.Contains("IntakeAsync(new SourceDocumentIntakeRequest", view, StringComparison.Ordinal);
        Assert.Contains("VerifyIntegrityAsync(document)", view, StringComparison.Ordinal);
        Assert.Contains("PDF or image", xaml, StringComparison.Ordinal);
        Assert.Contains("Extraction review", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_root_constructs_workspace_and_MainWindow_relays_only_immutable_id_to_registers()
    {
        var composition = Read("Composition", "DesktopCompositionRoot.cs");
        var main = Read("MainWindow.xaml.cs");

        Assert.Contains("new SqlServerSourceInboxService(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new SourceInboxWorkspaceView(", composition, StringComparison.Ordinal);
        Assert.Contains("new SourceDocumentLauncher()", composition, StringComparison.Ordinal);
        Assert.Contains("SourceInboxHost.Content = sourceInboxWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("sourceInboxWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe)", main, StringComparison.Ordinal);
        Assert.Contains("SelectedDocumentIdChanged += (_, documentId) => registersWorkspaceView.LinkedSourceDocumentId = documentId", main, StringComparison.Ordinal);
        Assert.DoesNotContain("investigationWorkspaceView.LinkedSourceDocumentId", ReadMainWindowCode(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("All", null)]
    [InlineData("Review Required", "REVIEW_REQUIRED")]
    [InlineData("Quarantined", "QUARANTINED")]
    public void Lifecycle_filter_preserves_existing_SQL_status_mapping(string selected, string? expected) =>
        Assert.Equal(expected, SourceInboxPresentation.LifecycleStatus(selected));

    [Fact]
    public void Intake_outcome_keeps_OCR_human_verification_wording()
    {
        var extraction = new SourceDocumentExtractionResult("PADDLE_OCR", "1", "recognized", 0.82m, 1, null, null, "PENDING");
        var message = SourceInboxPresentation.IntakeOutcome(new SourceDocumentIntakeOutcome(Document(41), extraction, false));

        Assert.Equal("Document stored. PaddleOCR extraction was captured for human verification.", message);
    }

    [Fact]
    public void Intake_outcome_never_claims_extraction_when_no_usable_text_exists()
    {
        var message = SourceInboxPresentation.IntakeOutcome(new SourceDocumentIntakeOutcome(Document(42), null, false));

        Assert.Equal("Document stored. No usable native text was found; manual review is required.", message);
    }

    [Fact]
    public void Workspace_preserves_integrity_quarantine_access_error_and_accessibility_contracts()
    {
        var view = Read("Modules", "SourceInbox", "SourceInboxWorkspaceView.xaml.cs");
        var xaml = Read("Modules", "SourceInbox", "SourceInboxWorkspaceView.xaml");

        Assert.Contains("failed its SHA-256 integrity check", view, StringComparison.Ordinal);
        Assert.Contains("document quarantined", view, StringComparison.Ordinal);
        Assert.Contains("RequireViewAccess();", view, StringComparison.Ordinal);
        Assert.Contains("RequireImportAccess();", view, StringComparison.Ordinal);
        Assert.Contains("errorDescriber(ex)", view, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_launcher_rejects_executable_and_missing_managed_documents_before_shell_open()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpSourceLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, "source.exe");
            File.WriteAllText(executable, "not executable");
            Assert.Throws<InvalidOperationException>(() => SourceDocumentLauncher.ValidateManagedDocumentPath(executable));

            var missingPdf = Path.Combine(directory, "missing.pdf");
            Assert.Throws<FileNotFoundException>(() => SourceDocumentLauncher.ValidateManagedDocumentPath(missingPdf));

            var retainedPdf = Path.Combine(directory, "retained.pdf");
            File.WriteAllText(retainedPdf, "%PDF-test");
            Assert.Equal(Path.GetFullPath(retainedPdf), SourceDocumentLauncher.ValidateManagedDocumentPath(retainedPdf));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static SourceInboxDocument Document(long id) => new(
        id, "invoice.pdf", @"C:\managed\invoice.pdf", new string('a', 64), 1024, "PDF", "VENDOR_INVOICE",
        "WLMHW", new DateOnly(2026, 8, 25), "RECEIVED", null, null, null, "tester", DateTime.UtcNow, null);

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine(new[] { DesktopRoot }.Concat(path).ToArray()));

    private static string ReadMainWindowCode() => string.Join(
        Environment.NewLine,
        Directory.GetFiles(DesktopRoot, "MainWindow*.cs").Select(File.ReadAllText));

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length) count++;
        return count;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
