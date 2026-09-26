using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class PhaseFiveNavigationStateTests
{
    [Theory]
    [InlineData("reports-list")]
    [InlineData("favourite-reports")]
    public void Report_lists_reopen_the_workspace_after_help_closes_without_a_return_view(string taskId)
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                // Help can open while no focused content exists, such as initial
                // setup. Closing that real view intentionally collapses the layer.
                window.FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
                window.FocusedWorkspaceHost.Content = null;
                window.focusedWorkspaceKind = null;
                window.ShowHelpWorkspace("sales-reports");
                var help = Assert.IsType<HelpCentreView>(window.FocusedWorkspaceHost.Content);
                Descendants(help).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Close Help Centre")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, window.FocusedWorkspaceLayer.Visibility);
                Assert.Null(window.FocusedWorkspaceHost.Content);

                var navigator = new TaskNavigator(window);
                Assert.True(navigator.DisplayTaskRoute(TaskNavigation.Find(taskId)!.Route));
                var content = Assert.IsAssignableFrom<UserControl>(window.FocusedWorkspaceHost.Content);
                Assert.Equal(taskId == "reports-list" ? typeof(ReportListView) : typeof(FavouriteReportsView), content.GetType());
                Assert.Equal(Visibility.Visible, window.FocusedWorkspaceLayer.Visibility);
                Assert.Equal("task", window.focusedWorkspaceKind);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    [Fact]
    public void Favourites_replaces_the_help_workspace_kind_when_opened_from_help()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                window.ShowHelpWorkspace("sales-reports");
                Assert.Equal("help", window.focusedWorkspaceKind);
                var navigator = new TaskNavigator(window);
                Assert.True(navigator.DisplayTaskRoute(TaskNavigation.Find("favourite-reports")!.Route));
                Assert.IsType<FavouriteReportsView>(window.FocusedWorkspaceHost.Content);
                Assert.Equal("task", window.focusedWorkspaceKind);
                Assert.Equal(Visibility.Visible, window.FocusedWorkspaceLayer.Visibility);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) throw new InvalidOperationException("Phase 5 navigation state regression.", failure);
    }
}
