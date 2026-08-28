namespace Etp.Reporting.Desktop.Composition;

public enum DesktopStartupMode
{
    Interactive,
    InitializeDatabase,
    AutomationOnce
}

public sealed record DesktopStartupOutcome(
    DesktopStartupMode Mode,
    int? ExitCode,
    bool WindowRequested,
    Exception? Failure)
{
    public bool ShouldShutdown => ExitCode.HasValue;

    public string? DiagnosticSource => Mode switch
    {
        DesktopStartupMode.InitializeDatabase => "DatabaseInitialization",
        DesktopStartupMode.AutomationOnce => "UnattendedAutomation",
        _ => null
    };
}

/// <summary>
/// Routes process startup without depending on WPF. The composition root supplies
/// the concrete work while this coordinator guarantees mutually exclusive startup paths.
/// </summary>
public sealed class DesktopStartupCoordinator
{
    private readonly Func<CancellationToken, Task> initializeDatabase;
    private readonly Func<CancellationToken, Task<int>> runAutomationOnce;
    private readonly Action showMainWindow;

    public DesktopStartupCoordinator(
        Func<CancellationToken, Task> initializeDatabase,
        Func<CancellationToken, Task<int>> runAutomationOnce,
        Action showMainWindow)
    {
        this.initializeDatabase = initializeDatabase ?? throw new ArgumentNullException(nameof(initializeDatabase));
        this.runAutomationOnce = runAutomationOnce ?? throw new ArgumentNullException(nameof(runAutomationOnce));
        this.showMainWindow = showMainWindow ?? throw new ArgumentNullException(nameof(showMainWindow));
    }

    public static DesktopStartupMode Route(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 1) return DesktopStartupMode.Interactive;
        if (string.Equals(arguments[0], "--initialize-database", StringComparison.Ordinal))
            return DesktopStartupMode.InitializeDatabase;
        if (string.Equals(arguments[0], "--automation-once", StringComparison.Ordinal))
            return DesktopStartupMode.AutomationOnce;
        return DesktopStartupMode.Interactive;
    }

    public async Task<DesktopStartupOutcome> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var mode = Route(arguments);
        if (mode == DesktopStartupMode.Interactive)
        {
            showMainWindow();
            return new(mode, null, true, null);
        }

        try
        {
            if (mode == DesktopStartupMode.InitializeDatabase)
            {
                await initializeDatabase(cancellationToken).ConfigureAwait(false);
                return new(mode, 0, false, null);
            }

            var exitCode = await runAutomationOnce(cancellationToken).ConfigureAwait(false);
            return new(mode, exitCode, false, null);
        }
        catch (Exception exception)
        {
            return new(mode, 1, false, exception);
        }
    }
}
