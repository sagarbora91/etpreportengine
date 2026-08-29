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

    public async Task RefreshAsync()
    {
        try
        {
            RequireOwnerAccess();
            var state = session.Capture(await Service.LoadAsync(SelectedContent(MasterTypeInput)));
            ControlledMastersGrid.ItemsSource = state.Masters;
            ApplicationUsersGrid.ItemsSource = state.Users;
            KpiCatalogueGrid.ItemsSource = state.Kpis;
            ProductHealthGrid.ItemsSource = state.ProductHealth;
            AdministrationStatus.Text = state.Status;
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "ADMINISTRATION_REFRESH_FAILED"); AdministrationStatus.Text = $"Master administration could not be loaded: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; }
    }

    private IAdministrationService Service => serviceFactory(connectionStringProvider());
    private async void MasterType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !access.CanAdminister) return;
        await RefreshAsync();
    }

    private async void SaveMaster_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await Service.SaveMasterAsync(OperationsAdministrationPresentationSession.CreateMasterCommand(
                SelectedContent(MasterTypeInput), MasterCodeInput.Text, MasterNameInput.Text,
                SelectedContent(MasterApprovalInput), MasterActiveInput.IsChecked == true, MasterReasonInput.Text));
            MasterCodeInput.Clear(); MasterNameInput.Clear(); MasterReasonInput.Clear();
            await RefreshAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "MASTER_VALUE_SAVE_FAILED"); AdministrationStatus.Text = $"Master value was not saved: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; }
    }

    private async void SaveUserAccess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await Service.SaveUserAsync(OperationsAdministrationPresentationSession.CreateUserCommand(
                UserIdentityInput.Text, UserDisplayNameInput.Text, SelectedContent(UserRoleInput),
                UserActiveInput.IsChecked == true, UserReasonInput.Text));
            UserIdentityInput.Clear(); UserDisplayNameInput.Clear(); UserReasonInput.Clear();
            await RefreshAsync();
            if (AccessChangedAsync is not null) await AccessChangedAsync();
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "OperationsAdministration.Administration", "USER_ACCESS_SAVE_FAILED"); AdministrationStatus.Text = $"User access was not saved: {DesktopFriendlyError.Describe(ex, "Owner permission is required.")}"; }
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
