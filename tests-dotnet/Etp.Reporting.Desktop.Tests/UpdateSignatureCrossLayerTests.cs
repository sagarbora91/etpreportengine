using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1, plan Appendix C 3): one signature format, two languages. Windows PowerShell 5.1 (the shell Sagar publishes
/// from) makes a THROWAWAY key, writes release.json with scripts\release\EtpReleaseSigning.psm1, signs it and copies the
/// release into a temporary feed in the agreed order; C# then reads that feed, verifies the signature with
/// ReleaseSignatureVerifier, offers the release, stages it, and refuses it once the installer or release.json is changed.
/// The reverse direction (a C# signature checked by the PowerShell verifier) is checked too. The key, its passphrase and
/// every file live in a temporary folder that is deleted; nothing is written to the repository.
/// </summary>
public sealed class UpdateSignatureCrossLayerTests
{
    private static string Module => Path.Combine(RepositoryRoot(), "scripts", "release", "EtpReleaseSigning.psm1").Replace("'", "''");

    [Fact]
    public async Task A_release_signed_and_published_by_PowerShell_is_verified_offered_and_staged_by_CSharp_and_refused_once_tampered()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var feed = Directory.CreateDirectory(Path.Combine(root, "ETP-Updates")).FullName;
            var updates = Directory.CreateDirectory(Path.Combine(root, "Updates")).FullName;
            var installer = RandomNumberGenerator.GetBytes(100_000);
            await File.WriteAllBytesAsync(Path.Combine(source, "EtpReportingEngine-Setup-1.12.0-x64.exe"), installer);

            var command = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module '{{Module}}' -Force
                $source = '{{source.Replace("'", "''")}}'; $feed = '{{feed.Replace("'", "''")}}'
                # A throwaway key and passphrase, made here and deleted with the temporary folder.
                $pass = ConvertTo-SecureString ([Guid]::NewGuid().ToString('N')) -AsPlainText -Force
                $key = New-EtpReleaseSigningKeyFileContent -Passphrase $pass -Iterations 10000
                $keyPath = Join-Path (Split-Path $source) 'throwaway-key.json'
                [IO.File]::WriteAllText($keyPath, $key.Content)
                $setup = Join-Path $source 'EtpReportingEngine-Setup-1.12.0-x64.exe'
                $text = ConvertTo-EtpReleaseManifestText -Version '1.12.0' -InstallerBytes (Get-Item $setup).Length -Sha256 (Get-FileHash $setup -Algorithm SHA256).Hash `
                    -Commit '9d9ae21' -SchemaCompatLevel 65 -HasSchemaChange $true -MinimumFromVersion '1.11.0' -Notes 'Synthetic notes: "quoted", <angle> & accents e' -KeyId $key.KeyId
                [IO.File]::WriteAllText((Join-Path $source 'release.json'), $text, (New-Object Text.UTF8Encoding($false)))
                $signedBy = New-EtpReleaseSignature -ManifestPath (Join-Path $source 'release.json') -KeyPath $keyPath -Passphrase $pass
                if ($signedBy -cne $key.KeyId) { throw 'Signed with another key id.' }
                $published = Copy-EtpReleaseToFeed -SourceFolder $source -Version '1.12.0' -FeedRoot $feed -PublicKey $key.PublicKey
                # A published version is never replaced.
                $refused = $false
                try { Copy-EtpReleaseToFeed -SourceFolder $source -Version '1.12.0' -FeedRoot $feed -PublicKey $key.PublicKey | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw 'A published release was replaced.' }
                # A wrong passphrase signs nothing.
                $wrong = ConvertTo-SecureString 'not-the-passphrase' -AsPlainText -Force
                $denied = $false
                try { New-EtpReleaseSignature -ManifestPath (Join-Path $source 'release.json') -KeyPath $keyPath -Passphrase $wrong -SignaturePath (Join-Path $source 'x.sig') | Out-Null } catch { $denied = $true }
                if (-not $denied -or (Test-Path (Join-Path $source 'x.sig'))) { throw 'A wrong passphrase signed.' }
                Write-Output ('KEY ' + $key.KeyId + ' ' + $key.PublicKey)
                """;
            var result = await RunPowerShellAsync(command);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            var keyLine = result.Output.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("KEY ", StringComparison.Ordinal)).Split(' ');
            var trusted = new ReleaseSigningKey(keyLine[1], keyLine[2]);

            // The feed holds exactly what the update reads, and the C# verifier accepts PowerShell's signature.
            var published = Path.Combine(feed, "1.12.0");
            Assert.Equal(new[] { "EtpReportingEngine-Setup-1.12.0-x64.exe", "release.json", "release.json.sig" },
                Directory.GetFiles(published).Select(p => Path.GetFileName(p)).Order(StringComparer.Ordinal));
            var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(published, "release.json"));
            Assert.False(manifestBytes is [0xEF, 0xBB, 0xBF, ..], "release.json is written without a byte order mark.");
            var signatureBytes = await File.ReadAllBytesAsync(Path.Combine(published, "release.json.sig"));
            Assert.Equal(ReleaseSignatureOutcome.Verified, ReleaseSignatureVerifier.Verify(manifestBytes, signatureBytes, [trusted]).Outcome);

            var choice = UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), UpdateFeedReader.Read(feed), [trusted]);
            Assert.Equal(UpdateFeedStatus.UpdateAvailable, choice.Status);
            var offer = choice.Offer!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(installer)), offer.InstallerSha256);
            Assert.Equal(installer.Length, offer.InstallerBytes);
            Assert.Equal(new ReleaseVersion(1, 11, 0), offer.MinimumFromVersion);
            Assert.Equal("Synthetic notes: \"quoted\", <angle> & accents e", offer.Notes);
            Assert.Equal(trusted.KeyId, offer.KeyId);

            var staged = await UpdateInstallerStager.StageAsync(published, offer, updates, CancellationToken.None, _ => { }, _ => "1.12.0");
            Assert.True(staged.Succeeded, staged.Problem.ToString());
            staged.Staged!.Dispose();

            // Someone with the shared OneDrive account swaps the installer: the release still verifies, the local copy does not.
            var swapped = installer.ToArray();
            swapped[^1] ^= 0x01;
            await File.WriteAllBytesAsync(Path.Combine(published, offer.InstallerFileName), swapped);
            Assert.Equal(StageProblem.HashMismatch,
                (await UpdateInstallerStager.StageAsync(published, offer, updates, CancellationToken.None, _ => { }, _ => "1.12.0")).Problem);

            // ... or edits release.json to match the new installer: the signature no longer verifies, nothing is offered.
            var edited = Encoding.UTF8.GetString(manifestBytes).Replace(offer.InstallerSha256, Convert.ToHexString(SHA256.HashData(swapped)), StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(published, "release.json"), edited, new UTF8Encoding(false));
            var tampered = UpdateFeedSelector.Choose(new ReleaseVersion(1, 11, 3), UpdateFeedReader.Read(feed), [trusted]);
            Assert.Equal(UpdateFeedStatus.UpToDate, tampered.Status);
            Assert.True(tampered.HasUntrustedEntries);
            Assert.Equal(UpdateFeedRejection.SignatureNotValid, Assert.Single(tampered.Findings).Rejection);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task A_signature_made_in_CSharp_verifies_in_PowerShell_and_a_changed_file_does_not()
    {
        var root = NewTemporaryDirectory();
        var (key, trusted) = NewKey();
        try
        {
            using (key)
            {
                var manifest = Manifest("1.12.0", trusted.KeyId);
                await File.WriteAllBytesAsync(Path.Combine(root, "release.json"), manifest);
                await File.WriteAllBytesAsync(Path.Combine(root, "release.json.sig"), Sign(key, trusted.KeyId, manifest));
                await File.WriteAllBytesAsync(Path.Combine(root, "changed.json"), [.. manifest, (byte)'\n']);
                await File.WriteAllBytesAsync(Path.Combine(root, "changed.json.sig"), Sign(key, trusted.KeyId, manifest));
            }
            var folder = root.Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module '{{Module}}' -Force
                $good = Test-EtpReleaseSignature -ManifestPath '{{folder}}\release.json' -PublicKey '{{trusted.PublicKey}}'
                $changed = Test-EtpReleaseSignature -ManifestPath '{{folder}}\changed.json' -PublicKey '{{trusted.PublicKey}}'
                $id = Get-EtpReleaseKeyId -PublicPoint ([Convert]::FromBase64String('{{trusted.PublicKey}}'))
                Write-Output "GOOD=$good CHANGED=$changed ID=$id"
                """;
            var result = await RunPowerShellAsync(command);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            Assert.Contains($"GOOD=True CHANGED=False ID={trusted.KeyId}", result.Output, StringComparison.Ordinal);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task The_key_ceremony_refuses_folders_that_are_not_on_two_removable_drives_before_asking_anything()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var script = Path.Combine(RepositoryRoot(), "scripts", "release", "New-EtpReleaseSigningKey.ps1").Replace("'", "''");
            var first = Path.Combine(root, "a").Replace("'", "''");
            var second = Path.Combine(root, "b").Replace("'", "''");
            var result = await RunPowerShellAsync($"& '{script}' -UsbFolder '{first}','{second}'");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("is not on a removable drive", result.Output + result.Error, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { DeleteDirectory(root); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }
}
