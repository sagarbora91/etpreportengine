namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// P4-10 regression. The desktop project copied the entire repository scripts tree
/// into its output, so a release shipped build, signing, release-rollback,
/// security-scan and emulator tooling onto shop machines, and the release signature
/// was applied to it. What sits beside the application is what an operator can run.
/// </summary>
public sealed class ShippedScriptsTests
{
    private static string ScriptsDirectory => Path.Combine(AppContext.BaseDirectory, "scripts");

    private static string[] ShippedNames() =>
        Directory.Exists(ScriptsDirectory)
            ? Directory.GetFiles(ScriptsDirectory).Select(Path.GetFileName).OfType<string>().ToArray()
            : [];

    [Theory]
    // The three the application itself is allowed to run at runtime.
    [InlineData("backup-etp-database.ps1")]
    [InlineData("invoke-etp-recovery-drill.ps1")]
    [InlineData("new-etp-support-package.ps1")]
    // Dot-sourced by all of the above; without it they fail at first line.
    [InlineData("etp-operations-common.ps1")]
    // Run by the installer after files are placed, and by the uninstaller.
    [InlineData("bootstrap-etp-prerequisites.ps1")]
    [InlineData("remove-etp-scheduled-tasks.ps1")]
    public void Operational_scripts_ship_beside_the_application(string name)
        => Assert.Contains(name, ShippedNames());

    [Fact]
    public void The_broker_template_ships_under_its_sql_folder()
        => Assert.True(File.Exists(Path.Combine(ScriptsDirectory, "sql", "etp-operations-broker.sql")));

    [Theory]
    // Build and release tooling. None of this has any business on a till machine,
    // and sign-etp-artifacts in particular should never travel with the product.
    [InlineData("build-windows-installer.ps1")]
    [InlineData("build-windows-release.ps1")]
    [InlineData("build-production-release.ps1")]
    [InlineData("build-installer.ps1")]
    [InlineData("sign-etp-artifacts.ps1")]
    [InlineData("invoke-release-rollback.ps1")]
    [InlineData("invoke-security-scan.ps1")]
    [InlineData("set-release-version.ps1")]
    [InlineData("new-offline-deployment-package.ps1")]
    [InlineData("android-emulator.ps1")]
    [InlineData("test-etp-operations-boundaries.ps1")]
    [InlineData("test-release-evidence-consistency.ps1")]
    [InlineData("Invoke-EtpFunctionAudit.ps1")]
    public void Developer_tooling_never_ships_with_the_application(string name)
        => Assert.DoesNotContain(name, ShippedNames());

    [Fact]
    public void No_build_helpers_of_any_other_kind_ship_either()
    {
        // The repository scripts folder also holds .mjs build helpers and a .cjs shim.
        var strays = ShippedNames()
            .Where(x => !x.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(strays.Length == 0, $"Unexpected non-PowerShell payload shipped: {string.Join(", ", strays)}");
    }
}
