namespace Etp.Reporting.Desktop.Tests;

public sealed class CiWorkflowContractTests
{
    private static readonly string Workflow = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), ".github", "workflows", "ci.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Ci_runs_full_windows_and_security_verification_for_pushes_and_pull_requests()
    {
        Assert.Contains("on:\n  push:\n  pull_request:\n", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("branches: [main]", Workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", Workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet-version: 10.0.x", Workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet restore Etp.Reporting.slnx", Workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet build Etp.Reporting.slnx --configuration Release --no-restore", Workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test Etp.Reporting.slnx --configuration Release --no-build", Workflow, StringComparison.Ordinal);
        Assert.Contains("npm ci --ignore-scripts", Workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test:security", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_scan_is_fail_closed_and_preserves_json_evidence_on_failure()
    {
        Assert.Contains("./scripts/invoke-security-scan.ps1 -OutputPath artifacts/security-scan.json", Workflow, StringComparison.Ordinal);
        Assert.Contains("if: ${{ always() }}", Workflow, StringComparison.Ordinal);
        Assert.Contains("uses: actions/upload-artifact@v4", Workflow, StringComparison.Ordinal);
        Assert.Contains("path: artifacts/security-scan.json", Workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", Workflow, StringComparison.Ordinal);
        Assert.Contains("Language.Parser]::ParseFile", Workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --check", Workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_uses_only_the_approved_official_actions_and_makes_no_release_claims()
    {
        var actionReferences = Workflow.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("uses: ", StringComparison.Ordinal))
            .Select(line => line["uses: ".Length..])
            .ToArray();

        Assert.NotEmpty(actionReferences);
        Assert.All(actionReferences, action => Assert.StartsWith("actions/", action, StringComparison.Ordinal));
        Assert.DoesNotContain("installer", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signtool", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sbom", Workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sqlcmd", Workflow, StringComparison.OrdinalIgnoreCase);
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
