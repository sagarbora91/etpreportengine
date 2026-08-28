extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DataQualityIssue = EtpApplication::Etp.Reporting.Application.OperationsAdministration.DataQualityIssue;
using IOperationsAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IOperationsAdministrationService;
using ManagementTrendPoint = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ManagementTrendPoint;
using ReportSchedule = EtpApplication::Etp.Reporting.Application.OperationsAdministration.ReportSchedule;
using UpdateDataQualityIssue = EtpApplication::Etp.Reporting.Application.OperationsAdministration.UpdateDataQualityIssue;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class OperationsWorkspaceView : UserControl
{
    private readonly OperationsAdministrationPresentationSession session;
    private readonly Func<string> connectionStringProvider;
    private readonly Func<string, IOperationsAdministrationService> serviceFactory;
    private readonly Func<string, CancellationToken, Task<MaintenanceOperationResult>> maintenanceRunner;
    private OperationsAdministrationWorkspaceAccess access = new(false, false, false);

    public OperationsWorkspaceView(
        OperationsAdministrationPresentationSession session,
        Func<string> connectionStringProvider,
        Func<string, IOperationsAdministrationService> serviceFactory,
        Func<string, CancellationToken, Task<MaintenanceOperationResult>> maintenanceRunner)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        this.maintenanceRunner = maintenanceRunner ?? throw new ArgumentNullException(nameof(maintenanceRunner));
        InitializeComponent();
        OperationsFromInput.SelectedDate = DateTime.Today.AddDays(-30);
        OperationsToInput.SelectedDate = DateTime.Today;
    }

    public Func<Task>? DashboardRefreshRequestedAsync { get; set; }
    public Func<string, string, string, Task>? AuditRequestedAsync { get; set; }
    public string StatusText => OperationsStatus.Text;
    public int TrendRowCount => ManagementTrendGrid.Items.Count;
    public int IssueRowCount => DataQualityGrid.Items.Count;

    public void UpdateAccess(OperationsAdministrationWorkspaceAccess value) => access = value;

    public async Task RefreshAsync()
    {
        try
        {
            RequireViewAccess();
            var dashboard = await Service.LoadDashboardAsync(session.CreatePeriod(
                OperationsFromInput.SelectedDate, OperationsToInput.SelectedDate));
            var state = session.Capture(dashboard);
            ManagementTrendGrid.ItemsSource = state.Trend;
            DataQualityGrid.ItemsSource = state.Issues;
            ReportSchedulesGrid.ItemsSource = state.Schedules;
            AutomationRunsGrid.ItemsSource = state.AutomationRuns;
            WatchInboundInput.Text = state.WatchFolders.InboundPath;
            WatchProcessedInput.Text = state.WatchFolders.ProcessedPath;
            WatchFailedInput.Text = state.WatchFolders.FailedPath;
            WatchReportOutputInput.Text = state.WatchFolders.ReportOutputPath;
            WatchEnabledInput.IsChecked = state.WatchFolders.IsEnabled;
            RenderManagementTrendChart(state.Trend);
            OperationsStatus.Text = state.Status;
        }
        catch (Exception ex) { OperationsStatus.Text = $"Operations center could not be refreshed: {ex.Message}"; }
    }

    private IOperationsAdministrationService Service => serviceFactory(connectionStringProvider());
    private async void RefreshOperations_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void SaveAutomationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await Service.SaveWatchFoldersAsync(OperationsAdministrationPresentationSession.CreateWatchFolderCommand(
                WatchInboundInput.Text, WatchProcessedInput.Text, WatchFailedInput.Text,
                WatchReportOutputInput.Text, WatchEnabledInput.IsChecked == true, WatchChangeReasonInput.Text));
            WatchChangeReasonInput.Clear();
            OperationsStatus.Text = "Automatic import and report-output folders were saved and audited.";
            await RefreshAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Automation settings were not saved: {ex.Message}"; }
    }

    private async void RunAutomationNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            OperationsStatus.Text = "Running controlled watch-folder import and due report schedules…";
            var result = await Service.RunAutomationOnceAsync();
            OperationsStatus.Text = result.Message;
            await RefreshAsync();
            if (DashboardRefreshRequestedAsync is not null) await DashboardRefreshRequestedAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Unattended processing failed: {ex.Message}"; }
    }

    private void ReportSchedule_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selection = session.SelectSchedule(ReportSchedulesGrid.SelectedItem as ReportSchedule);
        if (selection is null) return;
        ScheduleTimeInput.Text = selection.Time;
        ScheduleEnabledInput.IsChecked = selection.IsEnabled;
        ScheduleExcelInput.IsChecked = selection.ExportExcel;
        SchedulePdfInput.IsChecked = selection.ExportPdf;
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await Service.SaveScheduleAsync(session.CreateScheduleCommand(
                ScheduleTimeInput.Text, ScheduleEnabledInput.IsChecked == true,
                ScheduleExcelInput.IsChecked == true, SchedulePdfInput.IsChecked == true, ScheduleReasonInput.Text));
            ScheduleReasonInput.Clear();
            OperationsStatus.Text = "The selected report schedule was updated and audited.";
            await RefreshAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Schedule was not saved: {ex.Message}"; }
    }

    private async void UpdateIssueWorkflow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            if (sender is not Button { Tag: string status } || DataQualityGrid.SelectedItem is not DataQualityIssue row)
                throw new InvalidOperationException("Select one data-quality issue.");
            await Service.UpdateIssueAsync(new UpdateDataQualityIssue(row.Id, status, IssueWorkflowReasonInput.Text));
            IssueWorkflowReasonInput.Clear();
            await RefreshAsync();
            OperationsStatus.Text = $"Issue marked {status.Replace('_', ' ').ToLowerInvariant()}. Technical control remains {row.TechnicalControlStatus}.";
        }
        catch (Exception ex) { OperationsStatus.Text = OperationsAdministrationWorkspaceErrors.Friendly(ex); }
    }

    private async void RunBackupNow_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync(
        "backup-etp-database.ps1", "Creating and verifying a checksum database backup…",
        result => result.Succeeded ? $"Backup passed. {result.Message}" : $"Backup failed. {result.Message}",
        ex => $"Backup could not run: {ex.Message}", refreshDashboard: true);

    private async void RunRecoveryDrillNow_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync(
        "invoke-etp-recovery-drill.ps1", "Running an isolated restore, integrity check and lineage comparison…",
        result => result.Succeeded ? "Recovery drill passed and the temporary database was removed." : $"Recovery drill failed. {result.Message}",
        ex => $"Recovery drill could not run: {ex.Message}", refreshDashboard: true);

    private async void CreateSupportPackage_Click(object sender, RoutedEventArgs e) => await RunMaintenanceAsync(
        "new-etp-support-package.ps1", "Creating an aggregate-only support package without source rows or confidential identifiers…",
        result => result.Succeeded ? $"Support package created. {result.Message}" : $"Support package failed. {result.Message}",
        ex => $"Support package could not be created: {ex.Message}", auditSupportPackage: true);

    private async Task RunMaintenanceAsync(
        string script,
        string starting,
        Func<MaintenanceOperationResult, string> completed,
        Func<Exception, string> failed,
        bool refreshDashboard = false,
        bool auditSupportPackage = false)
    {
        try
        {
            RequireOwnerAccess();
            MaintenanceStatus.Text = starting;
            var result = await maintenanceRunner(script, CancellationToken.None);
            MaintenanceStatus.Text = completed(result);
            if (refreshDashboard && DashboardRefreshRequestedAsync is not null) await DashboardRefreshRequestedAsync();
            if (auditSupportPackage && AuditRequestedAsync is not null)
                await AuditRequestedAsync("SupportPackage", result.Succeeded ? "Succeeded" : "Failed", "Privacy-safe support package operation completed");
        }
        catch (Exception ex) { MaintenanceStatus.Text = failed(ex); }
    }

    private void RenderManagementTrendChart(IReadOnlyList<ManagementTrendPoint> rows)
    {
        ManagementTrendChartPanel.Children.Clear();
        var points = rows.GroupBy(x => x.BusinessDate).Select(group => new { Date = group.Key, Sales = group.Sum(x => x.NetSales) }).OrderBy(x => x.Date).TakeLast(31).ToArray();
        var maximum = Math.Max(1m, points.Select(x => Math.Abs(x.Sales)).DefaultIfEmpty(1m).Max());
        foreach (var point in points)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(95) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
            var label = new TextBlock { Text = point.Date.ToString("dd MMM"), VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border { Background = point.Sales < 0 ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(23, 107, 135)), Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Width = 480d * (double)(Math.Abs(point.Sales) / maximum) };
            var value = new TextBlock { Text = point.Sales.ToString("N2"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(label, 0); Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2);
            row.Children.Add(label); row.Children.Add(bar); row.Children.Add(value); ManagementTrendChartPanel.Children.Add(row);
        }
    }

    private void RequireViewAccess()
    {
        if (!access.CanView) throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireImportAccess()
    {
        if (!access.CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!access.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }
}
