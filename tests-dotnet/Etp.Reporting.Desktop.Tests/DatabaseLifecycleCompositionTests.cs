namespace Etp.Reporting.Desktop.Tests;

public sealed class DatabaseLifecycleCompositionTests
{
    [Fact]
    public void Database_lifecycle_and_audit_use_the_injected_application_service()
    {
        var root = FindRepositoryRoot();
        var mainWindow = ReadSource(root, "MainWindow.xaml.cs");
        var settingsView = ReadSource(root, "Modules", "Settings", "SettingsWorkspaceView.xaml.cs");
        var importCoordinator = ReadSource(root, "Modules", "Imports", "DesktopImportCoordinator.cs");
        var composition = ReadSource(root, "Composition", "DesktopCompositionRoot.cs");

        Assert.Contains("Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory", mainWindow, StringComparison.Ordinal);
        Assert.Contains("databaseLifecycleServiceFactory(candidate.ConnectionString!)", settingsView, StringComparison.Ordinal);
        Assert.Contains("BootstrapAsync(new BootstrapDatabase(migrationDirectory))", settingsView, StringComparison.Ordinal);
        Assert.Contains("CheckHealthAsync()", settingsView, StringComparison.Ordinal);
        Assert.Contains("databaseLifecycleServiceFactory(connectionState.ConnectionString).RecordAuditAsync(new RecordOperationalAudit", mainWindow, StringComparison.Ordinal);
        Assert.Contains("persistence.FindCurrentImportFileIdAsync", importCoordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("FindCurrentImportFileIdAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new SqlServerDatabaseLifecycleService(value)", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_does_not_construct_database_lifecycle_infrastructure()
    {
        var root = FindRepositoryRoot();
        var mainWindow = ReadSource(root, "MainWindow.xaml.cs");

        Assert.DoesNotContain("new SqlServerDatabaseBootstrapper", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new DirectoryMigrationSource", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new SqlServerHealthCheck", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new OperationalAuditRepository", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new OperationalCompletionRepository", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Lifecycle_handlers_retain_connection_validation_and_operator_feedback()
    {
        var root = FindRepositoryRoot();
        var mainWindow = ReadSource(root, "MainWindow.xaml.cs");
        var settingsSession = ReadSource(root, "Modules", "Settings", "DesktopSettingsPresentationSession.cs");
        var settingsView = ReadSource(root, "Modules", "Settings", "SettingsWorkspaceView.xaml.cs");

        Assert.Contains("session.ValidateCandidate(ConnectionStringInput.Text)", settingsView, StringComparison.Ordinal);
        Assert.Contains("ConnectionStringValidation.Validate(value)", settingsSession, StringComparison.Ordinal);
        Assert.Contains("access.HasAssignedRole && !access.CanAdminister", settingsView, StringComparison.Ordinal);
        Assert.Contains("Creating/updating database…", settingsView, StringComparison.Ordinal);
        Assert.Contains("Database ready. Applied migrations:", settingsView, StringComparison.Ordinal);
        Assert.Contains("Waiting for a valid Windows-integrated connection", settingsSession, StringComparison.Ordinal);
        Assert.Contains("Database connection tested", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DesktopFriendlyError.IsAuditFailure(ex)", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlException", mainWindow, StringComparison.Ordinal);
    }

    private static string ReadSource(string root, params string[] path) =>
        File.ReadAllText(Path.Combine([root, "src", "Etp.Reporting.Desktop", .. path]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
