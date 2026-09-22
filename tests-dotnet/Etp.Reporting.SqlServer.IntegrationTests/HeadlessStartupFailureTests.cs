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

    [Fact]
    public async Task Interactive_startup_says_why_and_exits_when_the_connection_string_is_rejected()
    {
        // The same rejection in an ordinary, windowed launch used to escape OnStartup to the
        // dispatcher handler: a generic "operation could not be completed" dialog, and after
        // OK a process with no window that kept running - holding the mutex that makes setup
        // refuse to upgrade - until it was killed. It must explain itself and exit.
        // This test shows a message box for a moment and dismisses it itself.
        using var process = Process.Start(StartInfo(null))!;
        try
        {
            var dialog = IntPtr.Zero;
            for (var attempt = 0; attempt < 120 && dialog == IntPtr.Zero && !process.HasExited; attempt++)
            {
                await Task.Delay(250);
                process.Refresh();
                dialog = process.MainWindowHandle;
            }
            Assert.False(process.HasExited, "The application exited without telling the user why.");
            Assert.NotEqual(IntPtr.Zero, dialog);

            // A message box's labels: the icon (no text) and the message.
            var shown = new System.Text.StringBuilder();
            for (var label = FindWindowEx(dialog, IntPtr.Zero, "Static", null); label != IntPtr.Zero; label = FindWindowEx(dialog, label, "Static", null))
            {
                var text = new System.Text.StringBuilder(1024);
                GetWindowText(label, text, text.Capacity);
                shown.Append(text).Append(' ');
            }
            Assert.Contains("could not start with the connection it was given", shown.ToString(), StringComparison.Ordinal);

            var ok = FindWindowEx(dialog, IntPtr.Zero, "Button", "OK");
            Assert.NotEqual(IntPtr.Zero, ok);
            SendMessage(ok, BmClick, IntPtr.Zero, IntPtr.Zero);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { }
            Assert.True(process.HasExited, "After OK the application kept running with no window.");
            Assert.Equal(2, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static ProcessStartInfo StartInfo(string? mode)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var application = Path.Combine(root!.FullName, "src", "Etp.Reporting.Desktop", "bin", configuration,
            "net10.0-windows", "Etp.Reporting.Desktop.exe");
        Assert.True(File.Exists(application), $"Build the desktop application before running this test: {application}");
        var info = new ProcessStartInfo(application) { UseShellExecute = false };
        if (mode is not null) info.ArgumentList.Add(mode);
        info.ArgumentList.Add("--connection-string");
        info.ArgumentList.Add(@"Server=.\SQLEXPRESS;Database=EtpPhase0Test_HeadlessReject;Integrated");
        return info;
    }

    private const int BmClick = 0x00F5;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int capacity);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
