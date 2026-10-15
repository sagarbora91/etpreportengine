using System.Configuration;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Composition;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // P4-12c. The installer refuses to upgrade while ETP is running, because replacing a
    // locked executable used to abort setup halfway and roll back. It detects us by this
    // mutex, so the name must match AppMutex in installer/EtpReportingEngine.iss. Held for
    // the life of the process and never released early.
    internal const string RunningMutexName = @"Global\EtpReportingEngineRunning";
    private static System.Threading.Mutex? runningMutex;

    private static void PublishRunningMutex()
    {
        // A failure here must never stop the shop working: the worst case is that an
        // upgrade is attempted while ETP is open, which setup still detects by the locked
        // executable.
        try { runningMutex = new System.Threading.Mutex(false, RunningMutexName); }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Startup", "RUNNING_MUTEX_UNAVAILABLE"); }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 1.12 A3: --plan-database-changes is read-only and headless. It prints the pending
        // migrations and database objects as JSON and exits, before the running mutex, so setup
        // can ask while it upgrades.
        if (DatabaseChangePlanCommand.IsRequested(e.Args))
        {
            headless = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Shutdown(await DatabaseChangePlanCommand.RunAsync(e.Args, Console.Out, WriteHeadlessFailure));
            return;
        }
        PublishRunningMutex();
        PresentationCulture.Initialize();
        Themes.ThemeBrushes.ApplyContrast(Resources);
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DesktopDiagnostics.Record(args.ExceptionObject as Exception, "AppDomain", "APPDOMAIN_UNHANDLED", DesktopDiagnosticSeverity.Critical);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DesktopDiagnostics.Record(args.Exception, "TaskScheduler", "TASK_UNOBSERVED");
            args.SetObserved();
        };
        // Route first: a headless caller must receive an exit code and a message on
        // stderr, never a modal dialog it cannot dismiss. Composition validates the
        // supplied connection string and throws before any coordinator exists.
        headless = DesktopStartupCoordinator.Route(e.Args) != DesktopStartupMode.Interactive;
        DesktopCompositionRoot compositionRoot;
        try { compositionRoot = DesktopCompositionRoot.CreateForArguments(e.Args); }
        catch (Exception configuration)
        {
            DesktopDiagnostics.Record(configuration, "Startup", "STARTUP_CONFIGURATION_REJECTED", DesktopDiagnosticSeverity.Critical);
            // Interactive launches too. The rejection used to escape this async void method to
            // the dispatcher handler, which showed a generic "operation could not be completed"
            // dialog and left a process with no window running - still holding the mutex that
            // makes setup refuse to upgrade - until someone killed it. The reason is fixed
            // validation text, never the connection string itself.
            if (headless) WriteHeadlessFailure(StartupFailureText.HeadlessLine(StartupFailureText.ConfigurationModeToken, configuration));
            else MessageBox.Show("ETP Reporting Engine could not start with the connection it was given. " + configuration.Message,
                "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }
        // Interactive start only: the headless verbs and setup have their own refusal (--plan-database-changes).
        // Same path as a rejected configuration: recorded, one message, exit 2, no window and no mutex left behind.
        string? objectsRefusal = null;
        if (!headless) objectsRefusal = await CheckObjectsWithSplashAsync(compositionRoot);
        if (objectsRefusal is not null)
        {
            // Review m-5: its own event id, so the Error register tells it apart from a rejected connection string.
            DesktopDiagnostics.Record(new InvalidOperationException(objectsRefusal), "Startup", "STARTUP_OBJECTS_PENDING", DesktopDiagnosticSeverity.Critical);
            MessageBox.Show("ETP Reporting Engine cannot start yet. " + objectsRefusal,
                "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }
        var startup = new DesktopStartupCoordinator(
            compositionRoot.InitializeDatabaseAsync,
            compositionRoot.RunAutomationOnceAsync,
            () =>
            {
                var window = compositionRoot.CreateMainWindow();
                // The splash (if one was shown) was the first window; the real window must be the main one.
                MainWindow = window;
                window.Show();
                if (e.Args.Contains("--capture-review"))
                    _ = ImportReviewSession.RunAsync(window, e.Args);
            },
            compositionRoot.InitializeConfiguredDatabaseAsync,
            compositionRoot.WaitForAutomationSqlAsync);
        var mode = DesktopStartupCoordinator.Route(e.Args);
        if (mode != DesktopStartupMode.Interactive)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var outcome = await startup.RunAsync(e.Args);
        if (outcome.Failure is not null && outcome.DiagnosticSource is not null)
        {
            DesktopDiagnostics.Record(outcome.Failure, outcome.DiagnosticSource, "STARTUP_FAILED", DesktopDiagnosticSeverity.Critical);
            // IE-CODE-05. The installer and the scheduled task see only the exit code and stderr; the
            // diagnostics entry above sits in the running account's profile, where setup never looks.
            WriteHeadlessFailure(StartupFailureText.HeadlessLine(StartupFailureText.ModeToken(outcome.Mode), outcome.Failure));
        }
        if (outcome.ShouldShutdown)
        {
            Shutdown(outcome.ExitCode!.Value);
            return;
        }
    }

    /// <summary>How long the check may run before the "Checking the database..." window appears (a quick check shows nothing).</summary>
    internal static readonly TimeSpan SplashDelay = TimeSpan.FromMilliseconds(500);

    // Review m-5. The check can take up to 20 s on a slow PC; after SplashDelay a small window says what is happening.
    // The window is the app's first window, so the app must not shut down when it closes before the main window exists.
    private async Task<string?> CheckObjectsWithSplashAsync(DesktopCompositionRoot compositionRoot)
    {
        var check = compositionRoot.CheckObjectsUpToDateAsync();
        if (await Task.WhenAny(check, Task.Delay(SplashDelay)).ConfigureAwait(true) == check)
            return await check;
        StartupCheckWindow? splash = null;
        var previousMode = ShutdownMode;
        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { splash = new StartupCheckWindow(); splash.Show(); }
            catch (Exception exception) { DesktopDiagnostics.Record(exception, "Startup", "STARTUP_SPLASH_FAILED", DesktopDiagnosticSeverity.Warning); }
            return await check;
        }
        finally
        {
            try { splash?.Close(); } catch (InvalidOperationException) { }
            MainWindow = null;
            ShutdownMode = previousMode;
        }
    }

    // A closed or missing stderr must never turn a reported failure into a crash.
    private static void WriteHeadlessFailure(string line)
    {
        try { Console.Error.WriteLine(line); Console.Error.Flush(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    // Set before composition so the handler below never blocks an unattended caller.
    private static bool headless;

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DesktopDiagnostics.Record(e.Exception, "Dispatcher", "DISPATCHER_UNHANDLED", DesktopDiagnosticSeverity.Critical);
        if (headless)
        {
            // A modal dialog cannot be dismissed by an installer or a scheduled task.
            WriteHeadlessFailure(StartupFailureText.HeadlessLine(StartupFailureText.UnhandledModeToken, e.Exception));
            e.Handled = true;
            Current.Shutdown(2);
            return;
        }
        MessageBox.Show("The operation could not be completed. A diagnostic entry was recorded. No source rows were written to the log.", "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

}

