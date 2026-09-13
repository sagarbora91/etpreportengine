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
