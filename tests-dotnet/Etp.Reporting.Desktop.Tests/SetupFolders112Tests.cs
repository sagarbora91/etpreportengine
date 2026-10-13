using System.Diagnostics;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave-1 integration (review B-1, B-2, M-5, M-6 and A3's packaging note). Setup prepares the folders the wave-1 engines
/// write to while running unelevated: ShopResults\pending for the automation account, CollectionsEvidence and TallyEvidence
/// for active Owners and Store Managers, Updates for active Owners. Uninstall removes the shop results tasks, and the release
/// folder carries the new OneDrive scripts and database\objects. These read the shipped scripts and run their pure functions
/// with no SQL Server; they change no ACL.
/// </summary>
public sealed class SetupFolders112Tests
{
    private static string Script(string name) => File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", name));

    [Fact]
    public void Folder_setup_creates_the_shop_results_folders_after_the_operations_reset_and_gives_automation_modify_on_pending_only()
    {
        var text = Script("initialize-etp-operation-folders.ps1");
        var operations = text.IndexOf("Set-PrivateDirectory (Join-Path $root 'Operations') -ReadOnlyAutomation", StringComparison.Ordinal);
        var shopResults = text.IndexOf("$shopResults = Join-Path $root 'Operations\\ShopResults'", StringComparison.Ordinal);
        Assert.True(operations > 0 && shopResults > operations, "ShopResults is set after the Operations line, which resets every child.");
        Assert.Contains("foreach ($folder in @('pending','published','rejected'))", text, StringComparison.Ordinal);
        var block = text[shopResults..text.IndexOf("if ($GrantAutomationFolderAccess) {", shopResults, StringComparison.Ordinal)];
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(block, "AddAccessRule"));
        Assert.Contains("$pendingPath = Join-Path $shopResults 'pending'", block, StringComparison.Ordinal);
        Assert.Contains("FileSystemAccessRule]::new($automationSid,'Modify'", block, StringComparison.Ordinal);
        Assert.Contains("Assert-EtpNoLinks", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_gives_the_evidence_folders_to_active_owners_and_store_managers_with_the_health_exports_rule()
    {
        var bootstrap = Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{bootstrap}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $names = (@(Get-EtpEvidenceFolderNames)) -join ';'
            if ($names -cne 'CollectionsEvidence;TallyEvidence') { throw "Names: $names" }
            $sql = New-EtpActiveEvidenceWritersSql
            if ($sql -cne "SET NOCOUNT ON; SELECT windows_identity FROM dbo.application_users WHERE role_code IN ('OWNER','STORE_MANAGER') AND is_active=1 ORDER BY windows_identity;") { throw "SQL: $sql" }
            if ($sql -match '(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC|GRANT|ALTER|DROP)\b') { throw "Not read-only: $sql" }
            Write-Output 'Evidence folder functions passed.'
            """;
        var result = await RunCommandAsync(command);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Evidence folder functions passed.", result.Output, StringComparison.Ordinal);

        var text = Script("bootstrap-etp-prerequisites.ps1");
        var guard = text.IndexOf("if ($MyInvocation.InvocationName -eq '.') { return }", StringComparison.Ordinal);
        var functions = text.IndexOf("function Get-EtpEvidenceFolderNames", StringComparison.Ordinal);
        var updates = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\Updates')", StringComparison.Ordinal);
        var evidence = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData \"EtpReporting\\$evidenceName\")", StringComparison.Ordinal);
        var tasks = text.IndexOf("& (Join-Path $scripts 'install-daily-backup-task.ps1')", StringComparison.Ordinal);
        Assert.True(functions > 0 && functions < guard, "The functions must be dot-sourceable (above the guard).");
        Assert.True(updates > guard && evidence > updates && evidence < tasks, "The evidence folders are prepared after the Updates folder and before the tasks.");
        Assert.Contains("Get-EtpHealthExportFolderRules -AutomationSid $null -OwnerSids $evidenceSids", text, StringComparison.Ordinal);
        var block = text[text.LastIndexOf("try {", evidence, StringComparison.Ordinal)..tasks];
        Assert.Contains("WARNING: setup could not prepare the evidence folders", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Uninstall_removes_the_shop_results_tasks_and_the_release_carries_the_new_scripts_and_the_object_files()
    {
        var remove = Script("remove-etp-scheduled-tasks.ps1");
        Assert.Contains("\"ETP Publish Shop Results\"", remove, StringComparison.Ordinal);
        Assert.Contains("\"ETP Show Shop Results\"", remove, StringComparison.Ordinal);

        var release = Script("build-windows-release.ps1");
        foreach (var file in new[] { "'ShopResultCommon.ps1'", "'Publish-ShopResults.ps1'", "'Show-ShopResults.ps1'" })
            Assert.Contains(file, release, StringComparison.Ordinal);
        Assert.Contains("foreach ($databaseFolder in 'database\\migrations','database\\objects')", release, StringComparison.Ordinal);

        var desktop = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Etp.Reporting.Desktop.csproj"));
        Assert.Contains("<Content Include=\"..\\..\\database\\objects\\**\\*.sql\" Link=\"database\\objects\\%(RecursiveDir)%(Filename)%(Extension)\"", desktop, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }
}
