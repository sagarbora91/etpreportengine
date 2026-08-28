using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReleaseVersionConsistencyTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Windows_version_matches_latest_changelog_release_and_release_consumers()
    {
        var propsPath = Path.Combine(RepositoryRoot, "Directory.Build.props");
        var version = XDocument.Load(propsPath)
            .Descendants("VersionPrefix")
            .Single()
            .Value
            .Trim();

        Assert.Matches(@"^\d+\.\d+\.\d+$", version);

        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot, "CHANGELOG.md"));
        var latestRelease = Regex.Match(
            changelog,
            @"(?m)^## \[(?<version>\d+\.\d+\.\d+)\] - \d{4}-\d{2}-\d{2}\s*$");
        Assert.True(latestRelease.Success, "CHANGELOG.md must contain a dated semantic-version release heading.");
        Assert.Equal(version, latestRelease.Groups["version"].Value);

        var windowsInstaller = Read("scripts", "build-windows-installer.ps1");
        var windowsRelease = Read("scripts", "build-windows-release.ps1");
        var offlinePackage = Read("scripts", "new-offline-deployment-package.ps1");
        Assert.Contains("Directory.Build.props", windowsInstaller, StringComparison.Ordinal);
        Assert.Contains("/DAppVersion=$version", windowsInstaller, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", windowsRelease, StringComparison.Ordinal);
        Assert.Contains("-p:Version=$Version", windowsRelease, StringComparison.Ordinal);
        Assert.Contains("Directory.Build.props", offlinePackage, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_versioning_isolated_from_android_identity()
    {
        var versionScript = Read("scripts", "set-release-version.ps1");
        Assert.Contains("Directory.Build.props", versionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("build-identity.js", versionScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("versionName", versionScript, StringComparison.Ordinal);
        Assert.DoesNotContain("versionCode", versionScript, StringComparison.Ordinal);

        var mobileIdentity = Read("www", "build-identity.js");
        Assert.Matches(@"versionName:\s*'6'", mobileIdentity);
        Assert.Matches(@"versionCode:\s*600", mobileIdentity);
    }

    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
