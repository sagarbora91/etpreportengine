extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using IAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class AdministrationWorkspaceView : UserControl
{
    private readonly OperationsAdministrationPresentationSession session;
    private readonly Func<string> connectionStringProvider;
    private readonly Func<string, IAdministrationService> serviceFactory;
    private OperationsAdministrationWorkspaceAccess access = new(false, false, false);
    private int refreshRevision;

    public AdministrationWorkspaceView(
        OperationsAdministrationPresentationSession session,
        Func<string> connectionStringProvider,
        Func<string, IAdministrationService> serviceFactory)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        InitializeComponent();
        userAccessGuidance = UserAccessGuidance.Text;
        UserActiveInput.Checked += (_, _) => UpdateSaveUserAccessState();
        UserActiveInput.Unchecked += (_, _) => UpdateSaveUserAccessState();
        MasterCodeInput.TextChanged += (_, _) => UpdateMasterActiveState();
    }

    // Service interim (0048). AW330 is the Service Centre, not a shop: the database refuses to
    // make it active (51900), so Settings > Stores turns the Active toggle off for it before
    // the Owner can try. Retail codes keep the toggle exactly as before.
    private IReadOnlySet<string> serviceStoreCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private object? masterActiveToolTip;
    private bool masterActiveToolTipCaptured;
    private bool masterActiveBeforeLock = true;

    /// <summary>True when the Active toggle is off because the typed code is a Service Centre store.</summary>
    public bool MasterActiveLockedForServiceStore { get; private set; }
    public bool CanToggleMasterActive => MasterActiveInput.IsEnabled;
    public bool MasterActiveChecked => MasterActiveInput.IsChecked == true;
    public IReadOnlyList<string> MasterDisplayNames =>
        ControlledMastersGrid.Items.OfType<ControlledMasterPresentation>().Select(row => row.DisplayName).ToArray();

    /// <summary>Types a store code into the editor, as the Owner would (tests use it).</summary>
    public void TypeMasterCode(string code) => MasterCodeInput.Text = code;

    private void UpdateMasterActiveState()
    {
        if (!masterActiveToolTipCaptured) { masterActiveToolTip = MasterActiveInput.ToolTip; masterActiveToolTipCaptured = true; }
        var locked = serviceStoreCodes.Contains(MasterCodeInput.Text.Trim());
        var wasLocked = MasterActiveLockedForServiceStore;
        MasterActiveLockedForServiceStore = locked;
        // Remember the Owner's tick when AW330 locks the box and give it back when the code
        // stops being AW330, so the Retail store saved next is never switched off by accident.
        if (locked && !wasLocked) masterActiveBeforeLock = MasterActiveInput.IsChecked == true;
        if (locked) MasterActiveInput.IsChecked = false;
        else if (wasLocked) MasterActiveInput.IsChecked = masterActiveBeforeLock;
        MasterActiveInput.IsEnabled = !locked;
        MasterActiveInput.ToolTip = locked ? Etp.Reporting.Infrastructure.SqlServer.ServiceCentreStores.ActiveToggleLockedToolTip : masterActiveToolTip;
    }

    private readonly string userAccessGuidance;

    /// <summary>
    /// Finding A, 2 Oct 2026. True when the last refresh found that this Owner's SQL login
    /// cannot finish a user change (it is not elevated), so the Users task says why before
    /// anything is typed and Save is off for any change that needs the server-level grant.
    /// </summary>
    public bool UserAccessNeedsElevation { get; private set; }
    public bool CanSaveUserAccess => SaveUserAccessButton.IsEnabled;
    public string UserAccessGuidanceText => UserAccessGuidance.Text;

    /// <summary>
    /// Security review 1.9.3, F5. Only a change that leaves the account active - adding,
    /// reactivating, promoting or changing the role of a user - ends with a GRANT or REVOKE of
    /// ALTER ANY LOGIN that SQL Server is known to refuse unelevated. A deactivation of an
    /// account without a login (the retired-PC accounts the restore helper asks the Owner to
    /// deactivate) makes no server-level change at all, so it is never blocked here. A
    /// deactivation of an account that does have a login still needs the grant; SQL Server then
    /// refuses it, nothing is changed, and the save says why.
    /// </summary>
    private bool DraftNeedsServerGrant => UserActiveInput.IsChecked == true;

    private void ApplyUserAccessReadiness(bool needsElevation)
    {
        UserAccessNeedsElevation = needsElevation;
        UserAccessGuidance.Text = needsElevation
            ? userAccessGuidance + " " + DesktopFriendlyError.UserAccessNeedsElevationMessage + " " + DesktopFriendlyError.UserDeactivationWorksUnelevatedMessage
            : userAccessGuidance;
        UpdateSaveUserAccessState();
    }

    private void UpdateSaveUserAccessState()
    {
        var blocked = UserAccessNeedsElevation && DraftNeedsServerGrant;
        SaveUserAccessButton.IsEnabled = !blocked;
        SaveUserAccessButton.ToolTip = blocked ? DesktopFriendlyError.UserAccessNeedsElevationMessage : null;
    }

    public Func<Task>? AccessChangedAsync { get; set; }
    public string StatusText => AdministrationStatus.Text;

    /// <summary>R4. The Database and recovery lines currently shown, for assertions.</summary>
    public IReadOnlyList<DatabaseRecoveryLine> RecoveryLines { get; private set; } = [];
    public string RecoveryStatusText => DatabaseRecoveryStatus.Text;

    /// <summary>Overridable so a test can supply health without a live SQL instance.</summary>
    internal Func<Task<Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealth>>? HealthLoader { get; set; }
    public int MasterRowCount => ControlledMastersGrid.Items.Count;
    public int UserRowCount => ApplicationUsersGrid.Items.Count;
    public void UpdateAccess(OperationsAdministrationWorkspaceAccess value) => access = value;

    public void SelectTask(string taskId)
    {
        if (taskId == "stores")
        {
            var type = "Store";
            MasterTypeInput.SelectedItem = MasterTypeInput.Items.OfType<ComboBoxItem>().First(x => x.Content?.ToString() == type);
        }
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var revision = ++refreshRevision; var type = SelectedContent(MasterTypeInput);
        try
        {
            RequireOwnerAccess();
            var dashboard = await Service.LoadAsync(type);
            if (revision != refreshRevision || type != SelectedContent(MasterTypeInput)) return;
            var state = session.Capture(dashboard);
            ControlledMastersGrid.ItemsSource = state.MasterRows;
            serviceStoreCodes = state.ServiceStoreCodes;
            UpdateMasterActiveState();
            ApplicationUsersGrid.ItemsSource = state.Users;
            KpiCatalogueGrid.ItemsSource = state.Kpis;
            ProductHealthGrid.ItemsSource = state.ProductHealth;
            AdministrationStatus.Text = state.Status;
            ApplyUserAccessReadiness(state.UserAccessChangesNeedElevation);
            await RefreshDatabaseRecoveryAsync(revision);
        }
        catch (Exception ex) { if (revision != refreshRevision) return; DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "ADMINISTRATION_REFRESH_FAILED"); AdministrationStatus.Text = $"Master administration could not be loaded: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; }
    }

    private IAdministrationService Service => serviceFactory(connectionStringProvider());
    private async void MasterType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RetainMasterType();
        if (!IsLoaded || !access.CanAdminister) return;
        await RefreshAsync();
    }

    private async void SaveMaster_Click(object sender, RoutedEventArgs e) => await SaveMasterDraftAsync();
    // R4. Owner-only, like the rest of this screen: RefreshAsync has already called
    // RequireOwnerAccess before this runs.
    private async Task RefreshDatabaseRecoveryAsync(int revision)
    {
        try
        {
            var loader = HealthLoader
                ?? (() => new Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealthRepository(connectionStringProvider()).LoadAsync(default));
            var health = await loader();
            if (revision != refreshRevision) return;
            RecoveryLines = DatabaseRecoveryPresentation.Lines(
                health, Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealthThresholds.Default, DateTime.UtcNow);
            DatabaseRecoveryGrid.ItemsSource = RecoveryLines;
            // Warning rows carry their severity in Status, so counting only Missing and
            // Stale let the block announce that everything was current while a Critical
            // row sat on screen beneath the sentence.
            var unresolved = RecoveryLines.Count(line =>
                line.Item == "Warning"
                || line.Status == DatabaseRecoveryPresentation.Missing
                || line.Status.StartsWith("Stale", StringComparison.Ordinal));
            DatabaseRecoveryStatus.Text = unresolved == 0
                ? "Backup and recovery evidence is present and current."
                : $"{unresolved} item(s) need attention. Missing or stale evidence is never reported as healthy.";
        }
        catch (Exception ex)
        {
            if (revision != refreshRevision) return;
            DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "DATABASE_RECOVERY_LOAD_FAILED");
            RecoveryLines = DatabaseRecoveryPresentation.Lines(null, Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealthThresholds.Default, DateTime.UtcNow);
            DatabaseRecoveryGrid.ItemsSource = RecoveryLines;
            DatabaseRecoveryStatus.Text = "Database and recovery health could not be read. " + DesktopFriendlyError.Describe(ex, "Owner permission is required.");
        }
    }

    public Func<Task>? StoresChangedAsync { get; set; }

    public async Task<bool> SaveMasterDraftAsync()
    {
        if (!BeginSave()) return false;
        try
        {
            RequireOwnerAccess();
            await Service.SaveMasterAsync(OperationsAdministrationPresentationSession.CreateMasterCommand(
                SelectedContent(MasterTypeInput), MasterCodeInput.Text, MasterNameInput.Text,
                SelectedContent(MasterApprovalInput), MasterActiveInput.IsChecked == true, MasterReasonInput.Text));
            MasterCodeInput.Clear(); MasterNameInput.Clear(); MasterReasonInput.Clear();
            masterBaselines[editingMaster] = CaptureMaster();
            await RefreshAsync();
            if (StoresChangedAsync is not null) await StoresChangedAsync();
            return true;
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "MASTER_VALUE_SAVE_FAILED"); AdministrationStatus.Text = $"Master value was not saved: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; return false; }
        finally { EndSave(); }
    }

    private async void SaveUserAccess_Click(object sender, RoutedEventArgs e) => await SaveUserDraftAsync();
    public async Task<bool> SaveUserDraftAsync()
    {
        if (!BeginSave()) return false;
        try
        {
            RequireOwnerAccess();
            // The unsaved-drafts prompt can reach here with the button off. Say why rather
            // than send a change SQL Server is known to refuse; the draft stays as typed. A
            // deactivation goes on (F5): see DraftNeedsServerGrant.
            if (UserAccessNeedsElevation && DraftNeedsServerGrant)
            {
                AdministrationStatus.Text = $"User access was not saved: {DesktopFriendlyError.UserAccessNeedsElevationMessage}";
                return false;
            }
            await Service.SaveUserAsync(OperationsAdministrationPresentationSession.CreateUserCommand(
                UserIdentityInput.Text, UserDisplayNameInput.Text, SelectedContent(UserRoleInput),
                UserActiveInput.IsChecked == true, UserReasonInput.Text));
            UserIdentityInput.Clear(); UserDisplayNameInput.Clear(); UserReasonInput.Clear();
            userBaseline = CaptureUser();
            await RefreshAsync();
            if (AccessChangedAsync is not null)
            {
                try { await AccessChangedAsync(); }
                catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "SAVED_ACCESS_REFRESH_FAILED"); AdministrationStatus.Text = "User access was saved. The access display could not be refreshed; reopen ETP to refresh permissions."; }
            }
            return true;
        }
        catch (Exception ex)
        {
            DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "USER_ACCESS_SAVE_FAILED", UserAccessFailureSeverity(ex), operation: "User access save failed");
            var reason = DesktopFriendlyError.DescribeUserAccessFailure(ex);
            // The check on refresh could not see the refusal coming; keep the screen honest now.
            if (reason == DesktopFriendlyError.UserAccessNeedsElevationMessage) ApplyUserAccessReadiness(true);
            AdministrationStatus.Text = $"User access was not saved: {reason}";
            return false;
        }
        finally { EndSave(); }
    }

    // IE-RT-04 (1.9.9): a field left empty or typed in the wrong form (blank identity, name or reason, an identity
    // that is not DOMAIN\User, no role) is refused by input validation with an ArgumentException. The user sees the
    // reason; the log keeps it as a Warning so it does not read as a fault among real Errors.
    internal static DesktopDiagnosticSeverity UserAccessFailureSeverity(Exception exception) =>
        exception is ArgumentException ? DesktopDiagnosticSeverity.Warning : DesktopDiagnosticSeverity.Error;

    private void RequireOwnerAccess()
    {
        if (!access.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value from the list.");
}
