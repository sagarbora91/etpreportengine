extern alias EtpApplication;

using System.IO;
using System.Windows;
using System.Windows.Controls;
using DatabaseConnectionStatus = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.DatabaseConnectionStatus;
using BootstrapDatabase = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.BootstrapDatabase;
using DatabaseLifecycleService = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.IDatabaseLifecycleService;
using AdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed record SettingsWorkspaceAccess(bool HasAssignedRole, bool CanAdminister);

public enum SettingsWorkspaceOperation
{
    ConnectionTest,
    DatabaseBootstrap,
    ProductConfigurationSaved
}

public sealed class SettingsConnectionPresentationChangedEventArgs(
    DesktopConnectionPresentationState state) : EventArgs
{
    public DesktopConnectionPresentationState State { get; } = state;
}

public partial class SettingsWorkspaceView : UserControl
{
    private readonly DesktopSettingsPresentationSession session;
    private readonly Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory;
    private readonly Func<string, AdministrationService> administrationServiceFactory;
    private readonly string migrationDirectory;
    private SettingsWorkspaceAccess access = new(false, false);

    public SettingsWorkspaceView(
        DesktopSettingsPresentationSession session,
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory,
        Func<string, AdministrationService> administrationServiceFactory,
        string migrationDirectory)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.databaseLifecycleServiceFactory = databaseLifecycleServiceFactory ?? throw new ArgumentNullException(nameof(databaseLifecycleServiceFactory));
        this.administrationServiceFactory = administrationServiceFactory ?? throw new ArgumentNullException(nameof(administrationServiceFactory));
        if (string.IsNullOrWhiteSpace(migrationDirectory))
            throw new ArgumentException("A migration directory is required.", nameof(migrationDirectory));
        this.migrationDirectory = Path.GetFullPath(migrationDirectory);

        InitializeComponent();
        ProductSettingsPanel.IsEnabled = false;
    }

    public event EventHandler<SettingsConnectionPresentationChangedEventArgs>? ConnectionPresentationChanged;
    public Func<SettingsWorkspaceOperation, bool, Task>? OperationCompletedAsync { get; set; }

    public string ConnectionStringText => ConnectionStringInput.Text;
    public string StatusText => ConnectionResult.Text;
    public bool ProductConfigurationEnabled => ProductSettingsPanel.IsEnabled;

    public void Initialize()
    {
        ConnectionStringInput.Text = session.LoadConnectionString();
        ProductSettingsPanel.IsEnabled = access.CanAdminister;
    }

    public void UpdateAccess(SettingsWorkspaceAccess currentAccess)
    {
        access = currentAccess ?? throw new ArgumentNullException(nameof(currentAccess));
        ProductSettingsPanel.IsEnabled = access.CanAdminister;
    }

    public async Task PrepareForDisplayAsync(bool loadProductConfiguration)
    {
        ProductSettingsPanel.IsEnabled = access.CanAdminister;
        if (loadProductConfiguration && access.CanAdminister)
            await LoadProductConfigurationAsync();
    }

    public async Task CheckConnectionAsync(bool showProgress)
    {
        if (showProgress) ConnectionResult.Text = "Testing…";
        var candidate = session.ValidateCandidate(ConnectionStringInput.Text);
        if (!candidate.IsValid)
        {
            ApplyPresentation(session.Current);
            return;
        }

        var health = await databaseLifecycleServiceFactory(candidate.ConnectionString!).CheckHealthAsync();
        var connected = health.Status == DatabaseConnectionStatus.Healthy;
        ApplyPresentation(session.CompleteHealthCheck(candidate, connected, health.Message, health.ServerVersion));
        ConnectionStringInput.Text = session.ConnectionString;
        if (OperationCompletedAsync is { } completed)
            await completed(SettingsWorkspaceOperation.ConnectionTest, connected);
    }

    public async Task BootstrapDatabaseAsync()
    {
        ConnectionResult.Text = "Creating/updating database…";
        try
        {
            RequireBootstrapAccess();
            var candidate = session.ValidateCandidate(ConnectionStringInput.Text);
            if (!candidate.IsValid) throw new InvalidOperationException(candidate.Error);
            var result = await databaseLifecycleServiceFactory(candidate.ConnectionString!)
                .BootstrapAsync(new BootstrapDatabase(migrationDirectory));
            var message = $"Database ready. Applied migrations: {(result.AppliedMigrations.Count == 0 ? "none" : string.Join(", ", result.AppliedMigrations))}.";
            ApplyPresentation(session.CompleteBootstrap(candidate, message));
            ConnectionStringInput.Text = session.ConnectionString;
            if (OperationCompletedAsync is { } completed)
                await completed(SettingsWorkspaceOperation.DatabaseBootstrap, true);
        }
        catch (Exception exception)
        {
            ConnectionResult.Text = $"Database setup failed: {exception.Message}";
        }
    }

    public async Task LoadProductConfigurationAsync()
    {
        ProductSettingsPanel.IsEnabled = access.CanAdminister;
        if (!access.CanAdminister) return;
        try
        {
            var dashboard = await administrationServiceFactory(session.ConnectionString).LoadAsync("Store");
            ApplyProductSettings(session.ShowProductSettings(dashboard.ProductConfiguration));
        }
        catch (Exception exception)
        {
            ConnectionResult.Text = FriendlyError(exception);
        }
    }

    public async Task SaveProductConfigurationAsync()
    {
        try
        {
            RequireOwnerAccess();
            var settings = DesktopSettingsPresentationSession.CreateProductConfiguration(
                DocumentRepositoryInput.Text, ShareFolderInput.Text, OcrHelperInput.Text, OcrModelInput.Text,
                SmtpHostInput.Text, SmtpPortInput.Text, SmtpFromInput.Text, MaximumAttachmentInput.Text,
                ProductSettingsReasonInput.Text);
            await administrationServiceFactory(session.ConnectionString).SaveProductConfigurationAsync(settings);
            ProductSettingsReasonInput.Clear();
            ConnectionResult.Text = "Product integration settings saved and audited.";
            await LoadProductConfigurationAsync();
            if (OperationCompletedAsync is { } completed)
                await completed(SettingsWorkspaceOperation.ProductConfigurationSaved, true);
        }
        catch (Exception exception)
        {
            ConnectionResult.Text = FriendlyError(exception);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e) =>
        await CheckConnectionAsync(true);

    private async void BootstrapDatabase_Click(object sender, RoutedEventArgs e) =>
        await BootstrapDatabaseAsync();

    private async void SaveProductSettings_Click(object sender, RoutedEventArgs e) =>
        await SaveProductConfigurationAsync();

    private void ApplyPresentation(DesktopConnectionPresentationState state)
    {
        ConnectionResult.Text = state.ConnectionResult;
        ConnectionPresentationChanged?.Invoke(this, new SettingsConnectionPresentationChangedEventArgs(state));
    }

    private void ApplyProductSettings(DesktopProductSettingsPresentation settings)
    {
        DocumentRepositoryInput.Text = settings.DocumentRepositoryPath;
        ShareFolderInput.Text = settings.ShareFolderPath;
        OcrHelperInput.Text = settings.OcrHelperPath;
        OcrModelInput.Text = settings.OcrModelPath;
        SmtpHostInput.Text = settings.SmtpHost;
        SmtpPortInput.Text = settings.SmtpPort;
        SmtpFromInput.Text = settings.SmtpFromAddress;
        MaximumAttachmentInput.Text = settings.MaximumAttachmentMb;
    }

    private void RequireBootstrapAccess()
    {
        if (access.HasAssignedRole && !access.CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!access.CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        InvalidOperationException or ArgumentException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };
}
