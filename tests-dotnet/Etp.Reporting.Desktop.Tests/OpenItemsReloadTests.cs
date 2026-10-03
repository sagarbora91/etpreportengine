namespace Etp.Reporting.Desktop.Tests;

// R-WLMHW-10 (report audit, 3 Oct 2026), review of FIX-10: Open items loaded once per session, so an import
// made after the first open never reached the grid. Every open of the task now reloads the view.
[Collection(WpfViewCollection.Name)]
public sealed class OpenItemsReloadTests
{
    [Fact]
    public void Opening_open_items_again_reloads_the_view_every_time()
    {
        Sta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                var navigator = new TaskNavigator(window);
                var route = TaskNavigation.Find("open-items")!.Route;

                Assert.True(navigator.DisplayTaskRoute(route));
                var afterFirst = window.operationsWorkspaceView.RefreshesStarted;
                Assert.True(navigator.DisplayTaskRoute(route));
                var afterSecond = window.operationsWorkspaceView.RefreshesStarted;
                Assert.True(navigator.DisplayTaskRoute(route));

                Assert.True(afterFirst >= 1);
                Assert.Equal(afterFirst + 1, afterSecond);
                Assert.Equal(afterSecond + 1, window.operationsWorkspaceView.RefreshesStarted);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)));
        if (failure is not null) throw new InvalidOperationException("Open items reload regression.", failure);
    }
}
