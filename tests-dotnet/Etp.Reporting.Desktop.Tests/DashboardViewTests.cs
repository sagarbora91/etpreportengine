using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DashboardViewTests
{
    [Fact]
    public void State_formats_metrics_health_and_chart_with_existing_dashboard_rules()
    {
        var imports = new DashboardImportActivity[]
        {
            new("a.xlsx", "R025", "Completed", 1250, new DateTime(2026, 8, 27, 8, 0, 0), null),
            new("b.xlsx", "R022", "Completed", 300, new DateTime(2026, 8, 27, 8, 1, 0), null),
            new("c.xlsx", "R025", "Completed", 750, new DateTime(2026, 8, 27, 8, 2, 0), null)
        };
        var health = new DashboardHealthSnapshot("Warning", 1024.5m, null, 2, null,
            [new("BACKUP_MISSING", "No successful full database backup is recorded.")]);

        var state = DashboardViewState.Create(1234, 1200, 9876543, new DateTime(2026, 8, 27, 9, 15, 0), imports, health, []);

        Assert.Equal("1,234", state.ImportedFiles);
        Assert.Equal("1,200", state.CompletedBatches);
        Assert.Equal("9,876,543", state.SourceRows);
        Assert.Equal("27 Aug 2026 09:15", state.LatestImport);
        Assert.Equal("1,024.50 MB", state.DatabaseSize);
        Assert.Equal("Missing", state.LatestBackup);
        Assert.Equal("Unavailable", state.BackupFreeSpace);
        Assert.Equal(DashboardHealthTone.Warning, state.DatabaseHealthTone);
        Assert.Equal([new DashboardChartItem("R025", 2000), new DashboardChartItem("R022", 300)], state.ImportedRowsByReport);
        Assert.Equal(["BACKUP_MISSING: No successful full database backup is recorded."], state.HealthWarnings);
    }

    [Fact]
    public void Error_state_matches_existing_unavailable_dashboard_display()
    {
        var state = DashboardViewState.Error("Database unavailable");

        Assert.Equal("-", state.ImportedFiles);
        Assert.Equal("-", state.CompletedBatches);
        Assert.Equal("-", state.SourceRows);
        Assert.Equal("Unavailable", state.LatestImport);
        Assert.Equal("Unavailable", state.DatabaseHealth);
        Assert.Empty(state.RecentImports);
        Assert.Empty(state.HealthWarnings);
        Assert.Equal("Database unavailable", state.ErrorMessage);
    }

    [Fact]
    public void View_exposes_refresh_and_accessible_actions()
    {
        RunSta(() =>
        {
            var view = new DashboardView();
            var refreshRaised = false;
            view.RefreshRequested += (_, _) => refreshRaised = true;

            FindButton(view, "Refresh dashboard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton(view, "Export management summary PDF").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(refreshRaised);
        });
    }

    [Fact]
    public void Show_and_show_error_replace_the_complete_presentation_state()
    {
        RunSta(() =>
        {
            var view = new DashboardView();
            var state = DashboardViewState.Create(1, 2, 3, null, [],
                new("Healthy", 4m, null, 0, 5m, []), []);

            view.Show(state);
            Assert.Same(state, view.CurrentState);

            view.ShowError("Refresh failed");
            Assert.Equal("Refresh failed", view.CurrentState?.ErrorMessage);
            Assert.Equal("Unavailable", view.CurrentState?.DatabaseHealth);
            Assert.Same(state.RecentImports, view.CurrentState?.RecentImports);
        });
    }

    private static Button FindButton(DependencyObject root, string automationName)
    {
        if (root is Button button && AutomationProperties.GetName(button) == automationName) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindButtonOrNull(child, automationName);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"Button '{automationName}' was not found.");
    }

    private static Button? FindButtonOrNull(DependencyObject root, string automationName)
    {
        if (root is Button button && AutomationProperties.GetName(button) == automationName) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindButtonOrNull(child, automationName);
            if (found is not null) return found;
        }
        return null;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
