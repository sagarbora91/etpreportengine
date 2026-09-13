extern alias EtpApplication;
using DataQualityIssue = EtpApplication::Etp.Reporting.Application.OperationsAdministration.DataQualityIssue;
using ReportSchedule = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ReportSchedule;
using System.Windows.Controls;
namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class OperationsWorkspaceView
{
    private Button[] issueActions = [];
    private SelectionReasonDrafts? issueReasons;
    public bool HasRetainedDraft => issueReasons?.HasDrafts == true;
    public void DiscardRetainedDraft() => issueReasons?.Discard();
    private void CaptureIssueActions() => issueActions = ((Panel)Content).Children.OfType<Panel>().SelectMany(x => x.Children.OfType<Button>()).Where(x => x.Tag is "ACKNOWLEDGED" or "RESOLVED" or "WAIVED").ToArray();
    private void ApplyActionAccess()
    {
        foreach (var button in issueActions) { button.IsEnabled = access.CanImport; button.ToolTip = access.CanImport ? null : "Owner or Store Manager permission is required."; }
        BackupTaskAction.IsEnabled = RecoveryTaskAction.IsEnabled = SupportTaskAction.IsEnabled = access.CanAdminister;
    }
    private string activeIssueTask = "data-quality";
    private IReadOnlyList<DataQualityIssue> issueRows = [];
    public void SelectIssueTask(string task) { activeIssueTask = task; ApplyIssueFilter(); }
    private void ApplyIssueFilter() => DataQualityGrid.ItemsSource = issueRows.Where(row => activeIssueTask != "open-items" || row.WorkflowStatus is not ("RESOLVED" or "WAIVED")).ToArray();
    private sealed record WatchDraft(string Inbound, string Processed, string Failed, string Output, bool Enabled);
    private WatchDraft? loadedWatch;
    public bool HasWatchDraft => loadedWatch is not null && (CaptureWatch() != loadedWatch || WatchChangeReasonInput.Text.Length > 0);
    public void DiscardWatchDraft()
    {
        if (loadedWatch is not { } value) return;
        WatchInboundInput.Text = value.Inbound; WatchProcessedInput.Text = value.Processed;
        WatchFailedInput.Text = value.Failed; WatchReportOutputInput.Text = value.Output;
        WatchEnabledInput.IsChecked = value.Enabled; WatchChangeReasonInput.Clear();
    }
    private WatchDraft CaptureWatch() => new(WatchInboundInput.Text, WatchProcessedInput.Text, WatchFailedInput.Text, WatchReportOutputInput.Text, WatchEnabledInput.IsChecked == true);
    private void ApplyWatchSettings(OperationsPresentationState state, WatchDraft beforeLoad)
    {
        var current = CaptureWatch();
        if (current != beforeLoad || (loadedWatch is not null && current != loadedWatch)) return;
        WatchInboundInput.Text = state.WatchFolders.InboundPath;
        WatchProcessedInput.Text = state.WatchFolders.ProcessedPath;
        WatchFailedInput.Text = state.WatchFolders.FailedPath;
        WatchReportOutputInput.Text = state.WatchFolders.ReportOutputPath;
        WatchEnabledInput.IsChecked = state.WatchFolders.IsEnabled;
        loadedWatch = CaptureWatch();
    }

    private sealed record ScheduleDraft(string Time, bool Enabled, bool Excel, bool Pdf, string Reason);
    private readonly Dictionary<int, ScheduleDraft> scheduleDrafts = new();
    private readonly Dictionary<int, ScheduleDraft> loadedSchedules = new();
    private int? editingSchedule;
    private bool applyingSchedules;
    private ScheduleDraft CaptureSchedule() => new(ScheduleTimeInput.Text, ScheduleEnabledInput.IsChecked == true,
        ScheduleExcelInput.IsChecked == true, SchedulePdfInput.IsChecked == true, ScheduleReasonInput.Text);
    private void ApplyScheduleDraft(ScheduleDraft value)
    {
        ScheduleTimeInput.Text = value.Time; ScheduleEnabledInput.IsChecked = value.Enabled;
        ScheduleExcelInput.IsChecked = value.Excel; SchedulePdfInput.IsChecked = value.Pdf; ScheduleReasonInput.Text = value.Reason;
    }
    private void RememberScheduleDraft()
    {
        if (editingSchedule is not { } id) return;
        var current = CaptureSchedule();
        if (current == loadedSchedules.GetValueOrDefault(id)) scheduleDrafts.Remove(id);
        else scheduleDrafts[id] = current;
    }
    public IReadOnlyList<int> UnsavedSchedules { get { RememberScheduleDraft(); return scheduleDrafts.Keys.Order().ToArray(); } }
    private void RetainScheduleSelection()
    {
        if (applyingSchedules) return;
        RememberScheduleDraft();
        var row = ReportSchedulesGrid.SelectedItem as ReportSchedule;
        session.SelectSchedule(row); editingSchedule = row?.Id;
        if (row is null) return;
        var baseline = new ScheduleDraft(row.LocalRunTime.ToString("HH:mm"), row.IsEnabled, row.ExportExcel, row.ExportPdf, string.Empty);
        loadedSchedules[row.Id] = baseline;
        ApplyScheduleDraft(scheduleDrafts.GetValueOrDefault(row.Id) ?? baseline);
    }
    private void ApplySchedules(IReadOnlyList<ReportSchedule> rows)
    {
        RememberScheduleDraft(); var selected = editingSchedule;
        applyingSchedules = true;
        try
        {
            ReportSchedulesGrid.ItemsSource = rows;
            ReportSchedulesGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == selected);
        }
        finally { applyingSchedules = false; }
        editingSchedule = null; RetainScheduleSelection();
    }
    private void AcceptScheduleDraft()
    {
        if (editingSchedule is not { } id) return;
        loadedSchedules[id] = CaptureSchedule(); scheduleDrafts.Remove(id);
    }
    public void DiscardScheduleDraft(int id)
    {
        scheduleDrafts.Remove(id);
        if (editingSchedule == id && loadedSchedules.TryGetValue(id, out var baseline)) ApplyScheduleDraft(baseline);
    }
    public async Task<bool> SaveScheduleDraftAsync(int id)
    {
        var row = ReportSchedulesGrid.Items.OfType<ReportSchedule>().FirstOrDefault(value => value.Id == id);
        if (row is null) { OperationsStatus.Text = $"Schedule {id} is no longer available. Keep or discard its draft."; return false; }
        ReportSchedulesGrid.SelectedItem = row;
        return await SaveScheduleDraftAsync();
    }
}
