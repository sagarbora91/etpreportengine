using System.Diagnostics;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave-1 integration (review B-1, B-2, M-5, M-6 and A3's packaging note). Setup prepares the folders the wave-1 engines
/// write to while running unelevated: ShopResults\pending for the automation account, CollectionsEvidence (Owners; Store Managers create-only)
/// and TallyEvidence (Owners only, IR-1/IR-2), Updates for active Owners. Uninstall removes the shop results tasks, and the release
/// folder carries the new OneDrive scripts and database\objects. These read the shipped scripts and run their pure functions
/// with no SQL Server; the only ACLs they change are on a temp folder of their own.
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
    public async Task Setup_gives_tally_evidence_to_owners_only_and_collections_evidence_to_owners_with_create_only_store_managers()
    {
        // IR-1/IR-2 (decision 59). Pure functions only: no ACL is applied.
        var bootstrap = Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''", StringComparison.Ordinal);
        const string owner = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        const string manager = "S-1-5-21-1111111111-2222222222-3333333333-1002";
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{bootstrap}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $names = (@(Get-EtpEvidenceFolderNames)) -join ';'
            if ($names -cne 'CollectionsEvidence;TallyEvidence') { throw "Names: $names" }
            $sql = New-EtpActiveStoreManagersSql
            if ($sql -cne "SET NOCOUNT ON; SELECT windows_identity FROM dbo.application_users WHERE role_code='STORE_MANAGER' AND is_active=1 ORDER BY windows_identity;") { throw "SQL: $sql" }
            if ($sql -match '(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC|GRANT|ALTER|DROP)\b') { throw "Not read-only: $sql" }
            if (Get-Command New-EtpActiveEvidenceWritersSql -ErrorAction SilentlyContinue) { throw 'The Owner + Store Manager writers query must be gone.' }

            # A Store Manager who is also listed as an Owner, a well-known group and a blank are skipped.
            $rules = @(Get-EtpCollectionsEvidenceFolderRules -OwnerSids @('{{owner}}') -StoreManagerSids @('{{manager}}','{{owner}}','S-1-5-32-545','S-1-1-0','',$null,'{{manager}}'))
            $text = ($rules | ForEach-Object { "$($_.Sid)=$($_.Rights)" }) -join ';'
            $expected = 'S-1-5-18=FullControl;S-1-5-32-544=FullControl;{{owner}}=Modify;' +
                '{{manager}}=Traverse, CreateFiles, CreateDirectories, ReadAttributes, Synchronize;{{manager}}=ReadAttributes, Synchronize;' +
                'S-1-3-0=Modify, Synchronize;S-1-3-4=ReadAttributes, Synchronize'
            if ($text -cne $expected) { throw "Rules: $text" }
            # No Store Manager: still no listing for anyone but Owners and administrators.
            $none = (@(Get-EtpCollectionsEvidenceFolderRules -OwnerSids @() -StoreManagerSids @()) | ForEach-Object { $_.Sid }) -join ';'
            if ($none -cne 'S-1-5-18;S-1-5-32-544;S-1-3-0;S-1-3-4') { throw "Empty: $none" }

            # The access list as Windows would store it: the Store Manager can never list a folder or read a file.
            $acl = New-EtpFolderSecurity -Rules $rules
            if (-not $acl.AreAccessRulesProtected) { throw 'The list must not inherit from EtpReporting.' }
            $read = [int]([Security.AccessControl.FileSystemRights]::ReadData)
            foreach ($rule in @($acl.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]))) {
                $sid = $rule.IdentityReference.Value
                $rights = [int]$rule.FileSystemRights
                if (($sid -eq '{{manager}}' -or $sid -eq 'S-1-5-11' -or $sid -eq 'S-1-5-32-545' -or $sid -eq 'S-1-1-0') -and ($rights -band $read)) { throw "$sid can list or read: $($rule.FileSystemRights)" }
                if ($sid -eq '{{manager}}' -and ($rights -band [int][Security.AccessControl.FileSystemRights]::WriteData) -and $rule.InheritanceFlags -ne 'ContainerInherit') { throw 'Store Manager create rights must stay on folders.' }
                if ($sid -eq 'S-1-3-0' -and ($rule.InheritanceFlags -ne 'ObjectInherit' -or $rule.PropagationFlags -ne 'InheritOnly')) { throw 'CREATOR OWNER must apply to files only.' }
                if ($sid -eq 'S-1-3-4' -and ($rights -band ([int][Security.AccessControl.FileSystemRights]::ChangePermissions -bor [int][Security.AccessControl.FileSystemRights]::ReadPermissions))) { throw 'OWNER RIGHTS must not give permission rights.' }
            }
            # The HealthExports rules keep their old shape (folder, subfolders and files).
            $plain = New-EtpFolderSecurity -Rules @(Get-EtpHealthExportFolderRules -AutomationSid $null -OwnerSids @('{{owner}}'))
            $ownerRule = @($plain.GetAccessRules($true,$false,[Security.Principal.SecurityIdentifier]) | Where-Object { $_.IdentityReference.Value -eq '{{owner}}' })
            if ($ownerRule.Count -ne 1 -or $ownerRule[0].InheritanceFlags -ne 'ContainerInherit, ObjectInherit') { throw 'HealthExports-style rule changed.' }
            Write-Output 'Evidence folder functions passed.'
            """;
        var result = await RunCommandAsync(command);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Evidence folder functions passed.", result.Output, StringComparison.Ordinal);

        var text = Script("bootstrap-etp-prerequisites.ps1");
        var guard = text.IndexOf("if ($MyInvocation.InvocationName -eq '.') { return }", StringComparison.Ordinal);
        var functions = text.IndexOf("function Get-EtpCollectionsEvidenceFolderRules", StringComparison.Ordinal);
        var updates = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\Updates')", StringComparison.Ordinal);
        var collections = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\CollectionsEvidence') -Rules (Get-EtpCollectionsEvidenceFolderRules -OwnerSids $evidenceOwnerSids -StoreManagerSids $evidenceManagerSids) -ResetChildren", StringComparison.Ordinal);
        var tally = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\TallyEvidence') -Rules (Get-EtpHealthExportFolderRules -AutomationSid $null -OwnerSids $evidenceOwnerSids) -ResetChildren", StringComparison.Ordinal);
        var tasks = text.IndexOf("& (Join-Path $scripts 'install-daily-backup-task.ps1')", StringComparison.Ordinal);
        Assert.True(functions > 0 && functions < guard, "The functions must be dot-sourceable (above the guard).");
        Assert.True(updates > guard && collections > updates && tally > collections && tally < tasks, "The evidence folders are prepared after the Updates folder and before the tasks.");
        var block = text[text.LastIndexOf("try {", collections, StringComparison.Ordinal)..tasks];
        Assert.Contains("-Q (New-EtpActiveOwnersSql)", block, StringComparison.Ordinal);
        Assert.Contains("-Q (New-EtpActiveStoreManagersSql)", block, StringComparison.Ordinal);
        Assert.DoesNotContain("$evidenceManagerSids) -ResetChildren\r\n    Write-SetupLog \"Evidence folder ready at $tallyFolder", block, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnerSids $evidenceManagerSids", block, StringComparison.Ordinal);
        Assert.Contains("WARNING: setup could not prepare the evidence folders", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_setup_rerun_resets_everything_under_an_evidence_folder_to_inherit_and_is_idempotent()
    {
        // Existing setups get narrowed: explicit or protected entries below the folder (an earlier build, a manual change) go.
        // Runs unelevated on a temp folder the test account owns; it never touches ProgramData.
        var bootstrap = Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''", StringComparison.Ordinal);
        var root = Path.Combine(Path.GetTempPath(), "EtpEvidenceReset_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "BankSources", "SYN", "2026", "10"));
            await File.WriteAllTextAsync(Path.Combine(root, "BankSources", "SYN", "2026", "10", "a.csv"), "x");
            var quoted = root.Replace("'", "''", StringComparison.Ordinal);
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{bootstrap}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
                $file = Join-Path '{{quoted}}' 'BankSources\SYN\2026\10\a.csv'
                $folder = Join-Path '{{quoted}}' 'BankSources\SYN'
                # An earlier state: the file protected with its own entries, the folder with an extra explicit entry for Users.
                $acl = Get-Acl -LiteralPath $file
                $acl.SetAccessRuleProtection($true,$false)
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($me,'FullControl','Allow'))
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),'Read','Allow'))
                Set-Acl -LiteralPath $file -AclObject $acl
                $acl = Get-Acl -LiteralPath $folder
                $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),'Modify','ContainerInherit,ObjectInherit','None','Allow'))
                Set-Acl -LiteralPath $folder -AclObject $acl
                $first = Reset-EtpFolderChildrenToInherited -Path '{{quoted}}'
                if ($first -ne 2) { throw "First run reset $first entries." }
                foreach ($path in @($file,$folder)) {
                    $after = Get-Acl -LiteralPath $path
                    if ($after.AreAccessRulesProtected) { throw "$path is still protected." }
                    $own = @($after.Access | Where-Object { -not $_.IsInherited })
                    if ($own.Count -ne 0) { throw "$path keeps $($own.Count) explicit entries." }
                    if (@($after.Access | Where-Object { $_.IdentityReference.Value -like '*Users' -or $_.IdentityReference.Value -eq 'S-1-5-32-545' }).Count -ne 0) { throw "$path still lets Users in." }
                }
                $second = Reset-EtpFolderChildrenToInherited -Path '{{quoted}}'
                if ($second -ne 0) { throw "Second run reset $second entries." }
                Write-Output 'Reset passed.'
                """;
            var result = await RunCommandAsync(command);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            Assert.Contains("Reset passed.", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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
