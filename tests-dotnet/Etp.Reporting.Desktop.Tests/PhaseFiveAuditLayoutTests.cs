using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class PhaseFiveAuditLayoutTests
{
    [Fact]
    public void Cash_entry_task_keeps_the_cash_book_fields_in_its_focused_tree()
    {
        WithWindow((window, navigator) =>
        {
            var fields = (WrapPanel)window.dailyWorkflowWorkspace.FindName("CashQuickFields");
            navigator.DisplayTaskRoute(TaskNavigation.Find("cash-input")!.Route);
            Assert.Contains(fields, Descendants(window.FocusedWorkspaceHost));
        });
    }

    [Fact]
    public void Investigation_and_approvals_keep_the_refresh_approvals_action_on_approvals_only()
    {
        WithWindow((window, navigator) =>
        {
            navigator.DisplayTaskRoute(TaskNavigation.Find("investigation")!.Route);
            Assert.DoesNotContain(Descendants(window.FocusedWorkspaceHost).OfType<Button>(), button => button.Name == "RefreshApprovalsButton");
            navigator.DisplayTaskRoute(TaskNavigation.Find("approval-centre")!.Route);
            Assert.Contains(Descendants(window.FocusedWorkspaceHost).OfType<Button>(), button => button.Name == "RefreshApprovalsButton");
            navigator.DisplayTaskRoute(TaskNavigation.Find("investigation")!.Route);
            Assert.DoesNotContain(Descendants(window.FocusedWorkspaceHost).OfType<Button>(), button => button.Name == "RefreshApprovalsButton");
        });
    }

    [Theory]
    [InlineData("users")]
    [InlineData("kpi")]
    [InlineData("health")]
    [InlineData("stores")]
    public void Administration_tasks_keep_the_status_line_and_health_heading(string id)
    {
        WithWindow((window, navigator) =>
        {
            var healthHeading = Assert.IsType<TextBlock>(((Panel)window.administrationWorkspaceView.Content).Children[12]);
            Assert.Equal("Integration health", healthHeading.Text);
            navigator.DisplayTaskRoute(TaskNavigation.Find(id)!.Route);
            var text = Descendants(window.FocusedWorkspaceHost).OfType<TextBlock>().ToArray();
            Assert.Contains(text, block => block.Name == "AdministrationStatus");
            Assert.Contains(healthHeading, text);
        });
    }

    [Theory]
    [InlineData("support-package", "aggregate-only diagnostic package")]
    [InlineData("backups", "checksum backup and verify it")]
    [InlineData("recovery", "isolated temporary database")]
    public void Maintenance_tasks_use_their_navigation_title_to_show_guidance_and_a_heading(string id, string guidance)
    {
        WithWindow((window, navigator) =>
        {
            // Use the real route/title. Copying a title into the test would hide the drift.
            navigator.DisplayTaskRoute(TaskNavigation.Find(id)!.Route);
            var text = Descendants(window.FocusedWorkspaceHost).OfType<TextBlock>().ToArray();
            Assert.Contains(text, block => block.Text.Contains(guidance, StringComparison.Ordinal));
            Assert.Contains(text, block => block.Text == "Backup, recovery and support");
        });
    }

    [Fact]
    public void Automatic_import_keeps_a_refresh_action_so_a_failed_load_can_be_retried()
    {
        WithWindow((window, navigator) =>
        {
            navigator.DisplayTaskRoute(TaskNavigation.Find("watch-folder")!.Route);
            var refresh = Assert.Single(Descendants(window.FocusedWorkspaceHost).OfType<Button>(),
                button => button.Content?.ToString() == "Refresh operations");
            Assert.True(refresh.IsEnabled);
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

    private static void WithWindow(Action<MainWindow, TaskNavigator> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = Phase3ShellTests.CreateWindow();
                window.operationsWorkspaceView.UpdateAccess(new(true, true, true));
                window.investigationWorkspaceView.UpdateAccess(new(true, true, true));
                window.administrationWorkspaceView.UpdateAccess(new(true, true, true));
                action(window, new TaskNavigator(window));
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                window?.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window?.Close();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "Focused task layout dispatcher did not close.");
        if (failure is not null) throw new InvalidOperationException("Phase 5 audit layout regression.", failure);
    }
}
