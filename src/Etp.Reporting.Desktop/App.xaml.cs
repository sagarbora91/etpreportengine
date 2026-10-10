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
        var startup = new DesktopStartupCoordinator(
            compositionRoot.InitializeDatabaseAsync,
            compositionRoot.RunAutomationOnceAsync,
            () =>
            {
                var window = compositionRoot.CreateMainWindow();
                window.Show();
                if (e.Args.Contains("--capture-review"))
                    _ = ImportReviewSession.RunAsync(window, compositionRoot.LoadConnectionString(), e.Args);
            },
            compositionRoot.InitializeConfiguredDatabaseAsync);
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

