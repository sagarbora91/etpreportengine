extern alias EtpApplication;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
namespace Etp.Reporting.Desktop.Modules.Archive;
public sealed partial class ArchiveWorkspaceView
{
    private ArchivedReportGenerationSummary SelectedArchiveGeneration() =>
        ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().SingleOrDefault()
        ?? throw new InvalidOperationException("Select exactly one report generation.");

    private void RequireViewAccess()
    {
        if (!accessProvider().CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireOwnerAccess()
    {
        if (!accessProvider().CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private void SetStatus(string message)
    {
        ReportArchiveStatus.Text = message;
        NotificationRequested?.Invoke(this, message);
    }

}
