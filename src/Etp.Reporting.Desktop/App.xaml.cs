using System.Configuration;
using System.Windows;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Composition;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
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
        catch (Exception configuration) when (headless)
        {
            DesktopDiagnostics.Record(configuration, "Startup", "STARTUP_CONFIGURATION_REJECTED", DesktopDiagnosticSeverity.Critical);
            Console.Error.WriteLine(configuration.Message);
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
            DesktopDiagnostics.Record(outcome.Failure, outcome.DiagnosticSource, "STARTUP_FAILED", DesktopDiagnosticSeverity.Critical);
        if (outcome.ShouldShutdown)
        {
            Shutdown(outcome.ExitCode!.Value);
            return;
        }
    }

    // Set before composition so the handler below never blocks an unattended caller.
    private static bool headless;

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DesktopDiagnostics.Record(e.Exception, "Dispatcher", "DISPATCHER_UNHANDLED", DesktopDiagnosticSeverity.Critical);
        if (headless)
        {
            // A modal dialog cannot be dismissed by an installer or a scheduled task.
            Console.Error.WriteLine(e.Exception.Message);
            e.Handled = true;
            Current.Shutdown(2);
            return;
        }
        MessageBox.Show("The operation could not be completed. A diagnostic entry was recorded. No source rows were written to the log.", "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

}

