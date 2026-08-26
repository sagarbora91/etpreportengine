using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Etp.Reporting.Infrastructure.SqlServer;

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
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteDiagnostic(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) => { WriteDiagnostic(args.Exception, "UnobservedTask"); args.SetObserved(); };

        if (e.Args.Length == 1 && string.Equals(e.Args[0], "--initialize-database", StringComparison.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var migrations = Path.Combine(AppContext.BaseDirectory, "database", "migrations");
                var connection = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
                await new SqlServerDatabaseBootstrapper(connection, new DirectoryMigrationSource(migrations)).BootstrapAsync();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                WriteDiagnostic(ex, "DatabaseInitialization");
                Shutdown(1);
            }
            return;
        }

        if (e.Args.Length == 1 && string.Equals(e.Args[0], "--automation-once", StringComparison.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var connection = @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";
                var result = await new AutomatedOperationsService(connection).RunOnceAsync();
                Shutdown(result.SourcesFailed == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                WriteDiagnostic(ex, "UnattendedAutomation");
                Shutdown(1);
            }
            return;
        }

        new MainWindow().Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteDiagnostic(e.Exception, "Dispatcher");
        MessageBox.Show("The operation could not be completed. A diagnostic entry was recorded. No source rows were written to the log.", "ETP Reporting Engine", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void WriteDiagnostic(Exception? exception, string source)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "Logs");
            Directory.CreateDirectory(directory);
            var entry = $"{DateTimeOffset.UtcNow:O}\t{source}\t{exception?.GetType().FullName ?? "Unknown"}\tHResult={exception?.HResult ?? 0}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(directory, $"diagnostics-{DateTime.UtcNow:yyyyMM}.log"), entry);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

