using System.Text.Json;
using Etp.Reporting.Desktop;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.Morning;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 U1: the Morning page's file readers (shop result folder, off-PC backup age by OPS-10's rule) and the
/// "Publish shop results now" task runner's sentences. No SQL; temp folders only, synthetic names.
/// </summary>
public sealed class MorningInfrastructureTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 10, 9);
    private readonly string root = Path.Combine(Path.GetTempPath(), "EtpMorningTests", Guid.NewGuid().ToString("N"));

    public MorningInfrastructureTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Shop_results_are_read_from_pending_and_published_for_the_day_only()
    {
        Write("pending", "TITAN-2026-10-09-1.json", "Accepted", "titan-r025.xlsx");
        Write("published", "HELIOS-2026-10-09-2.json", "Refused", "helios-daily.xlsx", "helios-r022.xlsx");
        Write("published", "HELIOS-2026-10-08-1.json", "Accepted", "old.xlsx");
        Write("published", "NOTAROLE-2026-10-09-1.json", "Accepted", "x.xlsx");
        File.WriteAllText(Path.Combine(root, "published", "SERVICE-2026-10-09-1.json"), "{ not json");

        var results = new ShopResultFolder(root).Read(Day).OrderBy(result => result.Role).ToArray();

        Assert.Equal(["HELIOS", "TITAN"], results.Select(result => result.Role));
        Assert.Equal((true, 2, "Refused"), (results[0].Published, results[0].Sequence, results[0].Overall));
        Assert.Equal(["helios-daily.xlsx", "helios-r022.xlsx"], results[0].FileNames);
        Assert.Equal((false, "Accepted"), (results[1].Published, results[1].Overall));
    }

    [Fact]
    public void A_missing_results_folder_is_no_results_not_an_error()
    {
        Assert.Empty(new ShopResultFolder(Path.Combine(root, "absent")).Read(Day));
    }

    [Fact]
    public void Off_pc_age_follows_the_OPS10_rule()
    {
        var oneDrive = Path.Combine(root, "OneDrive");
        var now = new DateTimeOffset(2026, 10, 10, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal((false, (double?)null), OffPcBackupAge.Read(new FakeOffPc(null), now));
        Directory.CreateDirectory(oneDrive);
        Assert.Equal((false, (double?)null), OffPcBackupAge.Read(new FakeOffPc(oneDrive), now));

        var folder = Directory.CreateDirectory(Path.Combine(oneDrive, OffPcCopyProbe.BackupFolderName)).FullName;
        Assert.Equal((true, (double?)null), OffPcBackupAge.Read(new FakeOffPc(oneDrive), now));

        var copy = Path.Combine(folder, "EtpReporting-20261009.bak.enc");
        File.WriteAllText(copy, "synthetic");
        File.SetLastWriteTimeUtc(copy, now.UtcDateTime.AddHours(-30));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a copy");
        Assert.Equal((true, (double?)30), OffPcBackupAge.Read(new FakeOffPc(oneDrive), now));
    }

    [Theory]
    [InlineData("ETP_TASK:RESULT:0", 0, true, "Every waiting result was signed")]
    [InlineData("ETP_TASK:RESULT:2", 0, false, "Done, but a result could not be published")]
    [InlineData("ETP_TASK:RESULT:1", 0, false, "Publishing stopped with an error")]
    [InlineData("ETP_TASK:RESULT:267011", 0, false, "Windows did not run the task")]
    [InlineData("ETP_TASK:RESULT:5", 0, false, "The publish task ended with code 5")]
    [InlineData("ETP_TASK:NOT_FOUND", 0, false, "is not set up on this PC")]
    [InlineData("ETP_TASK:DENIED", 0, false, "Windows did not let this account start")]
    [InlineData("ETP_TASK:RUNNING", 0, true, "still running after 2 minutes")]
    [InlineData("", 1, false, "could not be started (PowerShell ended with code 1)")]
    [InlineData("noise\r\nETP_TASK:RESULT:0\r\n", 0, true, "Every waiting result")]
    public void Every_task_result_is_a_sentence_never_a_bare_code(string output, int exitCode, bool succeeded, string phrase)
    {
        var result = ScheduledTaskOutcome.Interpret(output, exitCode);
        Assert.Equal(succeeded, result.Succeeded);
        Assert.Contains(phrase, result.Message, StringComparison.Ordinal);
        Assert.EndsWith(".", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_publish_task_can_be_started_and_its_name_is_the_only_value_in_the_script()
    {
        Assert.Equal(ScheduledTaskOutcome.PublishShopResultsTask, Assert.Single(PowerShellOperationsService.StartableTasks));
        var script = ScheduledTaskOutcome.StartScript(ScheduledTaskOutcome.PublishShopResultsTask);
        Assert.Contains("$name='ETP Publish Shop Results'", script, StringComparison.Ordinal);
        Assert.Contains("Start-ScheduledTask -TaskName $name", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Register-ScheduledTask", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-ScheduledTask", script, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ScheduledTaskOutcome.StartScript("x'; Remove-Item C:\\"));
        await Assert.ThrowsAsync<ArgumentException>(() => PowerShellOperationsService.StartScheduledTaskAsync("ETP Nightly Backup"));
    }

    private void Write(string folder, string name, string overall, params string[] files)
    {
        var path = Path.Combine(root, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, role = name.Split('-')[0], businessDate = "2026-10-09", overall,
            files = files.Select(file => new { name = file, sha256 = new string('a', 64), verdict = overall, code = "", message = "", whatToDo = "" })
        }));
    }

    private sealed class FakeOffPc(string? oneDrive) : IOffPcCopyEnvironment
    {
        public string? OneDriveRoot() => oneDrive;
        public string KeyFilePath() => @"C:\ProgramData\EtpReporting\Keys\onedrive-backup.key";
        public bool OneDriveIsRunning() => true;
    }
}
