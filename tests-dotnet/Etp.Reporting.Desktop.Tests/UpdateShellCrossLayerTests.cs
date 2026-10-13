using System.Diagnostics;
using System.Text;
using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (B2), cross-layer: "Check now" against a temporary feed folder that Windows PowerShell 5.1 filled with the
/// signing module (a throwaway key, made and deleted here). The coordinator, given the folder as the saved update folder,
/// shows the banner and the Settings sentence for the version the PowerShell side published, and says "files ETP will not
/// trust" once release.json is edited after signing. The compiled-in key list is empty in the repository, so the check is
/// given the throwaway key through the same <c>UpdateFeedSelector.Choose</c> overload the engine's check uses.
/// </summary>
public sealed class UpdateShellCrossLayerTests
{
    private static string Module => Path.Combine(RepositoryRoot(), "scripts", "release", "EtpReleaseSigning.psm1").Replace("'", "''");

    private sealed class MemoryStore(UpdateSettings value) : IUpdateSettingsStore
    {
        public Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task SaveAsync(UpdateSettings settings, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Check_now_shows_the_version_PowerShell_published_and_notices_a_release_edited_after_signing()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            var feed = Directory.CreateDirectory(Path.Combine(root, "ETP-Updates")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(source, "EtpReportingEngine-Setup-1.12.1-x64.exe"), new byte[2048]);
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                Import-Module '{{Module}}' -Force
                $source = '{{source.Replace("'", "''")}}'; $feed = '{{feed.Replace("'", "''")}}'
                $pass = ConvertTo-SecureString ([Guid]::NewGuid().ToString('N')) -AsPlainText -Force
                $key = New-EtpReleaseSigningKeyFileContent -Passphrase $pass -Iterations 10000
                $keyPath = Join-Path (Split-Path $source) 'throwaway-key.json'
                [IO.File]::WriteAllText($keyPath, $key.Content)
                $setup = Join-Path $source 'EtpReportingEngine-Setup-1.12.1-x64.exe'
                $text = ConvertTo-EtpReleaseManifestText -Version '1.12.1' -InstallerBytes (Get-Item $setup).Length -Sha256 (Get-FileHash $setup -Algorithm SHA256).Hash `
                    -Commit '9d9ae21' -SchemaCompatLevel 70 -HasSchemaChange $true -MinimumFromVersion '1.12.0' -Notes 'Synthetic: the Morning page loads faster.' -KeyId $key.KeyId
                [IO.File]::WriteAllText((Join-Path $source 'release.json'), $text, (New-Object Text.UTF8Encoding($false)))
                New-EtpReleaseSignature -ManifestPath (Join-Path $source 'release.json') -KeyPath $keyPath -Passphrase $pass | Out-Null
                Copy-EtpReleaseToFeed -SourceFolder $source -Version '1.12.1' -FeedRoot $feed -PublicKey $key.PublicKey | Out-Null
                Write-Output ('KEY ' + $key.KeyId + ' ' + $key.PublicKey)
                """;
            var result = await RunPowerShellAsync(command);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            var keyLine = result.Output.Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("KEY ", StringComparison.Ordinal)).Split(' ');
            var trusted = new ReleaseSigningKey(keyLine[1], keyLine[2]);

            var banners = new List<UpdateBannerModel?>();
            var coordinator = new UpdateShellCoordinator(new UpdateShellServices(
                Settings: new MemoryStore(new UpdateSettings(feed, false)),
                Running: () => new ReleaseVersion(1, 12, 0),
                IsOwner: () => true,
                Check: (running, path) => UpdateFeedSelector.Choose(running, UpdateFeedReader.Read(path), [trusted]),
                Gate: new UpdateGateProbe(new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false), () => false, () => false, () => false, () => false, () => false, () => true, _ => Task.FromResult(false))),
                Install: (_, _, _, _, _) => throw new InvalidOperationException("not used"),
                ResolvePending: _ => null,
                ShowBanner: banners.Add,
                Confirm: _ => Task.FromResult(false),
                ShowResult: _ => { },
                SetStatus: _ => { },
                CloseApplication: () => { },
                NowUtc: () => new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero)));

            // Checks are OFF (the switch), and Check now still finds the release: the banner appears.
            var choice = await coordinator.CheckAsync();
            Assert.Equal(UpdateFeedStatus.UpdateAvailable, choice!.Status);
            var banner = Assert.Single(banners)!;
            Assert.Equal("ETP 1.12.1 is available", banner.Title);
            Assert.Equal("Synthetic: the Morning page loads faster.", banner.Body);

            var summary = UpdateScreenText.LastCheck(coordinator.LastChoice, coordinator.Running, coordinator.LastCheckedUtc, TimeZoneInfo.Utc);
            Assert.Equal(("Update available", UpdateTextTone.Information), (summary.Word, summary.Tone));
            Assert.Equal("ETP 1.12.1 is ready to install. Checked at 09:30 on 14 Oct 2026.", summary.Sentence);
            Assert.Null(summary.Extra);

            // The confirm sheet for that release lists the schema change, and (checks being off) refuses with the Settings sentence.
            var gate = UpdateGate.Evaluate(await new UpdateGateProbe(new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false), () => false, () => false, () => false, () => false, () => false, () => true, _ => Task.FromResult(false)))
                .ReadAsync(CancellationToken.None));
            var sheet = UpdateScreenText.Confirm(choice.Offer!, gate);
            Assert.False(sheet.CanInstall);
            Assert.Equal("Update checks are turned off. An Owner can turn them on in Settings > Software update.", Assert.Single(sheet.Blocks));
            Assert.Contains(sheet.Steps, step => step.Contains("changes the database", StringComparison.Ordinal));

            // Someone edits release.json after it was signed: the release is ignored and the Settings screen says why.
            var manifestPath = Path.Combine(feed, "1.12.1", "release.json");
            var edited = (await File.ReadAllTextAsync(manifestPath)).Replace("Morning page", "Morning pages", StringComparison.Ordinal);
            await File.WriteAllTextAsync(manifestPath, edited, new UTF8Encoding(false));
            await coordinator.CheckAsync();
            Assert.Null(banners[^1]);
            var after = UpdateScreenText.LastCheck(coordinator.LastChoice, coordinator.Running, coordinator.LastCheckedUtc, TimeZoneInfo.Utc);
            Assert.Equal("The update folder holds files ETP will not trust. ETP ignored them. Tell Sagar.", after.Extra);
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
