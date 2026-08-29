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
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DesktopDiagnostics.Record(args.ExceptionObject as Exception, "AppDomain", "APPDOMAIN_UNHANDLED", DesktopDiagnosticSeverity.Critical);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DesktopDiagnostics.Record(args.Exception, "TaskScheduler", "TASK_UNOBSERVED");
            args.SetObserved();
        };
        var compositionRoot = DesktopCompositionRoot.CreateDefault();
        var startup = new DesktopStartupCoordinator(
            compositionRoot.InitializeDatabaseAsync,
            compositionRoot.RunAutomationOnceAsync,
            () => compositionRoot.CreateMainWindow().Show());
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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DesktopDiagnostics.Record(e.Exception, "Dispatcher", "DISPATCHER_UNHANDLED", DesktopDiagnosticSeverity.Critical);
        MessageBox.Show("The operation could not be completed. A diagnostic entry was recorded. No source rows were written to the log.", "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

}

