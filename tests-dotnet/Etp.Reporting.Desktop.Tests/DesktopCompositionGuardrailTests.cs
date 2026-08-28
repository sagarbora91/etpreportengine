using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopCompositionGuardrailTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Regex ConcreteClassDeclaration = new(
        @"\b(?:public|internal)\s+(?:(?:sealed|static|abstract|partial)\s+)*class\s+(?<type>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex ExplicitConstruction = new(
        @"\bnew\s+(?:global::)?(?:(?:Etp\.Reporting\.Infrastructure\.SqlServer|Microsoft\.Data\.SqlClient)\.)?(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.CultureInvariant);

    // Temporary baseline: these maxima may only decrease as composition moves to App/composition code.
    private static readonly IReadOnlyDictionary<string, int> MainWindowConstructionMaxima =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["MainWindow.xaml.cs|BatchImportCoordinator"] = 0,
            ["MainWindow.xaml.cs|DelegateWorkbookImportOutcomeProcessor"] = 0,
            ["MainWindow.xaml.cs|AutomatedOperationsService"] = 0,
            ["MainWindow.xaml.cs|DailyReportingPackService"] = 0,
            ["MainWindow.xaml.cs|DailyReportingWorkflowRepository"] = 0,
            ["MainWindow.xaml.cs|DirectoryMigrationSource"] = 0,
            ["MainWindow.xaml.cs|OperationalAuditRepository"] = 0,
            ["MainWindow.xaml.cs|OperationalCompletionRepository"] = 0,
            ["MainWindow.xaml.cs|OperationalReportRepository"] = 0,
            ["MainWindow.xaml.cs|Phase2OperationsRepository"] = 0,
            ["MainWindow.xaml.cs|ImportPreflight"] = 0,
            ["MainWindow.xaml.cs|ImportRowStager"] = 0,
            ["MainWindow.xaml.cs|OpenXmlWorkbookReader"] = 0,
            ["MainWindow.xaml.cs|ProductisationOperationsService"] = 0,
            ["MainWindow.xaml.cs|ProductisationRepository"] = 0,
            ["MainWindow.xaml.cs|R022SqlImportOrchestrator"] = 0,
            ["MainWindow.xaml.cs|R025SqlImportOrchestrator"] = 0,
            ["MainWindow.xaml.cs|RetailEnrichmentSqlImportOrchestrator"] = 0,
            ["MainWindow.xaml.cs|SafeImportFailureClassifier"] = 0,
            ["MainWindow.xaml.cs|SqlServerDatabaseBootstrapper"] = 0,
            ["MainWindow.xaml.cs|SqlServerHealthCheck"] = 0,
            ["MainWindow.xaml.cs|SqlServerImportFileRepository"] = 0,
            ["MainWindow.xaml.cs|SqlServerReportingQueryRepository"] = 0,
            ["MainWindow.xaml.cs|SqlServerTransactionalImportStore"] = 0,
            ["MainWindow.xaml.cs|StockSqlImportOrchestrator"] = 0
        };

    [Fact]
    public void Views_and_view_models_do_not_reference_or_construct_sql_infrastructure()
    {
        var infrastructureTypes = LoadSqlInfrastructureConcreteTypes();

        foreach (var sourceFile in EnumeratePresentationSourceFiles())
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Data.SqlClient", source, StringComparison.Ordinal);
            Assert.Empty(FindInfrastructureConstructions(sourceFile, source, infrastructureTypes));
        }
    }

    [Fact]
    public void MainWindow_sql_infrastructure_construction_inventory_can_only_decrease()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var infrastructureTypes = LoadSqlInfrastructureConcreteTypes();
        var actual = Directory.EnumerateFiles(desktopDirectory, "MainWindow*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(path => FindInfrastructureConstructions(path, File.ReadAllText(path), infrastructureTypes))
            .GroupBy(item => $"{Path.GetFileName(item.File)}|{item.Type}", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var (key, count) in actual)
        {
            Assert.True(
                MainWindowConstructionMaxima.TryGetValue(key, out var maximum),
                $"New MainWindow SQL-infrastructure construction is not allowed: {key} ({count}). Compose it outside the shell.");
            Assert.True(count <= maximum, $"MainWindow SQL-infrastructure construction increased: {key}, baseline {maximum}, actual {count}.");
        }
    }

    [Fact]
    public void MainWindow_does_not_use_the_connection_text_box_as_a_service_dependency()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var forbiddenDependency = new Regex(
            @"\bnew\s+[A-Za-z_][A-Za-z0-9_]*\s*\(\s*ConnectionStringInput\.Text|\w+Factory\s*\(\s*ConnectionStringInput\.Text",
            RegexOptions.CultureInvariant);

        foreach (var sourceFile in Directory.EnumerateFiles(desktopDirectory, "MainWindow*.cs", SearchOption.TopDirectoryOnly))
        {
            Assert.DoesNotMatch(forbiddenDependency, File.ReadAllText(sourceFile));
        }
    }

    [Fact]
    public void Operations_and_administration_adapters_are_composed_outside_MainWindow()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var rootSource = File.ReadAllText(Path.Combine(desktopDirectory, "Composition", "DesktopCompositionRoot.cs"));
        var mainSource = File.ReadAllText(Path.Combine(desktopDirectory, "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(desktopDirectory, "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisationSource = "";
        var settingsSource = File.ReadAllText(Path.Combine(desktopDirectory, "Modules", "Settings", "SettingsWorkspaceView.xaml.cs"));
        var operationsSource = File.ReadAllText(Path.Combine(desktopDirectory, "Modules", "OperationsAdministration", "OperationsWorkspaceView.xaml.cs"));
        var administrationSource = File.ReadAllText(Path.Combine(desktopDirectory, "Modules", "OperationsAdministration", "AdministrationWorkspaceView.xaml.cs"));

        Assert.Contains("new SqlServerOperationsAdministrationService(value)", rootSource, StringComparison.Ordinal);
        Assert.Contains("new SqlServerAdministrationService(value)", rootSource, StringComparison.Ordinal);
        Assert.Contains("serviceFactory(connectionStringProvider())", operationsSource, StringComparison.Ordinal);
        Assert.Contains("serviceFactory(connectionStringProvider())", administrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operationsAdministrationServiceFactory", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("administrationServiceFactory", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("operationsAdministrationServiceFactory", productisationSource, StringComparison.Ordinal);
        Assert.Contains("administrationServiceFactory(session.ConnectionString)", settingsSource, StringComparison.Ordinal);

        Assert.DoesNotContain("new Phase2OperationsRepository", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new AutomatedOperationsService", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository(connectionState.ConnectionString).CreateAdjustmentRequestAsync", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository(connectionState.ConnectionString).UpdateIssueWorkflowAsync", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository(connectionState.ConnectionString).LoadApprovalsAsync", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository(connectionState.ConnectionString).DecideApprovalAsync", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ProductisationRepository(connectionState.ConnectionString).SaveSettingsAsync", productisationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Investigation_and_distribution_adapters_are_composed_outside_MainWindow()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var rootSource = File.ReadAllText(Path.Combine(desktopDirectory, "Composition", "DesktopCompositionRoot.cs"));
        var mainSource = File.ReadAllText(Path.Combine(desktopDirectory, "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(desktopDirectory, "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisationSource = "";
        var archiveWorkspaceSource = File.ReadAllText(Path.Combine(desktopDirectory, "Modules", "Archive", "ArchiveWorkspaceView.xaml.cs"));
        var investigationWorkspaceSource = File.ReadAllText(Path.Combine(desktopDirectory, "Modules", "OperationsAdministration", "InvestigationApprovalsWorkspaceView.xaml.cs"));

        Assert.Contains("new SqlServerInvestigationQuery(value)", rootSource, StringComparison.Ordinal);
        Assert.Contains("new SqlServerReportDistributionService(value)", rootSource, StringComparison.Ordinal);
        Assert.Contains("investigationQueryFactory(connectionStringProvider())", investigationWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("session.RecordAttemptAsync(connectionStringProvider()", archiveWorkspaceSource, StringComparison.Ordinal);
        Assert.Contains("InvestigationApprovalsWorkspaceView", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, InvestigationQuery>", mainSource, StringComparison.Ordinal);
        Assert.Contains("ArchiveWorkspaceView", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<string, ReportDistributionService>", mainSource, StringComparison.Ordinal);

        Assert.DoesNotContain("new ProductisationRepository", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ReportPackageService", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordDistributionAttempt", productisationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Productisation_presentation_sessions_are_framework_neutral_and_own_mutable_workflow_state()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var sessionFiles = new[]
        {
            Path.Combine(desktopDirectory, "Modules", "Archive", "ArchiveDistributionPresentationSession.cs"),
            Path.Combine(desktopDirectory, "Modules", "Registers", "RegistersPresentationSession.cs"),
            Path.Combine(desktopDirectory, "Modules", "Accounting", "AccountingPresentationSession.cs")
        };

        foreach (var sessionFile in sessionFiles)
        {
            var source = File.ReadAllText(sessionFile);
            Assert.DoesNotContain("System.Windows", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Win32", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Etp.Reporting.Infrastructure.SqlServer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MainWindow", source, StringComparison.Ordinal);
        }

        var mainSource = File.ReadAllText(Path.Combine(desktopDirectory, "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(desktopDirectory, "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisationSource = "";
        Assert.DoesNotContain("currentArchivedDocument", mainSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentShareFile", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAccountingDraft", productisationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("currentAccountingReportGenerationId", productisationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_modules_do_not_reference_each_other_directly()
    {
        var modulesDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop", "Modules");
        var modules = Directory.EnumerateDirectories(modulesDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        foreach (var moduleDirectory in Directory.EnumerateDirectories(modulesDirectory))
        {
            var currentModule = Path.GetFileName(moduleDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(moduleDirectory, "*.*", SearchOption.AllDirectories)
                         .Where(path => Path.GetExtension(path) is ".cs" or ".xaml"))
            {
                var source = File.ReadAllText(sourceFile);
                foreach (var otherModule in modules.Where(name => !string.Equals(name, currentModule, StringComparison.Ordinal)))
                {
                    Assert.DoesNotContain($"Etp.Reporting.Desktop.Modules.{otherModule}", source, StringComparison.Ordinal);
                }
            }
        }
    }

    private static IReadOnlySet<string> LoadSqlInfrastructureConcreteTypes()
    {
        var directory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Infrastructure.SqlServer");
        var types = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => ConcreteClassDeclaration.Matches(File.ReadAllText(path)).Select(match => match.Groups["type"].Value))
            .ToHashSet(StringComparer.Ordinal);
        types.Add("SqlConnection");
        types.Add("ReportPackageService");
        return types;
    }

    private static IEnumerable<(string File, string Type)> FindInfrastructureConstructions(
        string sourceFile,
        string source,
        IReadOnlySet<string> infrastructureTypes) =>
        ExplicitConstruction.Matches(source)
            .Select(match => match.Groups["type"].Value)
            .Where(infrastructureTypes.Contains)
            .Select(type => (sourceFile, type));

    private static IEnumerable<string> EnumeratePresentationSourceFiles()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        return Directory.EnumerateFiles(desktopDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path =>
            {
                var relative = Path.GetRelativePath(desktopDirectory, path);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fileName = Path.GetFileNameWithoutExtension(path);
                return parts.Contains("Views", StringComparer.OrdinalIgnoreCase) ||
                       parts.Contains("ViewModels", StringComparer.OrdinalIgnoreCase) ||
                       fileName.EndsWith("View", StringComparison.Ordinal) ||
                       fileName.EndsWith("ViewModel", StringComparison.Ordinal);
            });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
