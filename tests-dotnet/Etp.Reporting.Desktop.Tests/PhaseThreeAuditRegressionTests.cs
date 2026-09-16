using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class PhaseThreeAuditRegressionTests
{
    [Fact]
    public void Complete_report_catalogue_fits_touch_canvas_and_opens_each_report()
    {
        Sta(() =>
        {
            var opened = new List<string>();
            var view = new ReportListView(new(true, true, true, true), task => opened.Add(task.ReportCode!));
            Theme(view);
            Layout(view, 1254, 610);
            var buttons = Visuals(view).OfType<Button>().ToArray();
            foreach (var button in buttons)
            {
                Assert.True(button.ActualHeight >= 44, $"{button.Content}: {button.ActualHeight} DIP");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            Assert.Equal(ProductReportCatalogue.All.Select(report => report.Code).Order(), opened.Order());
            foreach (var scroll in Visuals(view).OfType<ScrollViewer>())
                Assert.True(scroll.ScrollableHeight < 1, $"Full catalogue scrolls by {scroll.ScrollableHeight} DIP.");
        });
    }

    [Fact]
    public void Shell_leaves_at_least_610_dip_for_reports_at_full_screen()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                window.WindowState = WindowState.Normal;
                window.Width = 1366;
                window.Height = 728;
                window.OpenSection("Today", "Walk-ins");
                window.OpenSection("Reports");
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.Resources = window.Resources;
                Layout(root, 1366, 728);
                Assert.True(window.FocusedWorkspaceHost.ActualHeight >= 610,
                    $"Content height is {window.FocusedWorkspaceHost.ActualHeight} DIP.");
                Assert.Equal(TaskNavigation.Sections, window.RailPanel.Children.OfType<Button>()
                    .Where(button => button.Tag is string).Select(button => (string)button.Tag));
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void Long_status_discloses_hidden_lines_and_keeps_full_message_available()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.Resources = window.Resources;
                window.ApplicationStatus.Text = "First line\nSecond line\nThird line\nFourth line";
                Layout(root, 1366, 728);
                Assert.Equal("3 more lines · More details", window.StatusOverflowSummary.Text);
                Assert.Equal(Visibility.Visible, window.StatusOverflowSummary.Visibility);
                Assert.Contains("Fourth line", window.ApplicationStatus.Text);
                Assert.Equal(TextWrapping.NoWrap, window.ApplicationStatus.TextWrapping);
                Assert.True(window.ApplicationStatus.ActualHeight <= window.StatusFooter.ActualHeight);
                window.ApplicationStatus.Text = "Saved.";
                Layout(root, 1366, 728);
                Assert.Equal(Visibility.Collapsed, window.StatusOverflowSummary.Visibility);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void Profile_opens_in_the_content_area_and_sections_remain_usable()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                window.OpenProfile_Click(window, new RoutedEventArgs());
                var root = (FrameworkElement)window.Content;
                window.Content = null;
                root.Resources = window.Resources;
                Layout(root, 1366, 728);
                Assert.Contains(Visuals(window.FocusedWorkspaceHost).OfType<TextBlock>(), text => text.Text == "Current profile");
                Assert.Contains(Visuals(window.FocusedWorkspaceHost).OfType<TextBlock>(), text => text.Text.Contains("Synthetic owner"));
                window.OpenSection("Reports");
                Assert.IsType<ReportListView>(window.FocusedWorkspaceHost.Content);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

    [Fact]
    public void Store_heading_in_the_dsr_matrix_is_exposed_to_screen_readers()
    {
        Sta(() =>
        {
            var document = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>())
                with { EveningSheets = [new("HEMW", "Helios", 0, 0, 0, 0, [])] };
            var view = new EveningDsrView(document);
            Theme(view);
            Layout(view, 1000, 600);
            var heading = Visuals(view).OfType<TextBlock>().Single(text => text.Text == "Helios · 25 Aug 2026");
            Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(heading));
        });
    }

    private static void Theme(FrameworkElement element) => element.Resources.MergedDictionaries.Add(
        new ResourceDictionary { Source = new Uri("/Etp.Reporting.Desktop;component/Themes/Theme.xaml", UriKind.Relative) });

    private static void Layout(FrameworkElement root, double width, double height)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Pump();
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }

    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = PresentationCulture.Indian;
                action();
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "UI regression test timed out.");
        if (error is not null) throw new InvalidOperationException("Phase 3 audit regression failed.", error);
    }
}
