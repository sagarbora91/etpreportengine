using System.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class TallyExportPathAuditTests
{
    [Fact]
    public async Task Tally_export_refuses_a_linked_ancestor_before_creating_directories_or_files()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), "EtpTallyPathBoundary", Guid.NewGuid().ToString("N"));
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var link = Path.Combine(root, "linked");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add($"$ErrorActionPreference = 'Stop'; New-Item -ItemType Junction -Path '{link.Replace("'", "''")}' -Target '{outside.Replace("'", "''")}' | Out-Null");
        using (var process = Process.Start(start)!)
        {
            Assert.True(process.WaitForExit(15000), "Temporary junction creation timed out.");
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            var batch = new AccountingBatchDraft([
                new(1, "ADJUSTMENT", "Expense", 1, 0, "Fixture", null, "fixture"),
                new(2, "ADJUSTMENT", "Control", 0, 1, "Fixture", null, "fixture")], 1, 1, true, []);
            var path = Path.Combine(link, "new-subdirectory", "batch.xml");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TallyXmlExportService().ExportAsync(path, "TEST Fixture", new(2026, 8, 25), batch));
            Assert.Contains("linked", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }
}
