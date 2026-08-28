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
            ["MainWindow.Productisation.cs|AccountingBatchComposer"] = 1,
            ["MainWindow.Productisation.cs|ProductisationOperationsService"] = 1,
            ["MainWindow.Productisation.cs|ProductisationRepository"] = 20,
            ["MainWindow.Productisation.cs|TallyXmlExportService"] = 1,
            ["MainWindow.xaml.cs|AutomatedOperationsService"] = 1,
            ["MainWindow.xaml.cs|DailyReportingPackService"] = 0,
            ["MainWindow.xaml.cs|DailyReportingWorkflowRepository"] = 0,
            ["MainWindow.xaml.cs|DirectoryMigrationSource"] = 1,
            ["MainWindow.xaml.cs|OperationalAuditRepository"] = 1,
            ["MainWindow.xaml.cs|OperationalCompletionRepository"] = 1,
            ["MainWindow.xaml.cs|OperationalReportRepository"] = 10,
            ["MainWindow.xaml.cs|Phase2OperationsRepository"] = 7,
            ["MainWindow.xaml.cs|ProductisationOperationsService"] = 2,
            ["MainWindow.xaml.cs|ProductisationRepository"] = 2,
            ["MainWindow.xaml.cs|R022SqlImportOrchestrator"] = 2,
            ["MainWindow.xaml.cs|R025SqlImportOrchestrator"] = 2,
            ["MainWindow.xaml.cs|RetailEnrichmentSqlImportOrchestrator"] = 2,
            ["MainWindow.xaml.cs|SqlServerDatabaseBootstrapper"] = 1,
            ["MainWindow.xaml.cs|SqlServerHealthCheck"] = 1,
            ["MainWindow.xaml.cs|SqlServerImportFileRepository"] = 2,
            ["MainWindow.xaml.cs|SqlServerReportingQueryRepository"] = 2,
            ["MainWindow.xaml.cs|SqlServerTransactionalImportStore"] = 2,
            ["MainWindow.xaml.cs|StockSqlImportOrchestrator"] = 2
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
