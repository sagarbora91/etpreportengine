extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.Settings;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;
using ReportArchiveQuery = EtpApplication::Etp.Reporting.Application.Archive.IReportArchiveQuery<Etp.Reporting.Reporting.ReportPackDocument>;
using DigitalRegisterService = EtpApplication::Etp.Reporting.Application.Registers.IDigitalRegisterService;
using SharingContactsService = EtpApplication::Etp.Reporting.Application.Sharing.ISharingContactsService;
using DailyWorkflowQuery = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowQuery;
using DailyWorkflowCommands = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowCommands;
using DailyReportPackGenerator = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyReportPackGenerator<Etp.Reporting.Reporting.ReportPackDocument>;
using SourceInboxService = EtpApplication::Etp.Reporting.Application.SourceInbox.ISourceInboxService;

namespace Etp.Reporting.Desktop.Composition;

/// <summary>
/// Owns construction of the executable's concrete adapters and top-level WPF objects.
/// Feature views and view models must receive their dependencies from this boundary.
/// </summary>
public sealed class DesktopCompositionRoot
{
    public const string DefaultConnectionString =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    private readonly string baseDirectory;
    private readonly string connectionString;
    private readonly string settingsDirectory;

    public DesktopCompositionRoot(
        string baseDirectory,
        string connectionString,
        string? settingsDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("The application base directory is required.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        if (settingsDirectory is not null && string.IsNullOrWhiteSpace(settingsDirectory))
            throw new ArgumentException("The settings directory cannot be blank.", nameof(settingsDirectory));

        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.connectionString = connectionString;
        this.settingsDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtpReporting"));
    }

    public string MigrationDirectory => Path.Combine(baseDirectory, "database", "migrations");
    public string SettingsDirectory => settingsDirectory;

    public static DesktopCompositionRoot CreateDefault() =>
        new(AppContext.BaseDirectory, DefaultConnectionString);

    public MainWindow CreateMainWindow()
    {
        var shell = new ShellViewModel(new ShellNavigationService());
        var dashboardView = new DashboardView();
        var settingsStore = new DesktopSettingsStore(settingsDirectory);
        var connectionState = new DesktopConnectionState(connectionString);
        Func<string, DashboardQuery> dashboardQueryFactory = value => new SqlServerDashboardQuery(value);
        Func<string, AccessSessionQuery> accessSessionQueryFactory = value => new SqlServerAccessSessionQuery(value);
        Func<string, ReportArchiveQuery> reportArchiveQueryFactory = value => new SqlServerReportArchiveQuery(value);
        Func<string, DigitalRegisterService> digitalRegisterServiceFactory = value => new SqlServerDigitalRegisterService(value);
        Func<string, SharingContactsService> sharingContactsServiceFactory = value => new SqlServerSharingContactsService(value);
        Func<string, DailyWorkflowQuery> dailyWorkflowQueryFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, DailyWorkflowCommands> dailyWorkflowCommandsFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, DailyReportPackGenerator> dailyReportPackGeneratorFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, SourceInboxService> sourceInboxServiceFactory = value => new SqlServerSourceInboxService(value);
        return new MainWindow(
            shell,
            dashboardView,
            dashboardQueryFactory,
            settingsStore,
            connectionState,
            accessSessionQueryFactory,
            reportArchiveQueryFactory,
            digitalRegisterServiceFactory,
            sharingContactsServiceFactory,
            dailyWorkflowQueryFactory,
            dailyWorkflowCommandsFactory,
            dailyReportPackGeneratorFactory,
            sourceInboxServiceFactory);
    }

    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var migrations = new DirectoryMigrationSource(MigrationDirectory);
        var bootstrapper = new SqlServerDatabaseBootstrapper(connectionString, migrations);
        await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunAutomationOnceAsync(CancellationToken cancellationToken = default)
    {
        var result = await new AutomatedOperationsService(connectionString)
            .RunOnceAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.SourcesFailed == 0 ? 0 : 1;
    }
}
