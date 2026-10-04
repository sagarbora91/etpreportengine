using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// Pins the Service read model wiring. Lane L5 ships the screens before lane L4 ships
/// SqlServerServiceReportQuery, so the composition root carries a marked integration step. Once the
/// class exists, the composition root must wire it, or every Service screen says "not available".
/// </summary>
public sealed class ServiceCompositionPinTests
{
    private const string QueryTypeName = "Etp.Reporting.Infrastructure.SqlServer.SqlServerServiceReportQuery";

    [Fact]
    public void The_composition_root_wires_the_Service_read_model_once_it_exists()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var code = string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var wired = Regex.IsMatch(code, @"window\.serviceReportQuery\s*=\s*\(\)\s*=>\s*new\s+SqlServerServiceReportQuery\(");

        var sqlServer = Assembly.Load("Etp.Reporting.Infrastructure.SqlServer");
        var exists = sqlServer.GetType(QueryTypeName, throwOnError: false) is not null
            || sqlServer.GetTypes().Any(type => type.Name == "SqlServerServiceReportQuery");

        if (exists)
            Assert.True(wired, "SqlServerServiceReportQuery exists but DesktopCompositionRoot does not assign window.serviceReportQuery to it.");
        else
            Assert.Contains("INTEGRATION STEP (Service interim, lane L4 + L5)", source, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }
}
