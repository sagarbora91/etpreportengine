extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;

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

    public DesktopCompositionRoot(string baseDirectory, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("The application base directory is required.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));

        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.connectionString = connectionString;
    }

    public string MigrationDirectory => Path.Combine(baseDirectory, "database", "migrations");

    public static DesktopCompositionRoot CreateDefault() =>
        new(AppContext.BaseDirectory, DefaultConnectionString);

    public MainWindow CreateMainWindow()
    {
        var shell = new ShellViewModel(new ShellNavigationService());
        var dashboardView = new DashboardView();
        Func<string, DashboardQuery> dashboardQueryFactory = value => new SqlServerDashboardQuery(value);
        return new MainWindow(shell, dashboardView, dashboardQueryFactory);
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
