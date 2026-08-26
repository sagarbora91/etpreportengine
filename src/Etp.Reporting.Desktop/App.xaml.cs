using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Etp.Reporting.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteDiagnostic(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) => { WriteDiagnostic(args.Exception, "UnobservedTask"); args.SetObserved(); };
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

