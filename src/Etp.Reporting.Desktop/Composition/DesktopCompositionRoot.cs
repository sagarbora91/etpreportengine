extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Dashboard;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.SourceInbox;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;
using ReportArchiveQuery = EtpApplication::Etp.Reporting.Application.Archive.IReportArchiveQuery<Etp.Reporting.Reporting.ReportPackDocument>;
using DigitalRegisterService = EtpApplication::Etp.Reporting.Application.Registers.IDigitalRegisterService;
using SharingContactsService = EtpApplication::Etp.Reporting.Application.Sharing.ISharingContactsService;
using DailyWorkflowQuery = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowQuery;
using DailyWorkflowCommands = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowCommands;
using DailyReportPackGenerator = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyReportPackGenerator<Etp.Reporting.Reporting.ReportPackDocument>;
using SourceInboxService = EtpApplication::Etp.Reporting.Application.SourceInbox.ISourceInboxService;
using ControlledReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IControlledReportQuery;
using OperationalReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IOperationalReportQuery<Etp.Reporting.Reporting.DailySalesReportDocument>;
using ManagementTrendQuery = EtpApplication::Etp.Reporting.Application.Reports.IManagementTrendQuery;
using AccountingService = EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService;
using OperationsAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IOperationsAdministrationService;
using AdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;
using ImportPersistenceUseCase = EtpApplication::Etp.Reporting.Application.Imports.IImportPersistenceUseCase<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using DatabaseLifecycleService = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.IDatabaseLifecycleService;
using TenderVarianceDiagnostic = EtpApplication::Etp.Reporting.Application.Reports.ITenderVarianceDiagnostic;
using InvestigationQuery = EtpApplication::Etp.Reporting.Application.Distribution.IInvestigationQuery;
using ReportDistributionService = EtpApplication::Etp.Reporting.Application.Distribution.IReportDistributionService<Etp.Reporting.Reporting.ReportPackDocument>;

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
        IReportExportCoordinator reportExportCoordinator = new ReportExportCoordinator();
        var dashboardView = new DashboardView(
            new DashboardPresentationSession(),
            (path, metadata, data) => reportExportCoordinator.ExportManagementSummaryPdfAsync(path, metadata, data));
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
        Func<string, ControlledReportQuery> controlledReportQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, OperationalReportQuery> operationalReportQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, ManagementTrendQuery> managementTrendQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, AccountingService> accountingServiceFactory = value => new SqlServerAccountingService(value);
        Func<string, OperationsAdministrationService> operationsAdministrationServiceFactory = value => new SqlServerOperationsAdministrationService(value);
        Func<string, AdministrationService> administrationServiceFactory = value => new SqlServerAdministrationService(value);
        Func<string, ImportPersistenceUseCase> importPersistenceUseCaseFactory = value => new SqlServerImportPersistenceUseCase(value);
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory = value => new SqlServerDatabaseLifecycleService(value);
        var settingsWorkspaceView = new SettingsWorkspaceView(
            new DesktopSettingsPresentationSession(settingsStore, connectionState),
            databaseLifecycleServiceFactory,
            administrationServiceFactory,
            MigrationDirectory);
        TenderVarianceDiagnostic tenderVarianceDiagnostic = new ReportingTenderVarianceDiagnostic();
        var reportsWorkspaceView = new ReportsWorkspaceView(
            () => connectionState.ConnectionString,
            controlledReportQueryFactory,
            operationalReportQueryFactory,
            managementTrendQueryFactory,
            reportExportCoordinator,
            tenderVarianceDiagnostic);
        var dailyWorkflowWorkspaceView = new DailyWorkflowWorkspaceView(
            new DailyWorkflowPresentationSession(),
            () => connectionState.ConnectionString,
            dailyWorkflowQueryFactory,
            dailyWorkflowCommandsFactory,
            dailyReportPackGeneratorFactory,
            static () => new(false, false, false),
            administratorApproved: null,
            static (_, _, _) => Task.CompletedTask,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document));
        Func<string, InvestigationQuery> investigationQueryFactory = value => new SqlServerInvestigationQuery(value);
        Func<string, ReportDistributionService> reportDistributionServiceFactory = value => new SqlServerReportDistributionService(value);
        var archiveSession = new ArchiveDistributionPresentationSession(
            reportArchiveQueryFactory, sharingContactsServiceFactory, reportDistributionServiceFactory);
        var archiveWorkspaceView = new ArchiveWorkspaceView(
            archiveSession,
            () => connectionState.ConnectionString,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document),
            new ArchiveShareLauncher());
        var registersSession = new RegistersPresentationSession(digitalRegisterServiceFactory);
        var registersWorkspaceView = new RegistersWorkspaceView(
            registersSession, () => connectionState.ConnectionString);
        var sourceInboxWorkspaceView = new SourceInboxWorkspaceView(
            sourceInboxServiceFactory,
            () => connectionState.ConnectionString,
            new SourceDocumentLauncher());
        var accountingSession = new AccountingPresentationSession(accountingServiceFactory);
        var accountingWorkspaceView = new AccountingWorkspaceView(
            accountingSession, () => connectionState.ConnectionString);
        var operationsAdministrationSession = new OperationsAdministrationPresentationSession();
        var operationsWorkspaceView = new OperationsWorkspaceView(
            operationsAdministrationSession,
            () => connectionState.ConnectionString,
            operationsAdministrationServiceFactory,
            async (script, cancellationToken) =>
            {
                var result = await PowerShellOperationsService.RunAsync(script, cancellationToken);
                return new MaintenanceOperationResult(result.Succeeded, result.Message);
            });
        var investigationWorkspaceView = new InvestigationApprovalsWorkspaceView(
            () => connectionState.ConnectionString,
            operationsAdministrationServiceFactory,
            investigationQueryFactory);
        var administrationWorkspaceView = new AdministrationWorkspaceView(
            operationsAdministrationSession,
            () => connectionState.ConnectionString,
            administrationServiceFactory);
        var importCoordinator = new DesktopImportCoordinator(
            importPersistenceUseCaseFactory,
            async (value, path, sha256, reportCode, storeCode, businessDate, cancellationToken) =>
                _ = await new ProductisationOperationsService(value).IntakeEtpEvidenceAsync(
                    path, sha256, reportCode, storeCode, businessDate, cancellationToken).ConfigureAwait(false));
        var importWorkspaceView = new ImportWorkspaceView(
            importCoordinator,
            () => connectionState.ConnectionString);
        return new MainWindow(
            shell,
            dashboardView,
            dashboardQueryFactory,
            settingsWorkspaceView,
            connectionState,
            accessSessionQueryFactory,
            archiveWorkspaceView,
            registersWorkspaceView,
            dailyWorkflowWorkspaceView,
            sourceInboxWorkspaceView,
            reportsWorkspaceView,
            accountingWorkspaceView,
            operationsWorkspaceView,
            investigationWorkspaceView,
            administrationWorkspaceView,
            databaseLifecycleServiceFactory,
            importWorkspaceView);
    }

    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var migrations = new DirectoryMigrationSource(MigrationDirectory);
        var bootstrapper = new SqlServerDatabaseBootstrapper(connectionString, migrations);
        await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunAutomationOnceAsync(CancellationToken cancellationToken = default)
    {
        var result = await new SqlServerOperationsAdministrationService(connectionString)
            .RunAutomationOnceAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.SourcesFailed == 0 ? 0 : 1;
    }
}
