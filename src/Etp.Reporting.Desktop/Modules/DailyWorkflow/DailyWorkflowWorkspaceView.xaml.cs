extern alias EtpApplication;

using System.Globalization;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Etp.Reporting.Reporting;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

using DailyControlStatus = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyControlStatus;
using DailyReportPackGenerator = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyReportPackGenerator<ReportPackDocument>;
using DailyWorkflowCommands = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowCommands;
using DailyWorkflowQuery = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowQuery;
using DailyWorkflowScope = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyWorkflowScope;

public sealed record DailyWorkflowWorkspaceAccess(bool CanView, bool CanImport, bool CanAdminister);

public sealed class DailyWorkflowNotificationEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

/// <summary>
/// Owns the complete business-date workflow UI and delegates business behavior to
/// the Application daily-workflow ports. The shell only hosts and navigates this view.
/// </summary>
public partial class DailyWorkflowWorkspaceView : UserControl
{
    private readonly DailyWorkflowPresentationSession presentation;
    private readonly Func<string> connectionString;
    private readonly Func<string, DailyWorkflowQuery> queryFactory;
    private readonly Func<string, DailyWorkflowCommands> commandsFactory;
    private readonly Func<string, DailyReportPackGenerator> packGeneratorFactory;
    private Func<DailyWorkflowWorkspaceAccess> access;
    private readonly Func<bool> administratorApproved;
    private Func<string, string, string, Task> recordAuditAsync;
    private readonly Func<string, ReportPackDocument, Task> exportPackExcelAsync;
    private readonly Func<string, ReportPackDocument, Task> exportPackPdfAsync;
    private ReportPackDocument? currentPack;
    private DailyPackBinding? currentPackBinding;
    private bool stateAllowsFinalise;
    private bool packExportInProgress;

    public DailyWorkflowWorkspaceView(
        DailyWorkflowPresentationSession presentation,
        Func<string> connectionString,
        Func<string, DailyWorkflowQuery> queryFactory,
        Func<string, DailyWorkflowCommands> commandsFactory,
        Func<string, DailyReportPackGenerator> packGeneratorFactory,
        Func<DailyWorkflowWorkspaceAccess> access,
        Func<bool>? administratorApproved,
        Func<string, string, string, Task> recordAuditAsync,
        Func<string, ReportPackDocument, Task> exportPackExcelAsync,
        Func<string, ReportPackDocument, Task> exportPackPdfAsync)
    {
        this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        this.connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        this.queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
        this.commandsFactory = commandsFactory ?? throw new ArgumentNullException(nameof(commandsFactory));
        this.packGeneratorFactory = packGeneratorFactory ?? throw new ArgumentNullException(nameof(packGeneratorFactory));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.administratorApproved = administratorApproved ?? IsCurrentWindowsAdministrator;
        this.recordAuditAsync = recordAuditAsync ?? throw new ArgumentNullException(nameof(recordAuditAsync));
        this.exportPackExcelAsync = exportPackExcelAsync ?? throw new ArgumentNullException(nameof(exportPackExcelAsync));
        this.exportPackPdfAsync = exportPackPdfAsync ?? throw new ArgumentNullException(nameof(exportPackPdfAsync));
        InitializeComponent();
        BusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        StaffTargetFromInput.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        StaffTargetToInput.SelectedDate = DateTime.Today.AddDays(-1);
        RefreshAccessState();
    }

    public event EventHandler<DailyWorkflowNotificationEventArgs>? NotificationRequested;

    public Func<Task>? DashboardRefreshRequestedAsync { get; set; }

    public void AttachHost(
        Func<DailyWorkflowWorkspaceAccess> access,
        Func<string, string, string, Task> recordAuditAsync,
        Func<Task> dashboardRefreshRequestedAsync)
    {
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.recordAuditAsync = recordAuditAsync ?? throw new ArgumentNullException(nameof(recordAuditAsync));
        DashboardRefreshRequestedAsync = dashboardRefreshRequestedAsync ?? throw new ArgumentNullException(nameof(dashboardRefreshRequestedAsync));
        RefreshAccessState();
    }

    public string StatusText => WorkflowMessage.Text;

    public DateTime? BusinessDate
    {
        get => BusinessDateInput.SelectedDate;
        set => BusinessDateInput.SelectedDate = value;
    }

    public string? StoreCode
    {
        get => (StoreInput.SelectedItem as ComboBoxItem)?.Content?.ToString();
        set
        {
            var item = StoreInput.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
            if (item is not null) StoreInput.SelectedItem = item;
        }
    }

    public void PrepareForDisplay(bool focusManualEntry)
    {
        RefreshAccessState();
        if (focusManualEntry)
            Dispatcher.BeginInvoke(() => { ManualEntryHeading.BringIntoView(); ManualFieldInput.Focus(); });
    }

    public void RefreshAccessState()
    {
        var current = access();
        RefreshButton.IsEnabled = current.CanView;
        SaveManualInputButton.IsEnabled = current.CanImport;
        SaveStockCountButton.IsEnabled = current.CanImport;
        SaveStaffTargetButton.IsEnabled = current.CanImport;
        FinaliseDayButton.IsEnabled = current.CanImport && stateAllowsFinalise;
        ReopenDayButton.IsEnabled = current.CanAdminister;
        ReopenReasonInput.IsEnabled = current.CanAdminister;
        GenerateStorePackButton.IsEnabled = current.CanView;
        GenerateCombinedPackButton.IsEnabled = current.CanView;
        ExportPackExcelButton.IsEnabled = current.CanView && currentPack is not null && !packExportInProgress;
        ExportPackPdfButton.IsEnabled = current.CanView && currentPack is not null && !packExportInProgress;
    }

    public async Task RefreshAsync()
    {
        try
        {
            RequireViewAccess();
            InvalidatePack();
            var scope = SelectedScope();
            var query = queryFactory(connectionString());
            var stateTask = query.LoadAsync(scope);
            var stockTask = query.LoadStockCountsAsync(scope);
            await Task.WhenAll(stateTask, stockTask);
            Apply(presentation.Show(await stateTask, await stockTask));
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "DailyWorkflow.Workspace", "DAILY_WORKFLOW_LOAD_FAILED");
            Apply(presentation.ShowUnavailable(DesktopFriendlyError.Describe(
                exception,
                "This Windows account does not have application access.")));
        }
    }

    public async Task SaveManualInputAsync()
    {
        try
        {
            RequireImportAccess();
            await commandsFactory(connectionString()).SaveManualInputAsync(
                presentation.CreateManualInput(
                    SelectedScope(), ManualFieldInput.SelectedValue as string, ManualValueInput.Text,
                    Environment.UserName, ManualReasonInput.Text, CultureInfo.CurrentCulture));
            InvalidatePack();
            ManualValueInput.Clear();
            ManualReasonInput.Clear();
            await recordAuditAsync("ManualInput", "Succeeded", "Manual input saved");
            Publish("Manual input saved and daily readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "MANUAL_INPUT_SAVE_FAILED", "Manual input was not saved", "Owner or Store Manager permission is required."); }
    }

    public async Task SaveStockCountAsync()
    {
        try
        {
            RequireImportAccess();
            await commandsFactory(connectionString()).SaveStockCountAsync(
                presentation.CreateStockCount(
                    SelectedScope(), StockGroupInput.Text, StockDisplayInput.Text, StockBackstockInput.Text,
                    StockDefectiveInput.Text, StockYLocationInput.Text, StockPhysicalInput.Text, StockRemarksInput.Text,
                    Environment.UserName, StockReasonInput.Text, CultureInfo.CurrentCulture));
            InvalidatePack();
            StockGroupInput.Clear(); StockDisplayInput.Clear(); StockBackstockInput.Clear(); StockDefectiveInput.Clear();
            StockYLocationInput.Clear(); StockPhysicalInput.Clear(); StockRemarksInput.Clear(); StockReasonInput.Clear();
            await recordAuditAsync("StockCount", "Succeeded", "Physical stock count saved");
            Publish("Physical stock count saved and daily readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "PHYSICAL_STOCK_SAVE_FAILED", "Physical stock count was not saved", "Owner or Store Manager permission is required."); }
    }

    public async Task SaveStaffTargetAsync()
    {
        try
        {
            RequireImportAccess();
            var scope = SelectedScope();
            await commandsFactory(connectionString()).SaveStaffTargetAsync(
                presentation.CreateStaffTarget(
                    scope.StoreCode, StaffTargetCroInput.Text, StaffTargetFromInput.SelectedDate, StaffTargetToInput.SelectedDate,
                    StaffTargetValueInput.Text, Environment.UserName, StaffTargetReasonInput.Text, CultureInfo.CurrentCulture));
            InvalidatePack();
            StaffTargetCroInput.Clear(); StaffTargetValueInput.Clear(); StaffTargetReasonInput.Clear();
            await recordAuditAsync("StaffTarget", "Succeeded", "Staff target saved");
            await RelayDashboardRefreshAsync();
            Publish("Staff/CRO target saved. Target achievement and ranking are available in the staff report.");
        }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_SAVE_FAILED", "Staff target was not saved", "Owner or Store Manager permission is required."); }
    }

    public async Task FinaliseDayAsync()
    {
        try
        {
            RequireImportAccess();
            var scope = SelectedScope();
            var pack = await packGeneratorFactory(connectionString()).GenerateAsync(scope, Environment.UserName);
            ShowPack(pack.Document, pack.Sections, new(scope.BusinessDate, scope.StoreCode));
            await commandsFactory(connectionString()).FinaliseAsync(
                DailyWorkflowPresentationSession.CreateFinalise(scope, Environment.UserName, pack.Sections));
            InvalidatePack();
            await recordAuditAsync("DayFinalised", "Succeeded", "Business day finalised");
            Publish("Business day finalised and dashboard readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "DAY_FINALISE_FAILED", "Day was not finalised", "Owner or Store Manager permission is required."); }
    }

    public async Task ReopenDayAsync()
    {
        try
        {
            RequireOwnerAccess();
            var scope = SelectedScope();
            await commandsFactory(connectionString()).ReopenAsync(
                DailyWorkflowPresentationSession.CreateReopen(
                    scope, Environment.UserName, ReopenReasonInput.Text, administratorApproved()));
            InvalidatePack();
            ReopenReasonInput.Clear();
            await recordAuditAsync("DayReopened", "Succeeded", "Business day reopened");
            Publish("Business day reopened and dashboard readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "DAY_REOPEN_FAILED", "Day was not reopened", "Owner permission is required."); }
    }

    public async Task GenerateDailyPackAsync()
    {
        try
        {
            RequireViewAccess();
            var scope = SelectedScope();
            var pack = await packGeneratorFactory(connectionString()).GenerateAsync(scope, Environment.UserName);
            ShowPack(pack.Document, pack.Sections, new(scope.BusinessDate, scope.StoreCode));
            Publish(DailyWorkflowPresentationSession.PackReady(pack.Status, pack.Message, pack.GenerationNumber, pack.ContentSha256));
            await recordAuditAsync("ReportPack", pack.Status == DailyControlStatus.Passed ? "Succeeded" : "Failed", "Daily report pack");
        }
        catch (Exception exception) { PublishFailure(exception, "DAILY_PACK_GENERATION_FAILED", "Daily report pack failed", "This Windows account does not have application access."); }
    }

    public async Task GenerateCombinedDailyPackAsync()
    {
        try
        {
            RequireViewAccess();
            if (BusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date.");
            var date = DateOnly.FromDateTime(BusinessDateInput.SelectedDate.Value);
            var document = await packGeneratorFactory(connectionString()).GenerateCombinedAsync(date, Environment.UserName);
            ShowPack(document, document.Tables.Select(table => new
                { Report = table.Name, Status = table.Status, Rows = table.Data.Rows.Count, table.Message }), new(date, null));
            Publish($"{document.OverallStatus}: {document.Message} The export contains {document.Tables.Count:N0} report sections.");
            await recordAuditAsync("ReportPack",
                string.Equals(document.OverallStatus, "Passed", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Failed",
                "Combined daily report pack");
        }
        catch (Exception exception) { PublishFailure(exception, "COMBINED_PACK_GENERATION_FAILED", "Combined daily report pack failed", "This Windows account does not have application access."); }
    }

    private void Apply(DailyWorkflowPresentationState state)
    {
        WorkflowStatus.Text = state.Status;
        WorkflowStatus.Foreground = state.Tone switch
        {
            DailyWorkflowTone.Healthy => Brushes.SeaGreen,
            DailyWorkflowTone.Warning => Brushes.DarkOrange,
            _ => Brushes.Firebrick
        };
        WorkflowMessage.Text = state.Message;
        SourceStatus.Text = state.SourceStatus;
        InputStatus.Text = state.InputStatus;
        ManualInputsGrid.ItemsSource = state.ManualInputs;
        ManualFieldInput.ItemsSource = state.ManualInputs;
        if (ManualFieldInput.SelectedIndex < 0 && state.ManualInputs.Count > 0) ManualFieldInput.SelectedIndex = 0;
        StockCountsGrid.ItemsSource = state.StockCounts;
        stateAllowsFinalise = state.CanFinalise;
        RefreshAccessState();
    }

    private void ShowPack(ReportPackDocument document, object rows, DailyPackBinding binding)
    {
        currentPack = document;
        currentPackBinding = binding;
        DailyPackGrid.ItemsSource = rows as System.Collections.IEnumerable;
        RefreshAccessState();
    }

    private void InvalidatePack()
    {
        currentPack = null;
        currentPackBinding = null;
        if (!IsInitialized) return;
        DailyPackGrid.ItemsSource = null;
        RefreshAccessState();
    }

    private bool PackMatchesCurrentScope()
    {
        if (currentPackBinding is null || BusinessDateInput.SelectedDate is null) return false;
        if (currentPackBinding.BusinessDate != DateOnly.FromDateTime(BusinessDateInput.SelectedDate.Value)) return false;
        return currentPackBinding.StoreCode is null ||
               string.Equals(currentPackBinding.StoreCode, StoreCode, StringComparison.OrdinalIgnoreCase);
    }

    private DailyWorkflowScope SelectedScope() => presentation.SelectScope(StoreCode, BusinessDateInput.SelectedDate);

    private void RequireViewAccess()
    {
        if (!access().CanView) throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireImportAccess()
    {
        if (!access().CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!access().CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async Task RelayDashboardRefreshAsync()
    {
        if (DashboardRefreshRequestedAsync is null) return;
        try { await DashboardRefreshRequestedAsync(); }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "DailyWorkflow.Workspace", "DAILY_CHANGE_REFRESH_FAILED", DesktopDiagnosticSeverity.Warning);
            NotificationRequested?.Invoke(this, new($"Daily change was saved, but dashboard refresh failed: {DesktopFriendlyError.Describe(exception)}"));
        }
    }

    private void Publish(string message)
    {
        WorkflowMessage.Text = message;
        NotificationRequested?.Invoke(this, new(message));
    }

    private void PublishFailure(
        Exception exception,
        string eventId,
        string operation,
        string safeUnauthorizedMessage = "Your Windows account does not have permission for this action.")
    {
        DesktopDiagnostics.Record(exception, "DailyWorkflow.Workspace", eventId);
        Publish(DailyWorkflowPresentationSession.Failed(operation, exception, safeUnauthorizedMessage));
    }

    private static bool IsCurrentWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void SaveManualInput_Click(object sender, RoutedEventArgs e) => await SaveManualInputAsync();
    private async void SaveStockCount_Click(object sender, RoutedEventArgs e) => await SaveStockCountAsync();
    private async void SaveStaffTarget_Click(object sender, RoutedEventArgs e) => await SaveStaffTargetAsync();
    private async void FinaliseDay_Click(object sender, RoutedEventArgs e) => await FinaliseDayAsync();
    private async void ReopenDay_Click(object sender, RoutedEventArgs e) => await ReopenDayAsync();
    private async void GenerateDailyPack_Click(object sender, RoutedEventArgs e) => await GenerateDailyPackAsync();
    private async void GenerateCombinedDailyPack_Click(object sender, RoutedEventArgs e) => await GenerateCombinedDailyPackAsync();
    private void Scope_Changed(object sender, SelectionChangedEventArgs e) => InvalidatePack();

    private async void ExportDailyPackExcel_Click(object sender, RoutedEventArgs e) => await ExportCurrentPackAsync("Excel");
    private async void ExportDailyPackPdf_Click(object sender, RoutedEventArgs e) => await ExportCurrentPackAsync("PDF");

    private async Task ExportCurrentPackAsync(string format)
    {
        if (packExportInProgress) return;
        if (currentPack is null || !PackMatchesCurrentScope()) { Publish("Generate the complete daily report pack for the selected store and business date before exporting."); return; }
        var excel = string.Equals(format, "Excel", StringComparison.Ordinal);
        var dialog = new SaveFileDialog
        {
            Filter = excel ? "Excel workbook (*.xlsx)|*.xlsx" : "PDF report (*.pdf)|*.pdf",
            FileName = $"ETP_Daily_Report_Pack_{currentPack.DateTo:yyyyMMdd}.{(excel ? "xlsx" : "pdf")}",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        packExportInProgress = true;
        RefreshAccessState();
        try
        {
            if (excel) await exportPackExcelAsync(dialog.FileName, currentPack); else await exportPackPdfAsync(dialog.FileName, currentPack);
            Publish($"Complete {(excel ? "multi-sheet" : "paginated")} report pack saved to {dialog.FileName}");
            await recordAuditAsync(excel ? "ExportExcel" : "ExportPdf", "Succeeded", "Complete report pack exported");
        }
        catch (Exception exception) { PublishFailure(exception, "REPORT_PACK_EXPORT_FAILED", $"Report-pack {format} export failed", "This Windows account does not have application access."); }
        finally { packExportInProgress = false; RefreshAccessState(); }
    }

    private sealed record DailyPackBinding(DateOnly BusinessDate, string? StoreCode);
}
