using Etp.Reporting.Application.Access;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SettingsAndOperationsAdministrationPresentationTests : IDisposable
{
    private const string DefaultConnection =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(), "EtpPresentationSessionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Settings_session_owns_validation_status_and_safe_persistence()
    {
        var store = new DesktopSettingsStore(Path.Combine(testRoot, "settings"));
        var session = new DesktopSettingsPresentationSession(store, new DesktopConnectionState(DefaultConnection));
        var initialConnection = session.ConnectionString;

        var rejected = session.ValidateCandidate("Server=.;Database=Other;User ID=sa;Password=secret");

        Assert.False(rejected.IsValid);
        Assert.False(session.Current.IsConnected);
        Assert.Equal("Connection failed", session.Current.ConnectionStatus);
        Assert.Equal("Waiting for a valid Windows-integrated connection", session.Current.ImportStatus);
        Assert.Equal(initialConnection, session.ConnectionString);

        var candidate = session.ValidateCandidate(
            "Server=.;Database=Other;Integrated Security=SSPI;TrustServerCertificate=True");
        var presentation = session.CompleteHealthCheck(candidate, true, "Healthy", "16.0");

        Assert.True(presentation.IsConnected);
        Assert.True(presentation.SettingsPersisted);
        Assert.Equal("Ready to validate or report", presentation.ImportStatus);
        Assert.Equal("Connected to SQL Server 16.0.", presentation.ApplicationStatus);
        Assert.Contains("Initial Catalog=Other", session.ConnectionString, StringComparison.Ordinal);

        var reloaded = new DesktopSettingsPresentationSession(store, new DesktopConnectionState(DefaultConnection));
        Assert.Contains("Initial Catalog=Other", reloaded.LoadConnectionString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Operations_session_owns_period_dashboard_and_schedule_selection_state()
    {
        var session = new OperationsAdministrationPresentationSession();
        var period = session.CreatePeriod(new DateTime(2026, 8, 1), new DateTime(2026, 8, 25));
        var schedule = new ReportSchedule(7, "Morning", new TimeOnly(7, 30), true, true, false,
            null, null, null, null);
        var dashboard = new OperationsDashboard(
            new WatchFolderConfiguration("in", "done", "failed", "reports", 5, true, DateTime.UtcNow, "owner"),
            [new ManagementTrendPoint(new DateOnly(2026, 8, 25), "WLMHW", 100m, 2m, 1, 0m, 0)],
            [new DataQualityFinding("Warning", "Sales", "Q1", 1, null, "Review")],
            [new DataQualityIssue(1, "Sales", "Warning", "WLMHW", new DateOnly(2026, 8, 25),
                "Passed", "OPEN", "Review", null, DateTime.UtcNow, null)],
            [schedule],
            [new AutomationRun(3, "Scheduled", null, "WLMHW", new DateOnly(2026, 8, 25),
                "Succeeded", "Done", DateTime.UtcNow, DateTime.UtcNow, "system")]);

        var state = session.Capture(dashboard);
        var editor = Assert.IsType<ScheduleEditorState>(session.SelectSchedule(schedule));
        var command = session.CreateScheduleCommand("08:15", false, true, true, "Seasonal change");

        Assert.Equal(new DateOnly(2026, 8, 1), period.From);
        Assert.Single(state.Trend);
        Assert.Single(state.Quality);
        Assert.Single(state.Issues);
        Assert.Single(state.AutomationRuns);
        Assert.Contains("1 daily store result(s), 1 governed quality issue(s), and 1 recent unattended run(s)", state.Status, StringComparison.Ordinal);
        Assert.Equal("07:30", editor.Time);
        Assert.Equal(7, command.Id);
        Assert.Equal(new TimeOnly(8, 15), command.LocalRunTime);

        session.SelectSchedule(null);
        Assert.Equal("Select the morning or evening schedule first.",
            Assert.Throws<InvalidOperationException>(() => session.CreateScheduleCommand("08:15", true, true, true, "reason")).Message);
        Assert.Equal("Select the management trend dates.",
            Assert.Throws<InvalidOperationException>(() => session.CreatePeriod(null, DateTime.Today)).Message);
    }

    [Fact]
    public void Administration_session_maps_users_product_state_and_validates_commands()
    {
        var session = new OperationsAdministrationPresentationSession();
        var dashboard = new AdministrationDashboard(
            [new ControlledMaster("Store", "WLMHW", "Titan World", "APPROVED", true, null, null)],
            [new ApplicationUser(1, @"DOMAIN\owner", "Owner", AccessRole.Owner, true, DateTime.UtcNow, "seed")],
            [new KpiDefinition("SALES", "Sales", "Net sales", "SUM", "ETP", new DateOnly(2026, 4, 1), 1, "APPROVED", "owner", true)],
            [new ProductHealth("Database", "Healthy", "Ready")],
            new ProductConfiguration("docs", "share", null, null, "smtp.example", 587, true,
                "reports@example.com", 20, DateTime.UtcNow, "owner"));

        var state = session.Capture(dashboard);
        var user = OperationsAdministrationPresentationSession.CreateUserCommand(
            @"DOMAIN\manager", "Manager", "Store Manager", true, "Approved");
        var product = OperationsAdministrationPresentationSession.CreateProductConfiguration(
            "docs", "share", "", "", "smtp.example", "587", "reports@example.com", "25", "Approved");

        Assert.Equal("OWNER", Assert.Single(state.Users).RoleCode);
        Assert.Single(state.Masters);
        Assert.Single(state.Kpis);
        Assert.Single(state.ProductHealth);
        Assert.Equal("587", state.ProductSettings.SmtpPort);
        Assert.Equal("Controlled masters and Windows-integrated access are ready for Owner administration.", state.Status);
        Assert.Equal(AccessRole.StoreManager, user.Role);
        Assert.Equal(587, product.SmtpPort);
        Assert.Equal(25, product.MaximumAttachmentMb);
        Assert.Equal("Enter a valid SMTP port.",
            Assert.Throws<InvalidOperationException>(() => OperationsAdministrationPresentationSession.CreateProductConfiguration(
                "docs", "share", "", "", "smtp", "invalid", "from", "25", "reason")).Message);
        Assert.Equal("Enter a valid maximum attachment size in MB.",
            Assert.Throws<InvalidOperationException>(() => OperationsAdministrationPresentationSession.CreateProductConfiguration(
                "docs", "share", "", "", "smtp", "587", "from", "invalid", "reason")).Message);
    }

    [Fact]
    public void MainWindow_delegates_settings_and_operations_administration_presentation_logic()
    {
        var root = FindRepositoryRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisation = "";
        var settingsView = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Settings", "SettingsWorkspaceView.xaml.cs"));

        Assert.Contains("SettingsWorkspaceView settingsWorkspace", main, StringComparison.Ordinal);
        Assert.Contains("OperationsWorkspaceView operationsWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("InvestigationApprovalsWorkspaceView investigationWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("AdministrationWorkspaceView administrationWorkspaceView", main, StringComparison.Ordinal);
        Assert.DoesNotContain("OperationsAdministrationPresentationSession operationsAdministrationPresentation", main, StringComparison.Ordinal);
        Assert.DoesNotContain("operationsAdministrationServiceFactory", main, StringComparison.Ordinal);
        Assert.DoesNotContain("investigationQueryFactory", productisation, StringComparison.Ordinal);
        Assert.Contains("DesktopSettingsPresentationSession.CreateProductConfiguration", settingsView, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeOnly.TryParseExact(ScheduleTimeInput.Text", main, StringComparison.Ordinal);
        Assert.DoesNotContain("dashboard.Users.Select", main, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumAttachmentInput", productisation, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
