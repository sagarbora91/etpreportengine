extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Service;
using IAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;
using ServiceCentre = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentre;
using ServiceCentreAdministration = EtpApplication::Etp.Reporting.Application.Stores.IServiceCentreAdministration;
using ServiceCentreRules = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentreRules;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public partial class AdministrationWorkspaceView : UserControl
{
    private readonly OperationsAdministrationPresentationSession session;
    private readonly Func<IAdministrationService> serviceFactory;
    private OperationsAdministrationWorkspaceAccess access = new(false, false, false);
    private int refreshRevision;

    public AdministrationWorkspaceView(
        OperationsAdministrationPresentationSession session,
        Func<IAdministrationService> serviceFactory,
        Func<Task<Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealth>>? healthLoader = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));
        HealthLoader = healthLoader;
        InitializeComponent();
        userAccessGuidance = UserAccessGuidance.Text;
        UserActiveInput.Checked += (_, _) => UpdateSaveUserAccessState();
        UserActiveInput.Unchecked += (_, _) => UpdateSaveUserAccessState();
        MasterCodeInput.TextChanged += (_, _) => UpdateMasterActiveState();
        MasterServiceInput.Checked += (_, _) => UpdateMasterActiveState();
        MasterServiceInput.Unchecked += (_, _) => UpdateMasterActiveState();
    }

    // Service interim (0048). A Service Centre store is not a shop: the database refuses to
    // make it active (51900, 51972), so Settings > Stores turns the Active toggle off for it before
    // the Owner can try. Retail codes keep the toggle exactly as before.
    // 1.12 A4: the Owner can add a Service Centre here: tick "Service Centre" and give its job-number
    // prefix (JO and the store code is offered). An existing Service Centre's code ticks the box and
    // fills its prefix; an existing shop store's code leaves the box off (a store never changes kind).
    private IReadOnlySet<string> serviceStoreCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<string> shopStoreCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ServiceCentre> serviceCentres = [];
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

    /// <summary>Ticks or clears "Service Centre", as the Owner would (tests use it).</summary>
    public void SetMasterServiceCentre(bool value) => MasterServiceInput.IsChecked = value;

    /// <summary>Types the job-number prefix, as the Owner would (tests use it).</summary>
    public void TypeMasterPrefix(string prefix) => MasterPrefixInput.Text = prefix;

    /// <summary>Types the store name and the reason, as the Owner would (tests use it).</summary>
    public void TypeMasterNameAndReason(string name, string reason) { MasterNameInput.Text = name; MasterReasonInput.Text = reason; }

    public bool MasterServiceChecked => MasterServiceInput.IsChecked == true;
    public bool CanToggleMasterService => MasterServiceInput.IsEnabled;
    public bool CanTypeMasterPrefix => MasterPrefixInput.IsEnabled;
    public string MasterPrefixText => MasterPrefixInput.Text;

    /// <summary>The Service Centres the editor knows (from the catalogue at refresh); a test may set them.</summary>
    internal IReadOnlyList<ServiceCentre> ServiceCentres { get => serviceCentres; set { serviceCentres = value ?? []; UpdateMasterActiveState(); } }

    /// <summary>The Service Centre save for this view; null uses the composition root's (a test supplies its own).</summary>
    internal Func<ServiceCentreAdministration>? ServiceCentreAdministrationFactory { get; set; }

    private bool updatingServiceState;
    // True while "Service Centre" is ticked only because the typed code is an existing Service Centre; the tick goes
    // away again when the code changes, so a Retail code typed next is never saved as a Service Centre by accident.
    private bool serviceTickedByCode;

    private void UpdateServiceCentreState()
    {
        var code = MasterCodeInput.Text.Trim();
        var existingService = serviceStoreCodes.Contains(code);
        var existingShop = !existingService && shopStoreCodes.Contains(code);
        if (existingService)
        {
            if (MasterServiceInput.IsChecked != true) { MasterServiceInput.IsChecked = true; serviceTickedByCode = true; MasterPrefixInput.Clear(); }
        }
        else if (serviceTickedByCode || existingShop)
        {
            if (serviceTickedByCode) MasterPrefixInput.Clear();
            MasterServiceInput.IsChecked = false;
            serviceTickedByCode = false;
        }
        MasterServiceInput.IsEnabled = !existingService && !existingShop;
        var service = MasterServiceInput.IsChecked == true;
        MasterPrefixInput.IsEnabled = service;
        if (!service) return;
        if (MasterPrefixInput.Text.Trim().Length == 0)
            MasterPrefixInput.Text = serviceCentres.FirstOrDefault(centre => string.Equals(centre.StoreCode, code, StringComparison.OrdinalIgnoreCase))?.JobNumberPrefix
                ?? ServiceCentreRules.SuggestedPrefix(code);
    }

    private void UpdateMasterActiveState()
    {
        if (updatingServiceState) return;
        updatingServiceState = true;
        try { UpdateServiceCentreState(); }
        finally { updatingServiceState = false; }
        if (!masterActiveToolTipCaptured) { masterActiveToolTip = MasterActiveInput.ToolTip; masterActiveToolTipCaptured = true; }
        var locked = serviceStoreCodes.Contains(MasterCodeInput.Text.Trim()) || MasterServiceInput.IsChecked == true;
        var wasLocked = MasterActiveLockedForServiceStore;
        MasterActiveLockedForServiceStore = locked;
        // Remember the Owner's tick when a Service Centre code locks the box and give it back when the
        // code stops being one, so the Retail store saved next is never switched off by accident.
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

    /// <summary>The Database and recovery read, built by the composition root (1.12 A1); a test supplies its own.</summary>
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
            shopStoreCodes = state.ShopStoreCodes;
            serviceCentres = await ServiceCentreServices.LoadOrEmptyAsync();
            if (revision != refreshRevision) return;
            UpdateMasterActiveState();
            ApplicationUsersGrid.ItemsSource = state.Users;
            KpiCatalogueGrid.ItemsSource = state.Kpis;
            ProductHealthGrid.ItemsSource = state.ProductHealth;
            AdministrationStatus.Text = state.Status;
            ApplyUserAccessReadiness(state.UserAccessChangesNeedElevation);
            await RefreshDatabaseRecoveryAsync(revision);
        }
        catch (Exception ex) { if (revision != refreshRevision) return; var reference = DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "ADMINISTRATION_REFRESH_FAILED"); AdministrationStatus.Text = DesktopDiagnostics.WithReference($"Master administration could not be loaded: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}", reference); }
    }

    private IAdministrationService Service => serviceFactory();
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
                ?? (() => throw new InvalidOperationException("Database and recovery health is not available on this screen."));
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
            var reference = DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "DATABASE_RECOVERY_LOAD_FAILED");
            RecoveryLines = DatabaseRecoveryPresentation.Lines(null, Etp.Reporting.Infrastructure.SqlServer.DatabaseOperationalHealthThresholds.Default, DateTime.UtcNow);
            DatabaseRecoveryGrid.ItemsSource = RecoveryLines;
            DatabaseRecoveryStatus.Text = DesktopDiagnostics.WithReference("Database and recovery health could not be read. " + DesktopFriendlyError.Describe(ex, "Owner permission is required."), reference);
        }
    }

    public Func<Task>? StoresChangedAsync { get; set; }

    public async Task<bool> SaveMasterDraftAsync()
    {
        if (!BeginSave()) return false;
        try
        {
            RequireOwnerAccess();
            if (MasterServiceInput.IsChecked == true)
            {
                if (!await SaveServiceCentreDraftAsync()) return false;
            }
            else
                await Service.SaveMasterAsync(OperationsAdministrationPresentationSession.CreateMasterCommand(
                    SelectedContent(MasterTypeInput), MasterCodeInput.Text, MasterNameInput.Text,
                    SelectedContent(MasterApprovalInput), MasterActiveInput.IsChecked == true, MasterReasonInput.Text));
            MasterCodeInput.Clear(); MasterNameInput.Clear(); MasterReasonInput.Clear();
            MasterServiceInput.IsChecked = false; MasterPrefixInput.Clear(); serviceTickedByCode = false;
            masterBaselines[editingMaster] = CaptureMaster();
            await RefreshAsync();
            if (StoresChangedAsync is not null) await StoresChangedAsync();
            return true;
        }
        catch (Exception ex) { var reference = DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "MASTER_VALUE_SAVE_FAILED"); AdministrationStatus.Text = DesktopDiagnostics.WithReference($"Master value was not saved: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}", reference); return false; }
        finally { EndSave(); }
    }

    /// <summary>
    /// 1.12 A4: saves the editor as a Service Centre store (Owner). A missing or malformed prefix, or one that overlaps
    /// another centre's, is refused here in plain words before anything is sent; the database refuses the same (0067).
    /// </summary>
    private async Task<bool> SaveServiceCentreDraftAsync()
    {
        var code = MasterCodeInput.Text.Trim();
        var prefix = MasterPrefixInput.Text.Trim().ToUpperInvariant();
        var others = serviceCentres.Where(centre => !string.Equals(centre.StoreCode, code, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (OperationsAdministrationPresentationSession.ServiceCentreDraftProblem(code, MasterNameInput.Text, prefix, MasterReasonInput.Text, others) is { } problem)
        {
            AdministrationStatus.Text = "Service Centre was not saved: " + problem;
            return false;
        }
        if ((ServiceCentreAdministrationFactory ?? ServiceCentreServices.Administration) is not { } administration)
        {
            AdministrationStatus.Text = "Service Centre was not saved: adding a Service Centre is not available in this window. Reopen ETP and try again.";
            return false;
        }
        await administration().SaveServiceCentreAsync(code, MasterNameInput.Text.Trim(), prefix, MasterReasonInput.Text.Trim());
        return true;
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
                catch (Exception ex) { var reference = DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "SAVED_ACCESS_REFRESH_FAILED"); AdministrationStatus.Text = DesktopDiagnostics.WithReference("User access was saved. The access display could not be refreshed; reopen ETP to refresh permissions.", reference); }
            }
            return true;
        }
        catch (Exception ex)
        {
            var reference = DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "USER_ACCESS_SAVE_FAILED", UserAccessFailureSeverity(ex), operation: "User access save failed");
            var reason = DesktopFriendlyError.DescribeUserAccessFailure(ex);
            // The check on refresh could not see the refusal coming; keep the screen honest now.
            if (reason == DesktopFriendlyError.UserAccessNeedsElevationMessage) ApplyUserAccessReadiness(true);
            AdministrationStatus.Text = DesktopDiagnostics.WithReference($"User access was not saved: {reason}", reference);
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
