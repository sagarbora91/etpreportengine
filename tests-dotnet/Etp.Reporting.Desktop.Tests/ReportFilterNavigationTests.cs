using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Application.Access;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportFilterNavigationTests
{
    [Theory]
    [InlineData(AccessRole.Viewer)]
    [InlineData(AccessRole.StoreManager)]
    [InlineData(AccessRole.Owner)]
    public void Report_header_recovers_query_inputs_after_other_tasks_and_cached_reports(AccessRole role)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                typeof(MainWindow).GetField("currentAccess", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(window, new AccessSession("synthetic", "Synthetic report reader", role, true));
                foreach (var report in new[] { "sales-brand", "sales-item", "sales-brand" })
                {
                    window.OpenSection("Settings", "Display");
                    window.OpenSection("Reports");
                    var list = Assert.IsType<ReportListView>(window.FocusedWorkspaceHost.Content);
                    var title = TaskNavigation.Find("report-" + report)!.Title;
                    var button = Descendants(list).OfType<Button>().First(item => Equals(item.Content, title));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var header = Assert.IsType<ReportWorkspaceControl>(window.FocusedWorkspaceHost.Content);
                    var filters = Descendants(header).OfType<Expander>().First(item => Equals(item.Header, "Filters"));
                    Assert.Equal(Visibility.Visible, filters.Visibility);
                    filters.IsExpanded = true;
                    var input = (TextBox)window.reportsWorkspaceView.FindName("BrandSegmentFilterInput");
                    Assert.Contains(input, Descendants(filters));
                    Assert.True(input.IsEnabled);
                    input.Text = "SYNTHETIC-SEGMENT";
                    Assert.False(header.HasCurrentPreview);
                    Assert.Contains("refresh", Descendants(header).OfType<TextBlock>().First(item => item.Text == "Applied scope: refresh required.").Text);
                    input.Clear();
                }
            }
            catch (Exception error) { failure = error; }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
