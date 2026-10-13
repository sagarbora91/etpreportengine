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
            ["Etp.Reporting.Application", "Etp.Reporting.Import", "Etp.Reporting.Infrastructure.Http", "Etp.Reporting.Infrastructure.SqlServer", "Etp.Reporting.Reporting"],
        // 1.12 (plan A1/D1): the Tally HTTP gateway sits beside the SQL adapter and depends on the Application only.
        ["Etp.Reporting.Infrastructure.Http"] = ["Etp.Reporting.Application"],
        // 1.12 wave 2 (U3): Desktop builds the HttpTallyGateway in DesktopCompositionRoot.Accounting.cs.
    };

    // Projects the table may name before they exist on disk (wave-1 lanes create them; A2 owns the csproj).
    private static readonly string[] PlannedProjects = ["Etp.Reporting.Infrastructure.Http"];

    // Desktop files (relative to src\Etp.Reporting.Desktop, forward slashes) that still use
    // Microsoft.Data.SqlClient outside Composition\. The list may only shrink: remove a line when
    // its file stops using SqlClient, never add one. 1.12 (A1) removed the health-check probes,
    // ConnectionStringValidation and PowerShellOperationsService; 1.12 wave 2 (U1) removed ImportReviewSession. These three
    // read SqlException numbers for diagnostics.
    private static readonly string[] SqlClientAllowlist =
    [
        "DesktopDiagnostics.cs",
        "DesktopFriendlyError.cs",
        "StartupFailureText.cs",
    ];

    // 1.12 (A1, ARCHITECTURE-AUDIT finding 2): a screen gets services from the composition root, never a connection
    // string. Outside Composition\ no field or parameter may be a connection string (string or Func<string> named
    // connection/...ConnectionString...), and no factory may be keyed by one (Func<string, ...Query/Service...>).
    private static readonly Regex ConnectionStringMember = new(
        @"\b(?:string\??|Func<string\??>\??)\s+_?(?:\w*[cC]onnectionString\w*|connection)\s*[,;)=]" +
        @"|\bFunc<string\??,\s*(?:\w+\.)*\w*(?:Query|Service|UseCase|Repository|Store)\b",
        RegexOptions.CultureInvariant);

    // Settings is the connection editor: it reads, checks and saves the connection string by design.
    private static readonly string[] ConnectionEditorFiles =
    [
        "Modules/Settings/ConnectionStringValidation.cs",
        "Modules/Settings/DesktopConnectionSettings.cs",
        "Modules/Settings/DesktopConnectionState.cs",
        "Modules/Settings/DesktopSettingsPresentationSession.cs",
        "Modules/Settings/DesktopSettingsStore.cs",
        "Modules/Settings/SettingsWorkspaceView.xaml.cs",
    ];

    // Files that still hold a connection string. 1.12 wave 2 emptied it (U1: MainWindow, ImportReviewSession; U2:
    // EveningMastersView, BrandStockEntryWindow; U3: TallyDestinationSettingsView). It stays empty: never add a line.
    private static readonly string[] ConnectionStringAllowlist =
    [
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
        var unknown = actualProjects.Except(AllowedReferences.Keys, StringComparer.Ordinal).ToArray();
        Assert.True(unknown.Length == 0, "Add these projects to AllowedReferences with their allowed references: " + string.Join(", ", unknown));
        var missing = AllowedReferences.Keys.Except(actualProjects, StringComparer.Ordinal).Except(PlannedProjects, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0, "AllowedReferences names projects that no longer exist: " + string.Join(", ", missing));

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

    [Fact]
    public void Desktop_holds_connection_strings_only_in_Composition_Settings_and_the_shrinking_allowlist()
    {
        var desktop = Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop");
        var holders = Directory.EnumerateFiles(desktop, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(desktop, path))
            .Where(path => ConnectionStringMember.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(desktop, path).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("Composition/", StringComparison.Ordinal))
            .OrderBy(relative => relative, StringComparer.Ordinal)
            .ToArray();
        var allowed = ConnectionEditorFiles.Concat(ConnectionStringAllowlist).ToArray();

        var added = holders.Except(allowed, StringComparer.Ordinal).ToArray();
        Assert.True(added.Length == 0,
            "New connection string in a Desktop screen: " + string.Join(", ", added)
            + ". Take an Application service (Func<IService>) built by the composition root instead (1.12 A1).");

        var stale = ConnectionStringAllowlist.Except(holders, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "These files no longer hold a connection string; remove them from ConnectionStringAllowlist: " + string.Join(", ", stale));
    }

    [Theory]
    [InlineData("private readonly Func<string> connectionStringProvider;", true)]
    [InlineData("public View(Func<string> connection, Func<bool> owner)", true)]
    [InlineData("Task SaveAsync(string connectionString, Entry entry)", true)]
    [InlineData("private readonly Func<string, IDashboardQuery> factory;", true)]
    [InlineData("Func<string,AccountingService> accounting", true)]
    [InlineData("private readonly Func<IDashboardQuery> factory;", false)]
    [InlineData("Func<string, ReportPackDocument, Task> exportPackExcelAsync", false)]
    [InlineData("string ConnectionResult,", false)]
    [InlineData("private readonly DesktopConnectionState connectionState;", false)]
    public void The_connection_string_rule_matches_what_it_should(string line, bool matches) =>
        Assert.Equal(matches, ConnectionStringMember.IsMatch(line));

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
