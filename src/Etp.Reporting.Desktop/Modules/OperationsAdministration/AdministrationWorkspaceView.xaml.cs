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
        if (taskId is "stores" or "tender-rules")
        {
            var type = taskId == "stores" ? "Store" : "Tender";
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
            ControlledMastersGrid.ItemsSource = state.Masters;
            ApplicationUsersGrid.ItemsSource = state.Users;
            KpiCatalogueGrid.ItemsSource = state.Kpis;
            ProductHealthGrid.ItemsSource = state.ProductHealth;
            AdministrationStatus.Text = state.Status;
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

    private async void SupportPackage_Click(object sender, RoutedEventArgs e)
    {
        SupportPackageButton.IsEnabled = false;
        DatabaseRecoveryStatus.Text = "Creating the support package…";
        try
        {
            // Every other handler in this view re-checks the role rather than relying on
            // the screen being Owner-only. This one was the exception.
            RequireOwnerAccess();
            var result = await PowerShellOperationsService.RunAsync("new-etp-support-package.ps1", connectionStringProvider());
            DatabaseRecoveryStatus.Text = result.Message;
        }
        catch (Exception ex)
        {
            DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "SUPPORT_PACKAGE_FAILED");
            DatabaseRecoveryStatus.Text = "The support package could not be created. " + DesktopFriendlyError.Describe(ex, "Owner permission is required.");
        }
        finally { SupportPackageButton.IsEnabled = true; }
    }

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
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "USER_ACCESS_SAVE_FAILED"); AdministrationStatus.Text = $"User access was not saved: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; return false; }
        finally { EndSave(); }
    }

    private void RequireOwnerAccess()
    {
        if (!access.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value from the list.");
}
