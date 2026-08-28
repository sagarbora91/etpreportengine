namespace Etp.Reporting.Desktop.Tests;

public sealed class ImportCoordinatorCompositionTests
{
    [Fact]
    public void Import_workspace_delegates_import_orchestration_and_keeps_ui_behavior()
    {
        var root = FindRepositoryRoot();
        var importWorkspace = Read(root, "Modules", "Imports", "ImportWorkspaceView.xaml.cs");

        Assert.Contains("coordinator.ValidateAsync(WorkbookPathInput.Text", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.PersistValidatedAsync(connectionStringProvider(), context)", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.RetainValidatedEvidenceAsync(connectionStringProvider(), context)", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.OpenBatchSourceAsync(WorkbookPathInput.Text)", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.RunBatchAsync(", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.CancelBatch()", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("coordinator.FailedBatchPaths", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("RequireImportAccess();", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("Reading and validating workbook…", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("Validation blocked. Review the diagnostics below.", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("Batch completed:", importWorkspace, StringComparison.Ordinal);
        Assert.Contains("Controlled source restatement applied", importWorkspace, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_has_no_import_engine_constructions_or_import_workflow_state()
    {
        var root = FindRepositoryRoot();
        var mainWindow = Read(root, "MainWindow.xaml.cs");

        string[] forbidden =
        [
            "new OpenXmlWorkbookReader",
            "new ImportPreflight",
            "new ImportRowStager",
            "new BatchImportCoordinator",
            "new DelegateWorkbookImportOutcomeProcessor",
            "new SafeImportFailureClassifier",
            "new ProductisationOperationsService",
            "validatedWorkbook",
            "validatedPreflight",
            "validatedStaging",
            "activeBatchSource",
            "batchCancellation",
            "failedBatchPaths",
            "DesktopImportCoordinator importCoordinator",
            "WorkbookPathInput",
            "RetryBatchButton"
        ];
        foreach (var value in forbidden) Assert.DoesNotContain(value, mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Composition_owns_persistence_and_evidence_adapters()
    {
        var root = FindRepositoryRoot();
        var composition = Read(root, "Composition", "DesktopCompositionRoot.cs");

        Assert.Contains("new DesktopImportCoordinator(", composition, StringComparison.Ordinal);
        Assert.Contains("new SqlServerImportPersistenceUseCase(value)", composition, StringComparison.Ordinal);
        Assert.Contains("new ProductisationOperationsService(value).IntakeEtpEvidenceAsync", composition, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] path) =>
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
