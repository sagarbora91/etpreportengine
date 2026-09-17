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
    public void Compact_cash_keeps_a_complete_detail_row_visible_with_dates_filters_and_exports_available()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var workspace = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("cash"));
                workspace.ConfigureTaskScope("Titan World");
                workspace.DateFromPicker.SelectedDate = new DateTime(2026, 8, 1);
                workspace.DateToPicker.SelectedDate = new DateTime(2026, 8, 25);
                var row = new { Date = new DateOnly(2026, 8, 25), Store = "Titan World", Particular = "Expenses", Amount = 118m,
                    CreditParticular = "Opening balance", CreditAmount = 100m, Notes = "Synthetic opening balance and expense detail." };
                var model = new VisualReportModel(new("cash", "Cash Book", new(2026,8,1), new(2026,8,25), "synthetic", DateTimeOffset.UtcNow), [], [], new([], []), [], []);
                workspace.SetPreview(ReportVisualPresenter.BuildFocusedPreview(model, new[] { row }),
                    "25 days. Opening carries forward from the previous calculated closing. Enter opening overrides with a reason in Daily inputs.");
                window.FocusedWorkspaceHost.Content = workspace;
                window.FocusedWorkspaceLayer.Visibility = Visibility.Visible;
                window.WelcomeOverlay.Visibility = Visibility.Collapsed;
                var root = (FrameworkElement)window.Content;
                window.Content = null; root.Resources = window.Resources;
                Grid.SetRow(window.SectionTabs, 1); Grid.SetColumn(window.SectionTabs, 0); Grid.SetColumnSpan(window.SectionTabs, 4);
                Layout(root, 816, 440);
                var grid = Visuals(workspace).OfType<DataGrid>().Single();
                var renderedRow = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromItem(row));
                var viewport = Visuals(grid).OfType<ScrollContentPresenter>().First();
                var bounds = renderedRow.TransformToAncestor(viewport).TransformBounds(new Rect(renderedRow.RenderSize));
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight,
                    $"Cash row occupies {bounds.Top}–{bounds.Bottom} DIP in a {viewport.ActualHeight} DIP viewport.");
                Assert.True(renderedRow.ActualHeight >= 44);
                Assert.Equal(Visibility.Visible, workspace.DateFromPicker.Visibility);
                Assert.Equal(Visibility.Visible, workspace.DateToPicker.Visibility);
                Assert.True(workspace.DateFromPicker.ActualHeight >= 44 && workspace.DateToPicker.ActualHeight >= 44);
                var filter = Visuals(workspace).OfType<ReportDetailFilter>().Single();
                filter.Search.Text = "Expenses"; Assert.Same(row, grid.Items[0]);
                Assert.True(filter.Search.ActualHeight >= 44 && filter.VarianceOnly.ActualHeight >= 44);
                var actions = Visuals(workspace).OfType<Button>().Single(button => button.Content?.ToString() == "Actions ▾");
                Assert.Equal(Visibility.Visible, actions.Visibility);
                Assert.True(actions.ActualHeight >= 44);
                ReportWorkspaceAction? requested = null;
                workspace.ActionRequested += (_, request) => requested = request.Action;
                var pdf = actions.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Export PDF");
                Assert.True(pdf.IsEnabled); pdf.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal(ReportWorkspaceAction.ExportPdf, requested);
                workspace.DateToPicker.SelectedDate = new DateTime(2026, 8, 26);
                Assert.False(pdf.IsEnabled);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
    }

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
