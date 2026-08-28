namespace Etp.Reporting.Desktop.Tests;

public sealed class ImportPersistenceCompositionTests
{
    [Fact]
    public void Interactive_and_batch_imports_use_the_composed_application_port()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var workspace = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Imports", "ImportWorkspaceView.xaml.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var coordinator = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Imports", "DesktopImportCoordinator.cs"));

        Assert.Contains("ImportWorkspaceView importWorkspaceView", mainWindow, StringComparison.Ordinal);
        Assert.Contains("DesktopImportCoordinator coordinator", workspace, StringComparison.Ordinal);
        Assert.Contains("Func<string, ImportPersistenceUseCase> importPersistenceUseCaseFactory", composition, StringComparison.Ordinal);
        Assert.Contains("new DesktopImportCoordinator(", composition, StringComparison.Ordinal);
        Assert.Contains("var persistence = persistenceFactory(connectionString);", coordinator, StringComparison.Ordinal);
        Assert.Contains("persistence.ExistsByHashAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("persistence.LoadOutcomeByHashAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("FindCurrentImportFileIdAsync", coordinator, StringComparison.Ordinal);

        string[] forbidden =
        [
            "R022SqlImportOrchestrator",
            "R025SqlImportOrchestrator",
            "RetailEnrichmentSqlImportOrchestrator",
            "StockSqlImportOrchestrator",
            "SqlServerTransactionalImportStore",
            "SqlServerImportFileRepository",
            "OperationalCompletionRepository"
        ];
        foreach (var concreteType in forbidden)
            Assert.DoesNotContain(concreteType, mainWindow, StringComparison.Ordinal);

        Assert.Contains("new SqlServerImportPersistenceUseCase(value)", composition, StringComparison.Ordinal);
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
