extern alias EtpApplication;
using System.Windows;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
namespace Etp.Reporting.Desktop.Modules.Archive;
public sealed partial class ArchiveWorkspaceView
{
    private IReadOnlyList<ArchivedReportGenerationSummary> archiveRows = [];
    private int archiveRefreshRevision;
    private void ApplyArchiveFilter()
    {
        var selected = ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().Select(x => x.Id).ToHashSet();
        var rows = archiveRows.ToArray();
        ReportGenerationGrid.ItemsSource = rows;
        foreach (var row in rows.Where(x => selected.Contains(x.Id))) ReportGenerationGrid.SelectedItems.Add(row);
    }
    public void SelectTask(string id)
    {
        TestSmtpButton.IsEnabled = accessProvider().CanAdminister;
        ApplyArchiveFilter();
        foreach (var button in new[] { OpenArchivedGenerationButton, ExportArchivedExcelButton, ExportArchivedPdfButton,
            ExportArchivedZipButton, ShareArchivedWhatsAppButton, ShareArchivedEmailButton })
            button.Visibility = Visibility.Visible;
    }
}
