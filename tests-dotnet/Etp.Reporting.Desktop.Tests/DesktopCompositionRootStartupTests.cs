using Etp.Reporting.Desktop.Composition;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopCompositionRootStartupTests
{
    public static TheoryData<string[], DesktopStartupMode> Routes => new()
    {
        { ["--initialize-database"], DesktopStartupMode.InitializeDatabase },
        { ["--automation-once"], DesktopStartupMode.AutomationOnce },
        { [], DesktopStartupMode.Interactive },
        { ["--INITIALIZE-DATABASE"], DesktopStartupMode.Interactive },
        { ["--AUTOMATION-ONCE"], DesktopStartupMode.Interactive },
        { ["--initialize-database", "extra"], DesktopStartupMode.Interactive },
        { ["--automation-once", "extra"], DesktopStartupMode.Interactive },
        { ["--unknown"], DesktopStartupMode.Interactive }
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public void Routing_requires_one_exact_case_sensitive_argument(
        string[] arguments,
        DesktopStartupMode expected)
    {
        Assert.Equal(expected, DesktopStartupCoordinator.Route(arguments));
    }

    [Fact]
    public async Task Database_initialization_succeeds_without_requesting_a_window()
    {
        var calls = new Calls();
        var outcome = await Coordinator(calls).RunAsync(["--initialize-database"]);

        Assert.Equal(DesktopStartupMode.InitializeDatabase, outcome.Mode);
        Assert.Equal(0, outcome.ExitCode);
        Assert.False(outcome.WindowRequested);
        Assert.Null(outcome.Failure);
        Assert.Equal("DatabaseInitialization", outcome.DiagnosticSource);
        Assert.Equal(1, calls.Initialize);
        Assert.Equal(0, calls.Automation);
        Assert.Equal(0, calls.Window);
    }

    [Fact]
    public async Task Automation_preserves_the_service_exit_code_without_requesting_a_window()
    {
        var calls = new Calls { AutomationExitCode = 1 };
        var outcome = await Coordinator(calls).RunAsync(["--automation-once"]);

        Assert.Equal(DesktopStartupMode.AutomationOnce, outcome.Mode);
        Assert.Equal(1, outcome.ExitCode);
        Assert.False(outcome.WindowRequested);
        Assert.Null(outcome.Failure);
        Assert.Equal("UnattendedAutomation", outcome.DiagnosticSource);
        Assert.Equal(0, calls.Initialize);
        Assert.Equal(1, calls.Automation);
        Assert.Equal(0, calls.Window);
    }

    [Theory]
    [InlineData("--initialize-database", "DatabaseInitialization")]
    [InlineData("--automation-once", "UnattendedAutomation")]
    public async Task Headless_failure_maps_to_exit_one_and_the_existing_diagnostic_source(
        string argument,
        string expectedDiagnosticSource)
    {
        var failure = new InvalidOperationException("failure");
        var calls = new Calls { Failure = failure };
        var outcome = await Coordinator(calls).RunAsync([argument]);

        Assert.Equal(1, outcome.ExitCode);
        Assert.False(outcome.WindowRequested);
        Assert.Same(failure, outcome.Failure);
        Assert.Equal(expectedDiagnosticSource, outcome.DiagnosticSource);
        Assert.Equal(0, calls.Window);
    }

    [Fact]
    public async Task Interactive_startup_requests_exactly_one_window_and_no_headless_work()
    {
        var calls = new Calls();
        var outcome = await Coordinator(calls).RunAsync([]);

        Assert.Equal(DesktopStartupMode.Interactive, outcome.Mode);
        Assert.Null(outcome.ExitCode);
        Assert.True(outcome.WindowRequested);
        Assert.Null(outcome.Failure);
        Assert.Equal(0, calls.Initialize);
        Assert.Equal(0, calls.Automation);
        Assert.Equal(1, calls.Window);
    }

    private static DesktopStartupCoordinator Coordinator(Calls calls) => new(
        _ => calls.InitializeAsync(),
        _ => calls.AutomateAsync(),
        calls.ShowWindow);

    private sealed class Calls
    {
        public int Initialize { get; private set; }
        public int Automation { get; private set; }
        public int Window { get; private set; }
        public int AutomationExitCode { get; init; }
        public Exception? Failure { get; init; }

        public Task InitializeAsync()
        {
            Initialize++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task<int> AutomateAsync()
        {
            Automation++;
            return Failure is null ? Task.FromResult(AutomationExitCode) : Task.FromException<int>(Failure);
        }

        public void ShowWindow() => Window++;
    }
}
