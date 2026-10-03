extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ServiceJobEvent = EtpApplication::Etp.Reporting.Application.Service.ServiceJobEvent;
using ServiceJobEventKind = EtpApplication::Etp.Reporting.Application.Service.ServiceJobEventKind;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service job history: every list a job was in, with first seen, left and back-on-the-list dates.
/// A job leaving a list is history (decision 15), so the wording is information, never a problem.
/// </summary>
public sealed class ServiceJobHistoryView : ServiceScreenView
{
    private readonly TextBox jobNumber = new() { MinWidth = 220, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private string searched = "";

    public ServiceJobHistoryView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service job history",
            "Enter a job number to see each list the job was in. A job leaving a list is normal history, not a problem.",
            "Service.JobHistory", "SERVICE_JOB_HISTORY_LOAD_FAILED", query, export)
    {
        FilterBar.Children.Add(new TextBlock { Text = "Job number", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(jobNumber, "Job number");
        jobNumber.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await ActivateAsync(); } };
        FilterBar.Children.Add(jobNumber);
        var show = new Button { Content = "Show history", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);
        SetColumns(
        [
            new("List", Width: 180), new("Report", Width: 80), new("First seen", DisplayFormat: "dd MMM yyyy"),
            new("Last listed", DisplayFormat: "dd MMM yyyy"), new("Left the list", Width: 260), new("Back on the list", Width: 200)
        ]);
    }

    public string JobNumber { get => jobNumber.Text; set => jobNumber.Text = value; }

    /// <summary>The trimmed job number the last load asked for.</summary>
    public string SearchedJobNumber => searched;

    protected override string? PrepareLoad()
    {
        searched = (jobNumber.Text ?? "").Trim();
        return searched.Length == 0 ? "Enter a job number and select Show history." : null;
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source) =>
        SummariseEvents(await source.LoadJobHistoryAsync(searched));

    /// <summary>One row per list the job was in, in the order the job first reached each list.</summary>
    public static IReadOnlyList<ServiceGridRow> SummariseEvents(IReadOnlyList<ServiceJobEvent> events) =>
        events.GroupBy(item => (item.ReportCode, item.ListLabel))
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.SnapshotDate).ToArray();
                var listed = ordered.Where(item => item.EventKind != ServiceJobEventKind.LeftList).ToArray();
                DateOnly? firstSeen = listed.Length == 0 ? null : listed.Min(item => item.SnapshotDate);
                DateOnly? lastListed = listed.Length == 0 ? null : listed.Max(item => item.SnapshotDate);
                var left = string.Join("; ", ordered.Where(item => item.EventKind == ServiceJobEventKind.LeftList)
                    .Select(item => $"Left the list on or before {item.SnapshotDate:dd MMM yyyy}"));
                var back = string.Join("; ", ordered.Where(item => item.EventKind == ServiceJobEventKind.Reappeared)
                    .Select(item => $"Back on {item.SnapshotDate:dd MMM yyyy}"));
                return (First: firstSeen ?? ordered[0].SnapshotDate,
                    Row: new ServiceGridRow(group.ToArray(), [group.Key.ListLabel, group.Key.ReportCode, firstSeen, lastListed, left, back]));
            })
            .OrderBy(item => item.First).ThenBy(item => (string?)item.Row.Cells[1], StringComparer.Ordinal)
            .Select(item => item.Row).ToArray();

    protected override string EmptyRowsText => $"No Service list has held job {searched}. Check the number.";
    protected override string Summarise(int count) => $"Job {searched} was in {count:N0} list{(count == 1 ? "" : "s")}.";
    protected override string ExportName => "Service job history";
}
