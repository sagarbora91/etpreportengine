using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Etp.Reporting.Desktop.Modules.Imports;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed partial class PhaseFiveFullWindowCaptureTests
{
    private const string RoleWalkChildVariable = "ETP_PHASE5_ROLE_WALK_CHILD";

    private async Task RunIsolatedRoleWalkAsync()
    {
        // WPF Application shutdown is process-wide and cannot be reset. Run the
        // same ordinary Fact in a fresh test host, preserving its full assertions,
        // fixture cleanup, Application.Shutdown and joined dispatcher lifetime.
        var results = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpRoleWalk_" + Guid.NewGuid().ToString("N"))).FullName;
        var testName = typeof(PhaseFiveFullWindowCaptureTests).FullName + "." + nameof(Every_role_reachable_destination_loads_with_disposable_data_and_optional_captures);
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Environment.CurrentDirectory
            };
            foreach (var argument in new[]
            {
                "vstest", typeof(PhaseFiveFullWindowCaptureTests).Assembly.Location,
                "/TestCaseFilter:FullyQualifiedName=" + testName,
                "/logger:console;verbosity=detailed", "/logger:trx;LogFileName=role-walk.trx", "/ResultsDirectory:" + results
            }) start.ArgumentList.Add(argument);
            start.Environment[RoleWalkChildVariable] = "1";
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated role-walk test host.");
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var drainCancellation = new CancellationTokenSource();
            var drain = Task.WhenAll(CaptureOutputAsync(process.StandardOutput, stdout, drainCancellation.Token),
                CaptureOutputAsync(process.StandardError, stderr, drainCancellation.Token));
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await process.WaitForExitAsync(closeTimeout.Token);
                    throw new TimeoutException("The isolated role walk did not exit within ten minutes; its process tree was stopped.");
                }
            }
            finally
            {
                try { await drain.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException)
                {
                    drainCancellation.Cancel();
                    throw new TimeoutException("The isolated role-walk output pipes did not close within fifteen seconds; captured output follows.");
                }
                finally
                {
                    lock (stdout) output.WriteLine(stdout.ToString());
                    lock (stderr) output.WriteLine(stderr.ToString());
                }
            }
            Assert.True(process.ExitCode == 0, $"The isolated role walk failed with exit code {process.ExitCode}; see its complete output above.");
            var receipt = Path.Combine(results, "role-walk.trx");
            Assert.True(File.Exists(receipt), "The isolated role walk produced no test-completion receipt.");
            var document = XDocument.Load(receipt);
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
            var method = Assert.Single(document.Descendants(ns + "TestMethod"));
            Assert.Equal(typeof(PhaseFiveFullWindowCaptureTests).FullName, (string?)method.Attribute("className"));
            Assert.Equal(nameof(Every_role_reachable_destination_loads_with_disposable_data_and_optional_captures), (string?)method.Attribute("name"));
            var definition = Assert.Single(document.Descendants(ns + "UnitTest"));
            var completed = Assert.Single(document.Descendants(ns + "UnitTestResult"));
            Assert.Equal((string?)definition.Attribute("id"), (string?)completed.Attribute("testId"));
            Assert.Equal(testName, (string?)completed.Attribute("testName"));
            Assert.Equal("Passed", (string?)completed.Attribute("outcome"));
            var counts = Assert.Single(document.Descendants(ns + "Counters"));
            foreach (var count in new[] { "total", "executed", "passed" }) Assert.Equal("1", (string?)counts.Attribute(count));
            foreach (var count in new[] { "failed", "error", "timeout", "aborted", "notExecuted" }) Assert.Equal("0", (string?)counts.Attribute(count));
        }
        finally { Directory.Delete(results, recursive: true); }
    }

    private static async Task CaptureOutputAsync(StreamReader reader, StringBuilder captured, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken) is var count && count > 0)
            lock (captured) captured.Append(buffer, 0, count);
    }

    private static void AssertSubsequentWpfViewLoads()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var coordinator = new DesktopImportCoordinator(
                _ => throw new InvalidOperationException("The WPF lifecycle probe must not import data."),
                (_, _, _, _, _, _, _) => Task.CompletedTask);
            ImportWorkspaceView? view = null;
            try
            {
                view = new ImportWorkspaceView(coordinator, () => throw new InvalidOperationException("The WPF lifecycle probe must not connect to SQL."));
                Assert.IsType<TextBox>(view.FindName("WorkbookPathInput"));
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (view is not null) view.DisposeAsync().AsTask().GetAwaiter().GetResult();
                else coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The post-role-walk XAML dispatcher did not close.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
