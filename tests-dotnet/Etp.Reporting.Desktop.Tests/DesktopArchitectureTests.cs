namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Lower_layer_projects_do_not_reference_desktop()
    {
        string[] lowerProjects =
        [
            "Etp.Reporting.Domain",
            "Etp.Reporting.Application",
            "Etp.Reporting.Import",
            "Etp.Reporting.Reporting",
            "Etp.Reporting.Infrastructure.SqlServer"
        ];

        foreach (var projectName in lowerProjects)
        {
            var projectDirectory = Path.Combine(RepositoryRoot, "src", projectName);
            var projectFile = Path.Combine(projectDirectory, $"{projectName}.csproj");

            Assert.True(File.Exists(projectFile), $"Expected project file was not found: {projectFile}");
            Assert.DoesNotContain(
                "Etp.Reporting.Desktop",
                File.ReadAllText(projectFile),
                StringComparison.OrdinalIgnoreCase);

            foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !IsBuildOutput(path)))
            {
                Assert.DoesNotContain(
                    "Etp.Reporting.Desktop",
                    File.ReadAllText(sourceFile),
                    StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Desktop_navigation_folder_remains_framework_and_feature_neutral()
    {
        var desktopDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Desktop");
        var navigationDirectories = Directory.EnumerateDirectories(desktopDirectory, "Navigation", SearchOption.AllDirectories).ToArray();
        if (navigationDirectories.Length == 0) return;

        string[] forbiddenReferences =
        [
            "System.Windows",
            "Etp.Reporting.Import",
            "Etp.Reporting.Infrastructure.SqlServer",
            "Etp.Reporting.Reporting",
            "Microsoft.Data.SqlClient"
        ];

        foreach (var navigationDirectory in navigationDirectories)
        {
            Assert.Empty(Directory.EnumerateFiles(navigationDirectory, "*.xaml", SearchOption.AllDirectories));
            foreach (var sourceFile in Directory.EnumerateFiles(navigationDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(sourceFile);
                foreach (var forbiddenReference in forbiddenReferences)
                {
                    Assert.DoesNotContain(forbiddenReference, source, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void Application_dashboard_contract_remains_dependency_free()
    {
        var applicationDirectory = Path.Combine(RepositoryRoot, "src", "Etp.Reporting.Application");
        var dashboardDirectory = Path.Combine(applicationDirectory, "Dashboard");
        var projectFile = Path.Combine(applicationDirectory, "Etp.Reporting.Application.csproj");

        Assert.True(Directory.Exists(dashboardDirectory), $"Expected Dashboard contract directory was not found: {dashboardDirectory}");
        Assert.DoesNotContain("ProjectReference", File.ReadAllText(projectFile), StringComparison.OrdinalIgnoreCase);

        string[] forbiddenReferences =
        [
            "System.Windows",
            "Microsoft.Data.SqlClient",
            "Etp.Reporting.Desktop",
            "Etp.Reporting.Import",
            "Etp.Reporting.Infrastructure",
            "Etp.Reporting.Reporting"
        ];

        foreach (var sourceFile in Directory.EnumerateFiles(dashboardDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourceFile);
            foreach (var forbiddenReference in forbiddenReferences)
            {
                Assert.DoesNotContain(forbiddenReference, source, StringComparison.Ordinal);
            }
        }
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
