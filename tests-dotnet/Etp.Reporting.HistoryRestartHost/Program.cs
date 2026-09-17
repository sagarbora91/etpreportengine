using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Test-only host: refuses all non-generated databases, including the real shop DB.
        var target = new SqlConnectionStringBuilder(args[0]);
        if (!target.InitialCatalog.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) return 2;
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var name in new[] { "Colors", "Spacing", "Typography", "Icons", "Controls" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Etp.Reporting.Desktop;component/Themes/{name}.xaml", UriKind.Absolute) });
        Exception? failure = null;
        application.DispatcherUnhandledException += (_, e) => { failure = e.Exception; e.Handled = true; application.Shutdown(1); };
        application.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                var composition = new DesktopCompositionRoot(AppContext.BaseDirectory, args[0], args[1], temporaryConnection: true);
                var window = composition.CreateMainWindow();
                window.Title = "Disposable import history restart test";
                window.WindowState = WindowState.Normal; window.Width = 1366; window.Height = 768;
                window.ShowActivated = false; window.Show();
                await Until(() => ((Button)window.FindName("ContinueButton")).IsEnabled);
                if (args[3] != "read")
                {
                    await using var coordinator = new DesktopImportCoordinator(value => new SqlServerImportPersistenceUseCase(value),
                        (_, _, _, _, _, _, _) => Task.CompletedTask);
                    var result = await coordinator.ImportFolderAsync(args[3], args[0], new("History restart fixture"));
                    if (result.Files.Any(file => file.Failed)) throw new InvalidOperationException("Fixture import failed: " + string.Join(";", result.Files.Select(f => f.Message)));
                }
                ((DatePicker)window.FindName("ShellBusinessDateSelector")).SelectedDate = new DateTime(2026, 8, 25);
                var rail = (Panel)window.FindName("RailPanel");
                rail.Children.OfType<Button>().Single(button => button.Tag?.ToString() == "Import").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var tabs = (Panel)window.FindName("SectionTabs");
                var historyTab = tabs.Children.OfType<Button>().FirstOrDefault(button => button.Tag?.ToString() == "History");
                if (historyTab is not null) historyTab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else tabs.Children.OfType<ComboBox>().Single(combo => combo.Items.Cast<object>().Any(item => item is string text && text == "History")).SelectedItem = "History";
                var host = (ContentControl)window.FindName("FocusedWorkspaceHost");
                await Until(() => host.Content is ImportHistoryView { IsLoading: false });
                var history = (ImportHistoryView)host.Content;
                if (history.StatusText.Contains("could not")) throw new InvalidOperationException(history.StatusText);
                File.WriteAllText(args[2], JsonSerializer.Serialize(new { history.Entries, history.StatusText, history.DetailText }));
                window.Close();
                application.Shutdown();
            }
            catch (Exception exception) { failure = exception; application.Shutdown(1); }
        });
        var exitCode = application.Run();
        if (failure is not null) Console.Error.WriteLine(failure);
        return exitCode;
    }

    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!ready())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("History test window did not become ready.");
            await Task.Delay(25);
        }
    }
}
