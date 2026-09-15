using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;
using Xunit.Abstractions;

namespace Etp.Reporting.SqlServer.IntegrationTests;

[Collection("Full MainWindow smoke")]
public sealed class PhaseFourFullWindowSmokeTests(ITestOutputHelper output)
{
    [FullWindowSmokeFact]
    public async Task Full_MainWindow_starts_displays_dashboard_renders_and_closes_with_disposable_database()
    {
        var database = new SqlDatabaseFixture();
        var settings = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpFullWindowSmoke", Guid.NewGuid().ToString("N"))).FullName;
        var realSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
        var settingsBefore = HashIfPresent(realSettings);
        var preferencesBefore = HashIfPresent(UiPreferenceStore.FilePath);
        try
        {
            await database.InitializeAsync();
            new DesktopSettingsStore(settings).Save(database.ConnectionString);
            await RunWindowOnStaAsync(database.ConnectionString, settings);
            Assert.Equal(2, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type IN ('ApplicationStart','SessionStart') AND outcome='Succeeded';")));
            Assert.Equal(settingsBefore, HashIfPresent(realSettings));
            Assert.Equal(preferencesBefore, HashIfPresent(UiPreferenceStore.FilePath));
            output.WriteLine("Full MainWindow test host: real window shown, startup and SQL session succeeded, dashboard loaded, WPF rendering passed at 1366x768 and 816x480, and window closed. Only a generated test database and temporary connection settings were used; existing user settings hashes are unchanged. This is not production App.OnStartup or native screenshot capture.");
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(settings, recursive: true);
        }
    }

    private static async Task RunWindowOnStaAsync(string connectionString, string settings)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            System.Windows.Application? application = null;
            MainWindow? window = null;
            Exception? dispatcherFailure = null;
            try
            {
                // Base WPF Application provides the dispatcher/theme context only.
                // Desktop.App.OnStartup would select a production target and must never run here.
                application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var name in new[] { "Colors", "Spacing", "Typography", "Icons", "Controls" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Etp.Reporting.Desktop;component/Themes/{name}.xaml", UriKind.Absolute) });
                application.DispatcherUnhandledException += (_, args) => { dispatcherFailure = args.Exception; args.Handled = true; };
                application.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        var composition = new DesktopCompositionRoot(AppContext.BaseDirectory, connectionString, settings);
                        window = composition.CreateMainWindow();
                        window.Title = "ETP full MainWindow test host - disposable data";
                        window.WindowState = WindowState.Normal;
                        window.Width = 1366;
                        window.Height = 768;
                        window.ShowActivated = false;
                        var closed = false;
                        window.Closed += (_, _) => closed = true;
                        window.Show();
                        Assert.True(window.IsVisible);
                        Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(window).Handle);
                        Assert.NotNull(PresentationSource.FromVisual(window));
                        var continueButton = (Button)window.FindName("ContinueButton");
                        await WaitUntilAsync(() => continueButton.IsEnabled, () => dispatcherFailure);
                        Assert.Equal("Continue", continueButton.Content);
                        Assert.StartsWith("Connected", ((TextBlock)window.FindName("ConnectionStatus")).Text);
                        var dashboard = (DashboardView)((ContentControl)window.FindName("DashboardHost")).Content;
                        continueButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await WaitUntilAsync(() => dashboard.CurrentState is not null, () => dispatcherFailure);
                        Assert.Null(dashboard.CurrentState!.ErrorMessage);
                        Assert.Equal(Visibility.Collapsed, ((Grid)window.FindName("WelcomeOverlay")).Visibility);
                        foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 816, Height: 480) })
                        {
                            window.Width = size.Width;
                            window.Height = size.Height;
                            await application.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                            var content = (FrameworkElement)window.Content;
                            Assert.True(content.ActualWidth > 0 && content.ActualHeight > 0);
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(content);
                            Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
                        }
                        Assert.Null(dispatcherFailure);
                        window.Close();
                        await WaitUntilAsync(() => closed, () => dispatcherFailure);
                        Assert.False(window.IsVisible);
                        completion.TrySetResult();
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                    finally { window?.Close(); application.Shutdown(); }
                });
                application.Run();
            }
            catch (Exception exception) { completion.TrySetException(exception); application?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(90));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "The full-window test dispatcher did not close.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Func<Exception?> failure)
    {
        var stopAt = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (failure() is { } exception) throw new InvalidOperationException("Full-window dispatcher failed.", exception);
            if (DateTime.UtcNow >= stopAt) throw new TimeoutException("Full-window startup or close did not finish.");
            await Task.Delay(50);
        }
    }

    private static string? HashIfPresent(string path)
    {
        if (!File.Exists(path)) return null;
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}

[CollectionDefinition("Full MainWindow smoke", DisableParallelization = true)]
public sealed class FullWindowSmokeCollection { }

public sealed class FullWindowSmokeFactAttribute : FactAttribute
{
    public FullWindowSmokeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ETP_FULL_WINDOW_SMOKE") != "1")
            Skip = "Opt-in full-window test host: set ETP_FULL_WINDOW_SMOKE=1 and run this test alone.";
    }
}
