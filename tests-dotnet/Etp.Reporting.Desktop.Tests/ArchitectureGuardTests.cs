using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// Layer-rule guards (ARCHITECTURE-AUDIT findings 1, 2 and 13). They read project files and
/// source text only; no database is touched.
/// </summary>
public sealed class ArchitectureGuardTests
{
    // Allowed direct project references, by project name. Changing the layering is a design
    // decision: edit this table in the same change and say why.
    private static readonly Dictionary<string, string[]> AllowedReferences = new(StringComparer.Ordinal)
    {
        ["Etp.Reporting.Domain"] = [],
        ["Etp.Reporting.Application"] = [],
        ["Etp.Reporting.Reporting"] = [],
        ["Etp.Reporting.Import"] = ["Etp.Reporting.Application", "Etp.Reporting.Domain"],
        ["Etp.Reporting.Infrastructure.SqlServer"] =
            ["Etp.Reporting.Application", "Etp.Reporting.Domain", "Etp.Reporting.Import", "Etp.Reporting.Reporting"],
        ["Etp.Reporting.Desktop"] =
            ["Etp.Reporting.Application", "Etp.Reporting.Import", "Etp.Reporting.Infrastructure.SqlServer", "Etp.Reporting.Reporting"],
    };

    // Desktop files (relative to src\Etp.Reporting.Desktop, forward slashes) that still use
    // Microsoft.Data.SqlClient outside Composition\ as of 1.11.1. The list may only shrink:
    // remove a line when its file stops using SqlClient, never add one.
    private static readonly string[] SqlClientAllowlist =
    [
        "DesktopDiagnostics.cs",
        "DesktopFriendlyError.cs",
        "ImportReviewSession.cs",
        "Modules/HealthCheck/ErrorProbes.cs",
        "Modules/HealthCheck/NightlyHealthSchedule.cs",
        "Modules/HealthCheck/OperationsProbes.cs",
        "Modules/HealthCheck/ReportProbes.cs",
        "Modules/Settings/ConnectionStringValidation.cs",
        "PowerShellOperationsService.cs",
        "StartupFailureText.cs",
    ];

    [Fact]
    public void Src_projects_reference_only_the_allowed_projects()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var actualProjects = Directory.GetDirectories(src)
            .Select(path => Path.GetFileName(path)!)
            .Where(name => File.Exists(Path.Combine(src, name, name + ".csproj")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AllowedReferences.Keys.OrderBy(k => k, StringComparer.Ordinal), actualProjects);

        foreach (var project in actualProjects)
        {
            var csproj = XDocument.Load(Path.Combine(src, project, project + ".csproj"));
            var references = csproj.Descendants("ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            var unexpected = references.Except(AllowedReferences[project]).ToArray();
            Assert.True(unexpected.Length == 0,
                $"{project} references {string.Join(", ", unexpected)}, which the layer rules do not allow. "
                + "Update AllowedReferences only if the layering was deliberately changed.");
        }
    }

    [Fact]
    public void Desktop_uses_SqlClient_only_in_Composition_and_the_shrinking_allowlist()
    {
        var desktop = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop");
        var users = Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(desktop, path))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bMicrosoft\.Data\.SqlClient\b"))
            .Select(path => Path.GetRelativePath(desktop, path).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("Composition/", StringComparison.Ordinal))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToArray();

        var added = users.Except(SqlClientAllowlist, StringComparer.Ordinal).ToArray();
        Assert.True(added.Length == 0,
            "New Desktop use of Microsoft.Data.SqlClient outside Composition\\: " + string.Join(", ", added)
            + ". Go through an Application interface built by the composition root instead.");

        var stale = SqlClientAllowlist.Except(users, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "These files no longer use SqlClient; remove them from SqlClientAllowlist: " + string.Join(", ", stale));
    }

    private static bool IsBuildOutput(string root, string path)
    {
        var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj", StringComparer.OrdinalIgnoreCase) || parts.Contains("bin", StringComparer.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }
}
