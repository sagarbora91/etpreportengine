using System.Diagnostics;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// P4-8 regression. A headless caller — the installer or a scheduled task — must receive
/// an exit code and a message, never a modal dialog it cannot dismiss. Before the fix the
/// process logged DISPATCHER_UNHANDLED and then waited forever on MessageBox.Show.
/// </summary>
public sealed class HeadlessStartupFailureTests
{
    // "Integrated Security=True" contains spaces; a caller that loses the quoting supplies
    // a truncated, invalid string. Rejecting it is correct; hanging on it is not.
    [Theory]
    [InlineData("--initialize-database")]
    [InlineData("--initialize-configured-database")]
    [InlineData("--automation-once")]
    public async Task Headless_startup_exits_with_a_message_when_the_connection_string_is_rejected(string mode)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var application = Path.Combine(root!.FullName, "src", "Etp.Reporting.Desktop", "bin", configuration,
            "net10.0-windows", "Etp.Reporting.Desktop.exe");
        Assert.True(File.Exists(application), $"Build the desktop application before running this test: {application}");

        var info = new ProcessStartInfo(application)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add(mode);
        info.ArgumentList.Add("--connection-string");
        info.ArgumentList.Add(@"Server=.\SQLEXPRESS;Database=EtpPhase0Test_HeadlessReject;Integrated");

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var exited = false;
        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token);
            exited = true;
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); }

        Assert.True(exited, "Headless startup did not exit; a rejected configuration must not block an unattended caller.");
        Assert.NotEqual(0, process.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(await stderr), "The rejection reason must reach the caller on stderr.");
    }
}
