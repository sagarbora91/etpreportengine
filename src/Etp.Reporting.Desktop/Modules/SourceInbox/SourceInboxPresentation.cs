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
            ? "This document was already attached. The existing copy has been selected."
            : "Document attached to the business day. Its original and SHA-256 hash are retained.";
    }
}
