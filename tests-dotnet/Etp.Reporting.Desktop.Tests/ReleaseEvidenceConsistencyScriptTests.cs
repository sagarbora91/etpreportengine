using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReleaseEvidenceConsistencyScriptTests
{
    private const string SourceCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(RepositoryRoot, "scripts", "test-release-evidence-consistency.ps1");
    private static readonly string RealApplicationPath = Path.Combine(AppContext.BaseDirectory, "Etp.Reporting.Desktop.exe");
    private static readonly string Version = ReadEmbeddedReleaseVersion(RealApplicationPath);

    [Fact]
    public void Consistent_synthetic_release_evidence_passes()
    {
        using var fixture = SyntheticEvidenceFixture.Create();

        var result = RunValidator(fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Release evidence is internally consistent", result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.StandardError.Trim());
    }

    [Fact]
    public void Wrong_source_commit_fails_closed()
    {
        using var fixture = SyntheticEvidenceFixture.Create(cycloneCommit: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CycloneDX base commit mismatch", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_application_hash_fails_closed()
    {
        using var fixture = SyntheticEvidenceFixture.Create(provenanceApplicationHash: new string('0', 64));

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Provenance artifact 'self-contained-application' SHA-256 mismatch", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, false, "Provenance worktreeCleanAtBuild")]
    [InlineData(false, true, "CycloneDX source-worktree-clean mismatch")]
    public void Dirty_source_flags_fail_closed(bool dirtyProvenance, bool dirtyCycloneDx, string expectedMessage)
    {
        using var fixture = SyntheticEvidenceFixture.Create(
            provenanceClean: !dirtyProvenance,
            cycloneClean: !dirtyCycloneDx);

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedMessage, result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Unidentified_optional_artifact_evidence_fails_closed()
    {
        using var fixture = SyntheticEvidenceFixture.Create();

        var result = RunValidator(fixture, includeOptionalArguments: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Provenance artifact count mismatch", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_application_version_mismatch_fails_closed()
    {
        var mismatchedVersion = Version == "9.8.7" ? "9.8.6" : "9.8.7";
        using var fixture = SyntheticEvidenceFixture.Create(evidenceVersion: mismatchedVersion);

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("embedded ProductVersion mismatch", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_application_version_prefix_collision_fails_closed()
    {
        var parts = Version.Split('.');
        var patch = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        var prefixCollisionVersion = $"{parts[0]}.{parts[1]}.{patch * 10 + (patch == 0 ? 1 : 0)}";
        using var fixture = SyntheticEvidenceFixture.Create(evidenceVersion: prefixCollisionVersion);

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("embedded ProductVersion mismatch", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_pe_application_fails_closed_even_when_hash_evidence_matches()
    {
        using var fixture = SyntheticEvidenceFixture.Create(invalidApplication: true);

        var result = RunValidator(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not a valid readable PE file", result.CombinedOutput, StringComparison.Ordinal);
    }

    private static ValidatorResult RunValidator(SyntheticEvidenceFixture fixture, bool includeOptionalArguments = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath);
        AddArgument(startInfo, "-Version", fixture.EvidenceVersion);
        AddArgument(startInfo, "-SourceCommit", SourceCommit);
        AddArgument(startInfo, "-ReleaseApplicationPath", fixture.ApplicationPath);
        if (includeOptionalArguments)
        {
            AddArgument(startInfo, "-InstallerPath", fixture.InstallerPath);
            AddArgument(startInfo, "-OfflinePackagePath", fixture.OfflinePackagePath);
        }
        AddArgument(startInfo, "-ProvenancePath", fixture.ProvenancePath);
        AddArgument(startInfo, "-CycloneDxPath", fixture.CycloneDxPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Release evidence validator did not complete within 30 seconds.");
        }

        Task.WaitAll(standardOutput, standardError);
        return new ValidatorResult(process.ExitCode, standardOutput.Result, standardError.Result);
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }

    private static string ReadEmbeddedReleaseVersion(string path)
    {
        Assert.True(File.Exists(path), $"A built Desktop PE fixture is required at '{path}'.");
        var productVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
        Assert.False(string.IsNullOrWhiteSpace(productVersion));
        var core = productVersion.Split('+', 2)[0];
        Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+$", core);
        return core;
    }

    private sealed record ValidatorResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}{Environment.NewLine}{StandardError}";
    }

    private sealed class SyntheticEvidenceFixture : IDisposable
    {
        private SyntheticEvidenceFixture(string root, string evidenceVersion)
        {
            Root = root;
            EvidenceVersion = evidenceVersion;
        }

        public string Root { get; }
        public string EvidenceVersion { get; }
        public string ApplicationPath => Path.Combine(Root, "Etp.Reporting.Desktop.exe");
        public string InstallerPath => Path.Combine(Root, $"EtpReportingEngine-Setup-{EvidenceVersion}-x64.exe");
        public string OfflinePackagePath => Path.Combine(Root, $"EtpReportingEngine-Offline-{EvidenceVersion}.zip");
        public string ProvenancePath => Path.Combine(Root, $"etp-reporting-engine-{EvidenceVersion}.provenance.json");
        public string CycloneDxPath => Path.Combine(Root, $"etp-reporting-engine-{EvidenceVersion}.cdx.json");

        public static SyntheticEvidenceFixture Create(
            string? cycloneCommit = null,
            string? provenanceApplicationHash = null,
            bool provenanceClean = true,
            bool cycloneClean = true,
            string? evidenceVersion = null,
            bool invalidApplication = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "EtpReleaseEvidenceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var fixture = new SyntheticEvidenceFixture(root, evidenceVersion ?? Version);

            if (invalidApplication)
                File.WriteAllText(fixture.ApplicationPath, "not a portable executable");
            else
                File.Copy(RealApplicationPath, fixture.ApplicationPath);
            File.WriteAllText(fixture.InstallerPath, "synthetic installer");
            File.WriteAllText(fixture.OfflinePackagePath, "synthetic offline package");

            var applicationHash = Sha256(fixture.ApplicationPath);
            var artifacts = new object[]
            {
                Artifact("self-contained-application", fixture.ApplicationPath, provenanceApplicationHash ?? applicationHash),
                Artifact("bootstrap-installer", fixture.InstallerPath, Sha256(fixture.InstallerPath)),
                Artifact("offline-deployment-package", fixture.OfflinePackagePath, Sha256(fixture.OfflinePackagePath)),
            };
            var provenance = new
            {
                schema = "etp-release-evidence/v1",
                product = "ETP Reporting Engine",
                version = fixture.EvidenceVersion,
                runtime = "win-x64",
                source = new
                {
                    releaseSourceCommit = SourceCommit,
                    worktreeCleanAtBuild = provenanceClean,
                    exactCommittedSourceIdentityAvailable = true,
                },
                artifacts,
                sbom = new
                {
                    path = Path.GetFileName(fixture.CycloneDxPath),
                    format = "CycloneDX",
                    specVersion = "1.6",
                    components = 1,
                },
            };

            var component = new Dictionary<string, object>
            {
                ["type"] = "application",
                ["bom-ref"] = $"ETP Reporting Engine@{fixture.EvidenceVersion}",
                ["name"] = "ETP Reporting Engine",
                ["version"] = fixture.EvidenceVersion,
                ["properties"] = new object[]
                {
                    Property("etp:base-commit", cycloneCommit ?? SourceCommit),
                    Property("etp:source-worktree-clean", cycloneClean ? "true" : "false"),
                    Property("etp:runtime", "win-x64"),
                    Property("etp:artifact-sha256", applicationHash),
                },
            };
            var cycloneDx = new
            {
                bomFormat = "CycloneDX",
                specVersion = "1.6",
                version = 1,
                metadata = new { component },
                components = new[] { new { type = "library", name = "Synthetic.Dependency", version = "1.0.0" } },
            };

            WriteJson(fixture.ProvenancePath, provenance);
            WriteJson(fixture.CycloneDxPath, cycloneDx);
            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static object Artifact(string role, string path, string sha256) => new
        {
            role,
            path = Path.GetFileName(path),
            bytes = new FileInfo(path).Length,
            sha256,
        };

        private static object Property(string name, string value) => new { name, value };

        private static string Sha256(string path) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private static void WriteJson(string path, object value) =>
            File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
