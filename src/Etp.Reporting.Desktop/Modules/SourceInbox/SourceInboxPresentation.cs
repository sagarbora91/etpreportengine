extern alias EtpApplication;

using SourceDocumentIntakeOutcome = EtpApplication::Etp.Reporting.Application.SourceInbox.SourceDocumentIntakeOutcome;

namespace Etp.Reporting.Desktop.Modules.SourceInbox;

public static class SourceInboxPresentation
{
    public static string? LifecycleStatus(string selectedStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedStatus);
        return selectedStatus.Equals("All", StringComparison.OrdinalIgnoreCase)
            ? null
            : selectedStatus.Replace(' ', '_').ToUpperInvariant();
    }

    public static string IntakeOutcome(SourceDocumentIntakeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Duplicate
            ? "This document was already received. The existing immutable copy has been selected."
            : outcome.Extraction?.Method == "PADDLE_OCR"
                ? "Document stored. PaddleOCR extraction was captured for human verification."
                : string.IsNullOrWhiteSpace(outcome.Extraction?.Text)
                    ? "Document stored. No usable native text was found; manual review is required."
                    : "Document stored. Native PDF text was extracted and is awaiting human verification.";
    }
}
