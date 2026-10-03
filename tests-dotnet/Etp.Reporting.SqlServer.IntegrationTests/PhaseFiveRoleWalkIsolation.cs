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
    private const string RoleWalkDatabaseVariable = "ETP_PHASE5_ROLE_WALK_DATABASE";

    private async Task RunIsolatedRoleWalkAsync()
    {
        // WPF Application shutdown is process-wide and cannot be reset. Run the
        // same ordinary Fact in a fresh test host, preserving its full assertions,
        // fixture cleanup, Application.Shutdown and joined dispatcher lifetime.
        var results = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpRoleWalk_" + Guid.NewGuid().ToString("N"))).FullName;
        var testName = typeof(PhaseFiveFullWindowCaptureTests).FullName + "." + nameof(Every_role_reachable_destination_loads_with_disposable_data_and_optional_captures);
        // Named here, not in the child: a killed child cannot drop its own database.
        var databaseName = SqlDatabaseFixture.NewName();
        Process? process = null;
        await TestCleanup.RunAsync(async () =>
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
            start.Environment[RoleWalkDatabaseVariable] = databaseName;
            process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated role-walk test host.");
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var drainCancellation = new CancellationTokenSource();
            var drain = Task.WhenAll(CaptureOutputAsync(process.StandardOutput, stdout, drainCancellation.Token),
                CaptureOutputAsync(process.StandardError, stderr, drainCancellation.Token));
            // Each outcome is recorded first and reported afterwards, in order of
            // importance, so a later problem (pipes that stay open) cannot replace an
            // earlier one (a child that had to be killed).
            var timedOut = false;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { timedOut = true; }
            }
            if (timedOut) StopProcessTree(process);
            var drained = true;
            try { await drain.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { drained = false; drainCancellation.Cancel(); }
            lock (stdout) output.WriteLine(stdout.ToString());
            lock (stderr) output.WriteLine(stderr.ToString());
            if (timedOut) throw new TimeoutException("The isolated role walk did not exit within ten minutes; its process tree was stopped. Its complete output is above.");
            if (!drained) throw new TimeoutException("The isolated role-walk output pipes did not close within fifteen seconds; the output captured so far is above.");
            Assert.True(process.ExitCode == 0, $"The isolated role walk failed with exit code {process.ExitCode}; see its complete output above.");
            AssertSinglePassingReceipt(Path.Combine(results, "role-walk.trx"), testName);
        },
        TestCleanup.Step("stop the role-walk process", () =>
        {
            if (process is null) return;
            try { if (!process.HasExited) StopProcessTree(process); }
            finally { process.Dispose(); }
        }),
        // Normally the child has already dropped it and this does nothing. After a kill,
        // or a child that died before its own cleanup, this is the only cleanup there is.
        TestCleanup.StepAsync("drop the role walk's fixture database", () => SqlDatabaseFixture.DropIfPresentAsync(databaseName)),
        TestCleanup.Step("delete the role-walk results folder", () => Directory.Delete(results, recursive: true)));
    }

    private static void StopProcessTree(Process process)
    {
        process.Kill(entireProcessTree: true);
        if (!process.WaitForExit(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("The isolated role-walk process tree did not stop within fifteen seconds of being killed.");
    }

    private static void AssertSinglePassingReceipt(string receipt, string testName)
    {
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
                _ => throw new InvalidOperationException("The WPF lifecycle probe must not import data."));
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
