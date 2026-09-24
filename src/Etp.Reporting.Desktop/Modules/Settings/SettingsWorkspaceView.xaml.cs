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
    private int connectionCheckRevision;

    public SettingsWorkspaceView(
        DesktopSettingsPresentationSession session,
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory,
        Func<string, AdministrationService> administrationServiceFactory,
        string migrationDirectory,
        Func<string, EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService>? accountingServiceFactory = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.databaseLifecycleServiceFactory = databaseLifecycleServiceFactory ?? throw new ArgumentNullException(nameof(databaseLifecycleServiceFactory));
        this.administrationServiceFactory = administrationServiceFactory ?? throw new ArgumentNullException(nameof(administrationServiceFactory));
        if (string.IsNullOrWhiteSpace(migrationDirectory))
            throw new ArgumentException("A migration directory is required.", nameof(migrationDirectory));
        this.migrationDirectory = Path.GetFullPath(migrationDirectory);

        InitializeComponent();
        InitializeDataTruthMasters();
        if (accountingServiceFactory is not null)
        {
            tallySettings = new(() => session.ConnectionString, () => access.CanAdminister, accountingServiceFactory);
            ProductSettingsPanel.Children.Add(tallySettings);
        }
        ConnectionStringInput.TextChanged += (_, _) => ++connectionCheckRevision;
        ProductSettingsPanel.IsEnabled = false;
    }

    public event EventHandler<SettingsConnectionPresentationChangedEventArgs>? ConnectionPresentationChanged;
    public Func<SettingsWorkspaceOperation, bool, Task>? OperationCompletedAsync { get; set; }
    public Func<bool>? CanChangeDatabase { get; set; }

    public string ConnectionStringText => ConnectionStringInput.Text;
    public string StatusText => ConnectionResult.Text;
    public bool ProductConfigurationEnabled => ProductSettingsPanel.IsEnabled;

    private readonly TallyDestinationSettingsView? tallySettings;
    private bool integrationsLoaded;
    private bool productBusy;
    private readonly WorkspaceOperationGate databaseOperation = new();
    private string[]? savedProduct;
    private TextBox[] ProductFields => [DocumentRepositoryInput, ShareFolderInput, SmtpHostInput, SmtpPortInput, SmtpFromInput, MaximumAttachmentInput, ProductSettingsReasonInput];
    private bool HasIntegrationFieldsDraft => savedProduct is not null && !ProductFields.Select(input => input.Text).SequenceEqual(savedProduct);
    public bool HasProductDraft => tallySettings?.HasDraft == true || HasIntegrationFieldsDraft;
    public bool IsBusy => tallySettings?.IsBusy == true || productBusy || databaseOperation.IsBusy;
    public void DiscardProductDraft() { tallySettings?.DiscardDraft(); if (savedProduct is null) return; for (var i = 0; i < ProductFields.Length; i++) ProductFields[i].Text = savedProduct[i]; }
    public void SelectIntegrationTask(string id)
    {
        foreach (var input in new[] { SmtpHostInput, SmtpPortInput, SmtpFromInput, MaximumAttachmentInput })
            input.Visibility = id == "sharing" ? Visibility.Visible : Visibility.Collapsed;
        if (!integrationsLoaded) _ = PrepareForDisplayAsync(true);
    }

    public void Initialize()
    {
        ConnectionStringInput.Text = session.LoadConnectionString();
        ProductSettingsPanel.IsEnabled = access.CanAdminister && integrationsLoaded && !productBusy;
    }

    public void UpdateAccess(SettingsWorkspaceAccess currentAccess)
    {
        access = currentAccess ?? throw new ArgumentNullException(nameof(currentAccess));
        ExportRecoveryKeysButton.IsEnabled = access.CanAdminister;
        ProductSettingsPanel.IsEnabled = access.CanAdminister && integrationsLoaded && !productBusy;
        foreach (var masters in DataTruthMastersHost.Children.OfType<EveningMastersView>()) masters.RefreshAccessState();
    }

    public async Task PrepareForDisplayAsync(bool loadProductConfiguration)
    {
        if (productBusy) return;
        if (HasProductDraft) { ConnectionResult.Text = "Unsaved integration settings are retained. Save or discard them before reloading."; return; }
        if (loadProductConfiguration && access.CanAdminister)
        {
            await LoadProductConfigurationAsync();
            if (tallySettings is not null) await tallySettings.LoadAsync();
        }
    }

    public async Task CheckConnectionAsync(bool showProgress)
    {
        if (IsBusy || HasProductDraft) { ConnectionResult.Text = "Finish the current operation and save or discard integration edits before changing the database connection."; return; }
        var revision = ++connectionCheckRevision;
        if (showProgress) ConnectionResult.Text = "Testingâ€¦";
        var candidate = session.ValidateCandidate(ConnectionStringInput.Text);
        if (!candidate.IsValid)
        {
            ApplyPresentation(session.Current);
            return;
        }
        if (!AllowConnectionCandidate(candidate.ConnectionString!)) return;

        try
        {
            var health = await databaseLifecycleServiceFactory(candidate.ConnectionString!).CheckHealthAsync();
            if (revision != connectionCheckRevision) return;
            var connected = health.Status == DatabaseConnectionStatus.Healthy;
            if (!connected)
                DesktopDiagnostics.Record(null, "Settings.Workspace", "DATABASE_HEALTH_CHECK_FAILED",
                    DesktopDiagnosticSeverity.Warning);
            ApplyPresentation(session.CompleteHealthCheck(candidate, connected, health.Message, health.ServerVersion));
            ConnectionStringInput.Text = session.ConnectionString;
            await NotifyCompletedAsync(SettingsWorkspaceOperation.ConnectionTest, connected);
        }
        catch (Exception exception)
        {
            if (revision != connectionCheckRevision) return;
            DesktopDiagnostics.Record(exception, "Settings.Workspace", "DATABASE_HEALTH_CHECK_EXCEPTION");
            var message = DesktopFriendlyError.Describe(exception,
                "Check the SQL Server settings and try again.");
            ApplyPresentation(session.CompleteHealthCheck(candidate, false, message, null));
            await NotifyCompletedAsync(SettingsWorkspaceOperation.ConnectionTest, false);
        }
    }

    public async Task BootstrapDatabaseAsync()
    {
        if (productBusy || HasProductDraft) { ConnectionResult.Text = "Save or discard integration edits before updating the database."; return; }
        using var operation = databaseOperation.TryEnter(this); if (operation is null) return;
        ++connectionCheckRevision;
        ConnectionResult.Text = "Creating/updating databaseâ€¦";
        try
        {
            RequireBootstrapAccess();
            var candidate = session.ValidateCandidate(ConnectionStringInput.Text);
            if (!candidate.IsValid) throw new InvalidOperationException(candidate.Error);
            if (!AllowConnectionCandidate(candidate.ConnectionString!)) return;
            var result = await databaseLifecycleServiceFactory(candidate.ConnectionString!)
                .BootstrapAsync(new BootstrapDatabase(migrationDirectory));
            var message = $"Database ready. Applied migrations: {(result.AppliedMigrations.Count == 0 ? "none" : string.Join(", ", result.AppliedMigrations))}.";
            ApplyPresentation(session.CompleteBootstrap(candidate, message));
            ConnectionStringInput.Text = session.ConnectionString;
            await NotifyCompletedAsync(SettingsWorkspaceOperation.DatabaseBootstrap, true);
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Workspace", "DATABASE_BOOTSTRAP_FAILED");
            ConnectionResult.Text = $"Database setup failed: {DesktopFriendlyError.Describe(exception, "Owner permission is required.")}";
        }
    }

    public async Task LoadProductConfigurationAsync()
    {
        if (!access.CanAdminister || productBusy || HasProductDraft) return;
        productBusy = true; ProductSettingsPanel.IsEnabled = false;
        try
        {
            var dashboard = await administrationServiceFactory(session.ConnectionString).LoadAsync("Store");
            ApplyProductSettings(session.ShowProductSettings(dashboard.ProductConfiguration));
            integrationsLoaded = true; savedProduct = ProductFields.Select(input => input.Text).ToArray();
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Workspace", "PRODUCT_CONFIGURATION_LOAD_FAILED");
            ConnectionResult.Text = FriendlyError(exception);
        }
        finally { productBusy = false; ProductSettingsPanel.IsEnabled = access.CanAdminister && integrationsLoaded; }
    }

    public async Task<bool> SaveProductConfigurationAsync()
    {
        if (IsBusy) return false;
        if (tallySettings?.HasDraft == true)
        {
            if (!await tallySettings.SaveDraftAsync()) return false;
            if (!HasIntegrationFieldsDraft) return true;
        }
        productBusy = true; ProductSettingsPanel.IsEnabled = false;
        try
        {
            RequireOwnerAccess();
            if (!integrationsLoaded) throw new InvalidOperationException("Load the current integration settings before saving.");
            var settings = DesktopSettingsPresentationSession.CreateProductConfiguration(
                DocumentRepositoryInput.Text, ShareFolderInput.Text,
                SmtpHostInput.Text, SmtpPortInput.Text, SmtpFromInput.Text, MaximumAttachmentInput.Text,
                ProductSettingsReasonInput.Text);
            await administrationServiceFactory(session.ConnectionString).SaveProductConfigurationAsync(settings);
            ProductSettingsReasonInput.Clear();
            savedProduct = ProductFields.Select(input => input.Text).ToArray();
            ConnectionResult.Text = "Product integration settings saved and audited.";
            await NotifyCompletedAsync(SettingsWorkspaceOperation.ProductConfigurationSaved, true);
            return true;
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.Workspace", "PRODUCT_CONFIGURATION_SAVE_FAILED");
            ConnectionResult.Text = FriendlyError(exception);
            return false;
        }
        finally { productBusy = false; ProductSettingsPanel.IsEnabled = access.CanAdminister && integrationsLoaded; }
    }

    private void ChooseRecoveryLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an off-PC recovery location" };
        if (dialog.ShowDialog() != true) return;
        if ((sender as Button)?.Tag as string == "first") FirstRecoveryLocation.Text = dialog.FolderName;
        else SecondRecoveryLocation.Text = dialog.FolderName;
    }

    private async void ExportRecoveryKeys_Click(object sender, RoutedEventArgs e)
    {
        if (!access.CanAdminister) { ConnectionResult.Text = "Owner permission is required to export recovery keys."; return; }
        if (RecoveryLocationsConfirmed.IsChecked != true) { ConnectionResult.Text = "Confirm that both recovery copies are outside this PC."; return; }
        if (IsBusy) return;
        ExportRecoveryKeysButton.IsEnabled = false;
        productBusy = true;
        try
        {
            var receipt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Backups", "certificate-custody.json");
            await new Etp.Reporting.Infrastructure.SqlServer.BackupCertificateService(ConnectionStringInput.Text).ExportAsync(
                RecoveryPassword.Password, FirstRecoveryLocation.Text, SecondRecoveryLocation.Text, receipt);
            ConnectionResult.Text = "Two recovery key copies were exported and their hashes recorded. Keep the password separately; verify a restore on a second machine before deployment.";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Settings.BackupCertificate", "CERTIFICATE_EXPORT_FAILED");
            ConnectionResult.Text = exception is ArgumentException ? exception.Message : "Recovery key export failed. Check the SQL edition, Owner permissions and access to both recovery folders. No successful custody receipt was recorded.";
        }
        finally
        {
            RecoveryPassword.Clear();
            productBusy = false;
            ExportRecoveryKeysButton.IsEnabled = access.CanAdminister;
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

    private static string FriendlyError(Exception exception) => DesktopFriendlyError.Describe(exception);
}
