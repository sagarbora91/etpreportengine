using Etp.Reporting.Application.SourceInbox;
using Etp.Reporting.Desktop.Modules.SourceInbox;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SourceInboxCompositionTests
{
    [Theory]
    [InlineData("All", null)]
    [InlineData("Review Required", "REVIEW_REQUIRED")]
    [InlineData("Quarantined", "QUARANTINED")]
    public void Lifecycle_filter_preserves_existing_SQL_status_mapping(string selected, string? expected) =>
        Assert.Equal(expected, SourceInboxPresentation.LifecycleStatus(selected));

    [Theory]
    [InlineData(false, "Document attached to the business day. Its original and SHA-256 hash are retained.")]
    [InlineData(true, "This document was already attached. The existing copy has been selected.")]
    public void Intake_reports_attachment_and_duplicate_outcomes(bool duplicate, string expected)
    {
        var message = SourceInboxPresentation.IntakeOutcome(new SourceDocumentIntakeOutcome(Document(41), duplicate));
        Assert.Equal(expected, message);
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

}
