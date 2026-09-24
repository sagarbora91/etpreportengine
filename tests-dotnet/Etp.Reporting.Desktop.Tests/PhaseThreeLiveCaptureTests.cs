using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class PhaseThreeLiveCaptureTests
{
    [LiveCaptureFact]
    public void Merged_workspaces_render_against_the_disposable_fixture_database()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                System.Globalization.CultureInfo.CurrentCulture = PresentationCulture.Indian;
                const string connection = @"Server=.\SQLEXPRESS;Database=EtpPhase1Test_IntegrationUiReview;Integrated Security=True;Encrypt=Optional;Connect Timeout=5";
                var settings = Path.Combine(Path.GetTempPath(), "EtpLiveUiSettings", Guid.NewGuid().ToString("N"));
                window = new DesktopCompositionRoot(AppContext.BaseDirectory, connection, settings, temporaryConnection: true).CreateMainWindow();
                typeof(MainWindow).GetField("currentAccess", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window,
                    new AccessSession("synthetic", "Fixture owner", AccessRole.Owner, true));
                window.ApplyDensity(UiDensity.Touch, false);
                window.settingsWorkspace.UpdateAccess(new(true, true));
                window.WelcomeOverlay.Visibility = Visibility.Collapsed;
                window.ShellBusinessDateSelector.SelectedDate = new DateTime(2026, 8, 25);
                window.reportsWorkspaceView.ApplyScope(new DateTime(2026, 8, 1), new DateTime(2026, 8, 25), "Combined");
                window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.Resources = window.Resources;
                var output = Environment.GetEnvironmentVariable("ETP_PHASE3_LIVE_UI_EVIDENCE")!;
                Directory.CreateDirectory(output);
                foreach (var (name, task) in new[]
                {
                    ("Sales", "report-dsr"), ("Walk-ins", "walk-ins"), ("Import", "import-files"),
                    ("Reports", "reports-list"), ("Stock", "report-stock-closing"), ("Settings", "settings"),
                    ("Cash", "report-cash"), ("Customer-invoices", "report-invoice"), ("Staff-CRO", "report-staff"),
                    ("Brand-master", "masters")
                })
                {
                    if (name == "Cash") window.ShellStoreSelector.SelectedIndex = 2;
                    if (name is "Stock" or "Customer-invoices" or "Staff-CRO") window.ShellStoreSelector.SelectedIndex = 1;
                    Pump();
                    var destination = TaskNavigation.Find(task)!;
                    window.ApplyNavigationDecision(window.shell.Navigate(destination.Route, window.CurrentShellAccess));
                    if (destination.ReportCode is not null) Wait(window.reportsWorkspaceView.RunReportAsync(destination.ReportCode));
                    if (task == "masters")
                    {
                        var host = (UserControl)window.FocusedWorkspaceHost.Content;
                        var view = (EveningMastersView)((ScrollViewer)host.Content).Content;
                        Wait((Task)typeof(EveningMastersView).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null)!);
                    }
                    Pump();
                    foreach (var (width, height) in new[] { (1366, 728), (816, 440) })
                    {
                        window.WindowState = WindowState.Normal;
                        window.Width = width;
                        window.Height = height;
                        // The detached content is the native client area; captions/taskbar are excluded.
                        Grid.SetRow(window.SectionTabs, width < 1000 ? 1 : 0);
                        Grid.SetColumn(window.SectionTabs, width < 1000 ? 0 : 1);
                        Grid.SetColumnSpan(window.SectionTabs, width < 1000 ? 4 : 1);
                        for (var pass = 0; pass < 3; pass++)
                        {
                            root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Pump();
                        }
                        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                        image.Render(root);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var stream = File.Create(Path.Combine(output, $"{name}-{width}x{height}-wpf.png"));
                        encoder.Save(stream);
                        if (name == "Reports" && width == 1366)
                            foreach (var scroll in Visuals(window.FocusedWorkspaceHost).OfType<ScrollViewer>()) Assert.True(scroll.ScrollableHeight < 1);
                        if (name == "Cash" && width == 816)
                        {
                            var grid = Visuals(window.FocusedWorkspaceHost).OfType<DataGrid>().Single();
                            var row = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
                            var viewport = Visuals(grid).OfType<ScrollContentPresenter>().First();
                            var bounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
                            Assert.True(bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight,
                                $"Live Cash row ends at {bounds.Bottom} in a {viewport.ActualHeight} DIP viewport.");
                        }
                    }
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { if (window is not null) Wait(window.importWorkspaceView.DisposeAsync().AsTask()); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "Live capture timed out.");
        if (failure is not null) throw new InvalidOperationException("Live fixture capture failed.", failure);
    }

    private static void Wait(Task task)
    {
        while (!task.IsCompleted) Pump();
        task.GetAwaiter().GetResult();
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}

public sealed class LiveCaptureFactAttribute : FactAttribute
{
    public LiveCaptureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ETP_PHASE3_LIVE_UI_EVIDENCE")))
            Skip = "Opt-in capture against EtpPhase1Test_IntegrationUiReview: set ETP_PHASE3_LIVE_UI_EVIDENCE.";
    }
}
