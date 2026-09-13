extern alias EtpApplication;
using System.Windows;
using ArchivedReportGenerationSummary = EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary;
namespace Etp.Reporting.Desktop.Modules.Archive;
public sealed partial class ArchiveWorkspaceView
{
    private string activeArchiveTask = "generations";
    private IReadOnlyList<ArchivedReportGenerationSummary> archiveRows = [];
    private int archiveRefreshRevision;
    private void ApplyArchiveFilter()
    {
        var selected = ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGenerationSummary>().Select(x => x.Id).ToHashSet();
        var rows = archiveRows.Where(row => activeArchiveTask switch { "final-packs" => row.IsFinal, "restatements" => row.SupersedesGenerationId is not null, "re-export" => row.CanReExport, _ => true }).ToArray();
        ReportGenerationGrid.ItemsSource = rows;
        foreach (var row in rows.Where(x => selected.Contains(x.Id))) ReportGenerationGrid.SelectedItems.Add(row);
    }
    public void SelectTask(string id)
    {
        activeArchiveTask = id; ApplyArchiveFilter();
        OpenArchivedGenerationButton.Visibility = id is not ("compare" or "sharing-contacts") ? Visibility.Visible : Visibility.Collapsed;
        CompareArchivedGenerationsButton.Visibility = id == "compare" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { ExportArchivedExcelButton, ExportArchivedPdfButton, ExportArchivedZipButton }) button.Visibility = id is "re-export" or "final-packs" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { ShareArchivedWhatsAppButton, ShareArchivedEmailButton }) button.Visibility = id == "shared" ? Visibility.Visible : Visibility.Collapsed;
    }
}
