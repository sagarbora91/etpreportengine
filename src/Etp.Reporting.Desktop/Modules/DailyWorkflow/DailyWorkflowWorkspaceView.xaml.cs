extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

using DailyControlStatus = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyControlStatus;
using DailyManualInput = EtpApplication::Etp.Reporting.Application.DailyWorkflow.DailyManualInput;
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
    private readonly Func<DailyWorkflowQuery> queryFactory;
    private readonly Func<DailyWorkflowCommands> commandsFactory;
    private readonly Func<DailyReportPackGenerator> packGeneratorFactory;
    private Func<DailyWorkflowWorkspaceAccess> access;
    private Func<string, string, string, Task> recordAuditAsync;
    private readonly Func<string, ReportPackDocument, Task> exportPackExcelAsync;
    private readonly Func<string, ReportPackDocument, Task> exportPackPdfAsync;
    private ReportPackDocument? currentPack;
    private DailyPackBinding? currentPackBinding;
    private bool stateAllowsFinalise;
    private bool packExportInProgress;
    private bool operationInProgress;
    private int refreshRevision;

    public DailyWorkflowWorkspaceView(
        DailyWorkflowPresentationSession presentation,
        Func<DailyWorkflowQuery> queryFactory,
        Func<DailyWorkflowCommands> commandsFactory,
        Func<DailyReportPackGenerator> packGeneratorFactory,
        Func<DailyWorkflowWorkspaceAccess> access,
        Func<string, string, string, Task> recordAuditAsync,
        Func<string, ReportPackDocument, Task> exportPackExcelAsync,
        Func<string, ReportPackDocument, Task> exportPackPdfAsync)
    {
        this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
        this.queryFactory = queryFactory ?? throw new ArgumentNullException(nameof(queryFactory));
        this.commandsFactory = commandsFactory ?? throw new ArgumentNullException(nameof(commandsFactory));
        this.packGeneratorFactory = packGeneratorFactory ?? throw new ArgumentNullException(nameof(packGeneratorFactory));
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.recordAuditAsync = recordAuditAsync ?? throw new ArgumentNullException(nameof(recordAuditAsync));
        this.exportPackExcelAsync = exportPackExcelAsync ?? throw new ArgumentNullException(nameof(exportPackExcelAsync));
        this.exportPackPdfAsync = exportPackPdfAsync ?? throw new ArgumentNullException(nameof(exportPackPdfAsync));
        InitializeComponent();
        var brandEntry = new Button { Content = "Enter stock by brand", Margin = DesignTokens.Gap("Space8", null, null, null) };
        brandEntry.Click += (_, _) =>
        {
            if (BrandStockEntryWindowFactory is not { } create) return;
            var scope = SelectedScope();
            var window = create(scope.StoreCode, scope.BusinessDate);
            window.Owner = Window.GetWindow(this);
            window.ShowDialog();
        };
        ((Panel)SaveStockCountButton.Parent).Children.Add(brandEntry);
        BusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        TrackStaffTargetPeriodEdits();
        ApplyStaffTargetPeriodDefaults();
        InitializeStaffTargetCopy();
        RefreshAccessState();
    }

    public event EventHandler<DailyWorkflowNotificationEventArgs>? NotificationRequested;

    /// <summary>"Enter stock by brand": store and business date to the dialog. Set by the composition root (1.12 A1).</summary>
    internal Func<string, DateOnly, Window>? BrandStockEntryWindowFactory { get; set; }

    /// <summary>
    /// 1.12 U2 (decision 52, lane N1): the "Mark as no-sale day" or "Withdraw" button for a store-day, built by N1's
    /// <c>NoSaleDayActions</c> (the composition root sets this from <c>MainWindow.NoSaleDays</c>); null when the day needs neither.
    /// The last argument is called after a mark or a withdraw, so the band refreshes. Unset: no button.
    /// </summary>
    public Func<string, DateOnly, Func<Task>, Task<UIElement?>>? NoSaleDayButtonFactory { get; set; }

    /// <summary>Whether the verdict band shows the no-sale day button (tests read it).</summary>
    internal bool NoSaleDayButtonVisible => NoSaleDayHost.Visibility == Visibility.Visible;

    /// <summary>
    /// 1.12 U2 (wave plan C-d): the staff-target period follows the header date: From = the first day of its month, To = the
    /// last day. Called at start and whenever the business date changes. Review m-7: a period the user typed is kept; the
    /// fields are only re-defaulted while they are empty or still hold the previous default. 1.12.1 P4: a date the Owner
    /// picked or typed counts as typed even when it equals the default (choosing 1-31 Oct and then moving the header to
    /// November keeps October), until both fields are cleared.
    /// </summary>
    private void ApplyStaffTargetPeriodDefaults()
    {
        if (BusinessDateInput.SelectedDate is not { } header) return;
        if (!ShouldReplaceStaffTargetPeriod(StaffTargetFromInput.SelectedDate, StaffTargetToInput.SelectedDate, lastStaffTargetDefault,
                staffTargetPeriodTyped)) return;
        var period = TargetCopy.DefaultPeriod(DateOnly.FromDateTime(header));
        lastStaffTargetDefault = period;
        settingStaffTargetDefault = true;
        try
        {
            StaffTargetFromInput.SelectedDate = period.From.ToDateTime(TimeOnly.MinValue);
            StaffTargetToInput.SelectedDate = period.To.ToDateTime(TimeOnly.MinValue);
        }
        finally { settingStaffTargetDefault = false; }
        staffTargetPeriodTyped = false;
    }

    private (DateOnly From, DateOnly To)? lastStaffTargetDefault;
    private bool settingStaffTargetDefault;
    private bool staffTargetPeriodTyped;

    /// <summary>1.12.1 P4 (review m-7): any change of From or To that ETP did not make itself is the Owner's.</summary>
    private void TrackStaffTargetPeriodEdits()
    {
        StaffTargetFromInput.SelectedDateChanged += (_, _) => { if (!settingStaffTargetDefault) staffTargetPeriodTyped = true; };
        StaffTargetToInput.SelectedDateChanged += (_, _) => { if (!settingStaffTargetDefault) staffTargetPeriodTyped = true; };
    }

    /// <summary>Whether the Owner changed the staff-target period since ETP last set it (tests read it).</summary>
    internal bool StaffTargetPeriodTyped => staffTargetPeriodTyped;

    /// <summary>
    /// Review m-7 (tests): true when the staff-target fields are empty, or ETP set them (<paramref name="typed"/> false) and
    /// they still hold the last default period. Never true for a period the Owner picked or typed, unless both are empty.
    /// </summary>
    internal static bool ShouldReplaceStaffTargetPeriod(DateTime? from, DateTime? to, (DateOnly From, DateOnly To)? lastDefault, bool typed = false) =>
        lastDefault is not { } previous
        || (from is null && to is null)
        || (!typed && from is { } f && to is { } t && DateOnly.FromDateTime(f) == previous.From && DateOnly.FromDateTime(t) == previous.To);

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

    private string? requestedStore;
    public string? StoreCode
    {
        get => (StoreInput.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? requestedStore;
        set
        {
            var changed = !string.Equals(StoreCode,value,StringComparison.OrdinalIgnoreCase);
            requestedStore = value;
            var item = StoreInput.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(candidate => string.Equals(candidate.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
            StoreInput.SelectedItem = item;
            if (changed) InvalidateScope();
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
        // D22: staff targets are Owner-only (decision extended 10 Oct 2026), like monthly targets.
        SaveStaffTargetButton.IsEnabled = current.CanAdminister;
        SaveStaffTargetButton.ToolTip = current.CanAdminister ? null : StaffTargetsNeedOwnerMessage;
        ToolTipService.SetShowOnDisabled(SaveStaffTargetButton, true);
        RefreshStaffTargetCopyAccess(current);
        FinaliseDayButton.IsEnabled = current.CanImport && stateAllowsFinalise;
        // B6: a disabled Finalise says why.
        FinaliseDayButton.ToolTip = FinaliseDayButton.IsEnabled ? null
            : !current.CanImport ? "Owner or Store Manager permission is required."
            : "Finalise is available when the day is ready. Refresh, then complete what the band lists.";
        ReopenDayButton.IsEnabled = current.CanAdminister;
        ReopenReasonInput.IsEnabled = current.CanAdminister;
        // FIX-17: generating a pack saves a report generation, which SQL allows only the Owner and Store Managers
        // (daily_report_generations INSERT); a Viewer sees the buttons disabled with the reason instead of a refusal.
        GenerateStorePackButton.IsEnabled = current.CanImport;
        GenerateCombinedPackButton.IsEnabled = current.CanImport;
        GenerateStorePackButton.ToolTip = current.CanImport ? null : PackGenerationNeedsManagerMessage;
        GenerateCombinedPackButton.ToolTip = current.CanImport ? null : PackGenerationNeedsManagerMessage;
        ExportPackExcelButton.IsEnabled = current.CanView && currentPack is not null && !packExportInProgress;
        ExportPackPdfButton.IsEnabled = current.CanView && currentPack is not null && !packExportInProgress;
        if (Content is UIElement content) content.IsEnabled = !operationInProgress;
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(StoreCode)) { WorkflowStatus.Text = "Choose a store"; ApplyVerdictTone(null); Publish("Choose one store in the header."); return; }
        var revision = ++refreshRevision;
        try
        {
            RequireViewAccess();
            InvalidatePack();
            var scope = SelectedScope();
            var query = queryFactory();
            var stateTask = query.LoadAsync(scope);
            var stockTask = query.LoadStockCountsAsync(scope);
            await Task.WhenAll(stateTask, stockTask);
            if (revision != refreshRevision) return;
            Apply(presentation.Show(await stateTask, await stockTask));
            await RefreshDayRegistersAsync();
        }
        catch (Exception exception)
        {
            if (revision != refreshRevision) return;
            DesktopDiagnostics.Record(exception, "DailyWorkflow.Workspace", "DAILY_WORKFLOW_LOAD_FAILED");
            Apply(presentation.ShowUnavailable(DesktopFriendlyError.Describe(
                exception,
                "This Windows account does not have application access.")));
        }
    }

    public async Task SaveManualInputAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualValueInput.Text)) { Publish(cashInputsMode ? "Enter the amount" : "Enter the walk-in count"); ManualValueInput.Focus(); return; }
        if (string.IsNullOrWhiteSpace(StoreCode)) { Publish("Choose one store in the header."); return; }
        if (string.IsNullOrWhiteSpace(ManualReasonInput.Text))
        {
            if (cashInputsMode) { Publish("Enter a reason for this cash entry"); ManualReasonInput.Focus(); return; }
            ManualReasonInput.Text = "Daily count";
        }
        if (!BeginOperation()) return;
        try
        {
            RequireImportAccess();
            var command = presentation.CreateManualInput(
                    SelectedScope(), ManualFieldInput.SelectedValue as string, ManualValueInput.Text,
                    Environment.UserName, ManualReasonInput.Text, CultureInfo.CurrentCulture);
            await commandsFactory().SaveManualInputAsync(command);
            InvalidatePack();
            ManualValueInput.Clear();
            ManualReasonInput.Clear();
            await recordAuditAsync("ManualInput", "Succeeded", "Manual input saved");
            Publish("Manual input saved and daily readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "MANUAL_INPUT_SAVE_FAILED", "Manual input was not saved", "Owner or Store Manager permission is required."); }
        finally { EndOperation(); }
    }

    public async Task SaveStockCountAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            RequireImportAccess();
            await commandsFactory().SaveStockCountAsync(
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
        finally { EndOperation(); }
    }

    public async Task SaveStaffTargetAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            if (!access().CanAdminister) throw new UnauthorizedAccessException(StaffTargetsNeedOwnerMessage);
            var scope = SelectedScope();
            await commandsFactory().SaveStaffTargetAsync(
                presentation.CreateStaffTarget(
                    scope.StoreCode, StaffTargetCroInput.Text, StaffTargetFromInput.SelectedDate, StaffTargetToInput.SelectedDate,
                    StaffTargetValueInput.Text, Environment.UserName, StaffTargetReasonInput.Text, CultureInfo.CurrentCulture));
            InvalidatePack();
            StaffTargetCroInput.Clear(); StaffTargetValueInput.Clear(); StaffTargetReasonInput.Clear();
            await recordAuditAsync("StaffTarget", "Succeeded", "Staff target saved");
            await RelayDashboardRefreshAsync();
            Publish("Staff/CRO target saved. Target achievement and ranking are available in the staff report.");
        }
        catch (Exception exception) { PublishFailure(exception, "STAFF_TARGET_SAVE_FAILED", "Staff target was not saved", StaffTargetsNeedOwnerMessage); }
        finally { EndOperation(); }
    }

    public async Task FinaliseDayAsync()
    {
        if (!BeginOperation()) return;
        using var progress = new OperationProgress(this, "Preparing daily report pack");
        try
        {
            RequireImportAccess();
            var scope = SelectedScope();
            var pack = await packGeneratorFactory().GenerateAsync(scope, Environment.UserName, progress.Token);
            ShowPack(pack.Document, pack.Sections, new(scope.BusinessDate, scope.StoreCode));
            await commandsFactory().FinaliseAsync(
                DailyWorkflowPresentationSession.CreateFinalise(scope, Environment.UserName, pack.Sections), progress.Token);
            InvalidatePack();
            await recordAuditAsync("DayFinalised", "Succeeded", "Business day finalised");
            Publish(DailyWorkflowPresentationSession.Finalised(pack.Sections));
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (OperationCanceledException) { Publish("Cancellation requested. Refresh day status before retrying."); }
        catch (Exception exception) { PublishFailure(exception, "DAY_FINALISE_FAILED", "Day was not finalised", "Owner or Store Manager permission is required."); }
        finally { EndOperation(); }
    }

    public async Task ReopenDayAsync()
    {
        if (!BeginOperation()) return;
        try
        {
            RequireOwnerAccess();
            var scope = SelectedScope();
            await commandsFactory().ReopenAsync(
                DailyWorkflowPresentationSession.CreateReopen(
                    scope, Environment.UserName, ReopenReasonInput.Text));
            InvalidatePack();
            ReopenReasonInput.Clear();
            await recordAuditAsync("DayReopened", "Succeeded", "Business day reopened");
            Publish("Business day reopened and dashboard readiness refreshed.");
            await RefreshAsync();
            await RelayDashboardRefreshAsync();
        }
        catch (Exception exception) { PublishFailure(exception, "DAY_REOPEN_FAILED", "Day was not reopened", "Owner permission is required."); }
        finally { EndOperation(); }
    }

    public async Task GenerateDailyPackAsync()
    {
        if (!BeginOperation()) return;
        using var progress = new OperationProgress(this, "Preparing daily report pack");
        try
        {
            RequireImportAccess();
            var scope = SelectedScope();
            var pack = await packGeneratorFactory().GenerateAsync(scope, Environment.UserName, progress.Token);
            ShowPack(pack.Document, pack.Sections, new(scope.BusinessDate, scope.StoreCode));
            Publish(DailyWorkflowPresentationSession.PackReady(pack.Status, pack.Message, pack.GenerationNumber, pack.ContentSha256));
            await recordAuditAsync("ReportPack", pack.Status == DailyControlStatus.Passed ? "Succeeded" : "Failed", "Daily report pack");
        }
        catch (OperationCanceledException) { Publish("Cancellation requested. Refresh day status before retrying."); }
        catch (Exception exception) { PublishFailure(exception, "DAILY_PACK_GENERATION_FAILED", "Daily report pack failed", PackGenerationNeedsManagerMessage); }
        finally { EndOperation(); }
    }

    public async Task GenerateCombinedDailyPackAsync()
    {
        if (!BeginOperation()) return;
        using var progress = new OperationProgress(this, "Preparing daily report pack");
        try
        {
            RequireImportAccess();
            if (BusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date.");
            var date = DateOnly.FromDateTime(BusinessDateInput.SelectedDate.Value);
            var document = await packGeneratorFactory().GenerateCombinedAsync(date, Environment.UserName, progress.Token);
            ShowPack(document, document.Tables.Select(table => new
                { Report = table.Name, Status = table.Status, Rows = table.Data.Rows.Count, table.Message }), new(date, null));
            Publish($"{document.OverallStatus}: {document.Message} The export contains {document.Tables.Count:N0} report sections.");
            await recordAuditAsync("ReportPack",
                string.Equals(document.OverallStatus, "Passed", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Failed",
                "Combined daily report pack");
        }
        catch (OperationCanceledException) { Publish("Cancellation requested. Refresh day status before retrying."); }
        catch (Exception exception) { PublishFailure(exception, "COMBINED_PACK_GENERATION_FAILED", "Combined daily report pack failed", PackGenerationNeedsManagerMessage); }
        finally { EndOperation(); }
    }

    private bool BeginOperation()
    {
        if (operationInProgress || packExportInProgress) return false;
        operationInProgress = true;
        ++refreshRevision;
        RefreshAccessState();
        Publish("Working on the selected store and business date. Please wait…");
        return true;
    }

    private void EndOperation()
    {
        operationInProgress = false;
        RefreshAccessState();
    }

    private void Apply(DailyWorkflowPresentationState state)
    {
        WorkflowStatus.Text = state.Status;
        ApplyVerdictTone(state.Tone);
        WorkflowMessage.Text = state.Message;
        SourceStatus.Text = state.SourceStatus;
        InputStatus.Text = state.InputStatus;
        ManualInputsGrid.ItemsSource = state.ManualInputs;
        var selectedField = ManualFieldInput.SelectedValue;
        ManualFieldInput.ItemsSource = cashInputsMode ? state.ManualInputs.Where(input => input.FieldCode != "WALK_INS").ToArray() : state.ManualInputs;
        ManualFieldInput.SelectedValue = walkinsMode ? "WALK_INS" : selectedField;
        if (ManualFieldInput.SelectedIndex < 0 && state.ManualInputs.Count > 0) ManualFieldInput.SelectedValue = cashInputsMode ? "OPENING_CASH" : state.ManualInputs.FirstOrDefault(x => x.FieldCode.Contains("WALK", StringComparison.OrdinalIgnoreCase))?.FieldCode ?? state.ManualInputs[0].FieldCode;
        RebuildCashQuickFields(state.ManualInputs);
        StockCountsGrid.ItemsSource = state.StockCounts;
        stateAllowsFinalise = state.CanFinalise;
        RefreshAccessState();
        _ = ShowNoSaleDayButtonAsync(state);
    }

    private int noSaleRevision;

    /// <summary>
    /// Decision 52: on a day with no sales file that is not finalised, the Owner and the Store Manager get N1's "Mark as no-sale
    /// day" (or "Withdraw" when it is marked) in the verdict band. A Viewer, a finalised day and a day with sales get nothing,
    /// except that a marked day whose mark no longer applies (sales or entries saved since, review M-1) gets "Withdraw".
    /// </summary>
    private async Task ShowNoSaleDayButtonAsync(DailyWorkflowPresentationState state)
    {
        var revision = ++noSaleRevision;
        NoSaleDayHost.Content = null;
        NoSaleDayHost.Visibility = Visibility.Collapsed;
        if (!ShowsNoSaleDayButton(state, access()) || NoSaleDayButtonFactory is not { } create || string.IsNullOrWhiteSpace(StoreCode)) return;
        try
        {
            var scope = SelectedScope();
            var button = await create(scope.StoreCode, scope.BusinessDate, RefreshAsync);
            if (revision != noSaleRevision || button is null) return;
            NoSaleDayHost.Content = button;
            NoSaleDayHost.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "DailyWorkflow.Workspace", "NO_SALE_DAY_BUTTON_FAILED", DesktopDiagnosticSeverity.Warning);
        }
    }

    /// <summary>The rule above, as a pure function (tests).</summary>
    internal static bool ShowsNoSaleDayButton(DailyWorkflowPresentationState state, DailyWorkflowWorkspaceAccess current) =>
        state.IsAvailable && (state.NoSalesFile || state.NoSaleMarkNoLongerApplies) && !state.IsLocked && current.CanImport;

    /// <summary>DS-08: the verdict band takes the tone of the state (soft fill, tone-coloured word) instead of one green box.</summary>
    private void ApplyVerdictTone(DailyWorkflowTone? tone)
    {
        var (fill, edge, word) = tone switch
        {
            DailyWorkflowTone.Healthy => ("SuccessSoft", "Success", "Success"),
            DailyWorkflowTone.Warning => ("WarningSoft", "Warning", "Warning"),
            DailyWorkflowTone.Critical => ("CriticalSoft", "Critical", "Critical"),
            _ => ("SurfaceSecondary", "Divider", "SecondaryText")
        };
        VerdictBand.SetResourceReference(Border.BackgroundProperty, fill);
        VerdictBand.SetResourceReference(Border.BorderBrushProperty, edge);
        WorkflowStatus.SetResourceReference(TextBlock.ForegroundProperty, word);
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

    public const string StaffTargetsNeedOwnerMessage = "Only the Owner can change staff targets.";

    public const string PackGenerationNeedsManagerMessage =
        "Owner or Store Manager permission is required to generate a pack. Saved packs are in Reports → Archive.";

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

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    // One labelled control per cash-book figure, in formula order, each showing what is
    // recorded today. Choosing one drives the existing dropdown, so the save path keeps
    // its mandatory reason and its audit trail exactly as they were.
    private void RebuildCashQuickFields(IReadOnlyList<DailyManualInput> inputs)
    {
        CashQuickFields.Children.Clear();
        CashQuickFields.Visibility = cashInputsMode ? Visibility.Visible : Visibility.Collapsed;
        if (!cashInputsMode) return;
        foreach (var field in CashEntryFields.Prominent(inputs, input => input.FieldCode))
        {
            var recorded = field.NumericValue?.ToString("N2", CultureInfo.CurrentCulture);
            // 1.12 U2 (design system T1/B7): styles, not sizes; the button style owns height and padding.
            var caption = new TextBlock { Text = field.DisplayName, TextWrapping = TextWrapping.Wrap };
            caption.SetResourceReference(StyleProperty, "CaptionText");
            var amount = new TextBlock
            {
                // Zero is a real answer and must not read the same as an empty field.
                Text = recorded ?? "Not entered",
                Margin = DesignTokens.Gap(null, "Space4", null, null)
            };
            amount.SetResourceReference(StyleProperty, recorded is null ? "BodyText" : "CardTitleText");
            var button = new Button
            {
                Content = new StackPanel { Children = { caption, amount } },
                Margin = DesignTokens.Gap(null, null, "Space8", "Space8"),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = field.FieldCode
            };
            button.SetResourceReference(MinWidthProperty, "InputMedium");
            System.Windows.Automation.AutomationProperties.SetName(
                button, $"{field.DisplayName}, {recorded ?? "not entered"}");
            button.Click += CashQuickField_Click;
            CashQuickFields.Children.Add(button);
        }
    }

    private void CashQuickField_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string fieldCode }) return;
        // Clear the previous field's amount and reason. Carrying them over means one
        // tap can save the last field's figure, with the last field's reason, against
        // a different cash-book line.
        if (!string.Equals(ManualFieldInput.SelectedValue as string, fieldCode, StringComparison.Ordinal))
        {
            ManualValueInput.Clear();
            ManualReasonInput.Clear();
        }
        ManualFieldInput.SelectedValue = fieldCode;
        ManualValueInput.Focus();
    }

    private async void SaveManualInput_Click(object sender, RoutedEventArgs e) => await SaveManualInputAsync();
    private async void SaveStockCount_Click(object sender, RoutedEventArgs e) => await SaveStockCountAsync();
    private async void SaveStaffTarget_Click(object sender, RoutedEventArgs e) => await SaveStaffTargetAsync();
    private async void FinaliseDay_Click(object sender, RoutedEventArgs e) { if (ConfirmationSheet.Show(this, "Finalise day", $"Lock {StoreCode} for {BusinessDate:dd MMM yyyy}? Changes require reopening.")) await FinaliseDayAsync(); }
    private async void ReopenDay_Click(object sender, RoutedEventArgs e) { if (ConfirmationSheet.Show(this, "Reopen day", $"Allow corrections for {StoreCode} on {BusinessDate:dd MMM yyyy}?")) await ReopenDayAsync(); }
    private async void GenerateDailyPack_Click(object sender, RoutedEventArgs e) => await GenerateDailyPackAsync();
    private async void GenerateCombinedDailyPack_Click(object sender, RoutedEventArgs e) => await GenerateCombinedDailyPackAsync();
    private void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(sender, BusinessDateInput) && IsInitialized) ApplyStaffTargetPeriodDefaults();
        InvalidateScope();
    }

    private void InvalidateScope()
    {
        ++refreshRevision;
        InvalidatePack();
        if (!IsInitialized) return;
        stateAllowsFinalise = false;
        WorkflowStatus.Text = "Refresh required";
        ApplyVerdictTone(null);
        WorkflowMessage.Text = "Store or business date changed. Refresh status before reviewing readiness or finalising. Unsaved values are retained; check them for the selected scope before saving.";
        SourceStatus.Text = string.Empty;
        InputStatus.Text = string.Empty;
        ManualInputsGrid.ItemsSource = null;
        StockCountsGrid.ItemsSource = null;
        ++noSaleRevision;
        NoSaleDayHost.Content = null;
        NoSaleDayHost.Visibility = Visibility.Collapsed;
        RefreshAccessState();
    }

    private async void ExportDailyPackExcel_Click(object sender, RoutedEventArgs e) => await ExportCurrentPackAsync("Excel");
    private async void ExportDailyPackPdf_Click(object sender, RoutedEventArgs e) => await ExportCurrentPackAsync("PDF");

    private async Task ExportCurrentPackAsync(string format)
    {
        if (packExportInProgress) return;
        if (currentPack is null || !PackMatchesCurrentScope()) { Publish("Generate the complete daily report pack for the selected store and business date before exporting."); return; }
        var excel = string.Equals(format, "Excel", StringComparison.Ordinal);
        var dialog = ExportSaveDialog.Create(excel ? ExportSaveDialog.ExcelFilter : ExportSaveDialog.PdfFilter,
            $"ETP_Daily_Report_Pack_{currentPack.DateTo:yyyyMMdd}.{(excel ? "xlsx" : "pdf")}");
        if (!ExportSaveDialog.ShowDialog(dialog, this)) return;
        using var progress = new OperationProgress(this,"Saving report pack");
        packExportInProgress = true;
        RefreshAccessState();
        try
        {
            var document = currentPack;
            await Reports.ExportStaging.WriteAsync(dialog.FileName, temporary => excel ? exportPackExcelAsync(temporary, document) : exportPackPdfAsync(temporary, document), progress.Token);
            Publish($"Complete {(excel ? "multi-sheet" : "paginated")} report pack saved to {dialog.FileName}");
            await recordAuditAsync(excel ? "ExportExcel" : "ExportPdf", "Succeeded", "Complete report pack exported");
        }
        catch (OperationCanceledException) { Publish("Export cancelled. The destination file was unchanged."); }
        catch (Exception exception) { PublishFailure(exception, "REPORT_PACK_EXPORT_FAILED", $"Report-pack {format} export failed", "This Windows account does not have application access."); }
        finally { packExportInProgress = false; RefreshAccessState(); }
    }

    private sealed record DailyPackBinding(DateOnly BusinessDate, string? StoreCode);
}
