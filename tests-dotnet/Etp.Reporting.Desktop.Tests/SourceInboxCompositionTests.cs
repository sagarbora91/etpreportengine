using Etp.Reporting.Application.SourceInbox;
using Etp.Reporting.Desktop.Modules.SourceInbox;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SourceInboxCompositionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string DesktopRoot = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");

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
