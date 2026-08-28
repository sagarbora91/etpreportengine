using Etp.Reporting.Application.Dashboard;
using Etp.Reporting.Desktop.Modules.Dashboard;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DashboardPresentationSessionTests
{
    [Fact]
    public void Dashboard_session_owns_latest_snapshot_and_management_summary_projection()
    {
        var session = new DashboardPresentationSession();
        var snapshot = Snapshot();

        var state = session.Show(snapshot);
        var summary = session.BuildManagementSummary(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 25),
            new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

        Assert.True(session.HasSnapshot);
        Assert.Same(state, session.Current);
        Assert.Equal("2", state.ImportedFiles);
        Assert.Equal("ETP Management Summary", summary.Metadata.ReportName);
        Assert.Equal(2, summary.Data.Rows.Count);
        Assert.Equal("Total", summary.Data.Totals![0]);
        Assert.Equal(2, summary.Data.Totals[1]);
        Assert.Equal(30L, summary.Data.Totals[2]);
    }

    [Fact]
    public void Dashboard_error_preserves_last_safe_view_and_export_snapshot()
    {
        var session = new DashboardPresentationSession();
        var original = session.Show(Snapshot());

        var failed = session.ShowError("Connection failed");

        Assert.True(session.HasSnapshot);
        Assert.Equal(original.RecentImports, failed.RecentImports);
        Assert.Equal("Connection failed", failed.ErrorMessage);
        Assert.NotNull(session.BuildManagementSummary(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 25), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Management_summary_requires_a_successful_dashboard_snapshot()
    {
        var session = new DashboardPresentationSession();
        var error = Assert.Throws<InvalidOperationException>(() => session.BuildManagementSummary(
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 25), DateTimeOffset.UtcNow));
        Assert.Equal("Refresh the dashboard before exporting a management summary.", error.Message);
    }

    [Fact]
    public void MainWindow_has_no_latest_dashboard_snapshot_or_summary_calculation()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var compositionRoot = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var dashboardView = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Dashboard", "DashboardView.cs"));

        Assert.DoesNotContain("latestDashboardSnapshot", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("RecentImports.GroupBy", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("new ExcelReportMetadata(\"ETP Management Summary\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardPresentationSession", mainWindow, StringComparison.Ordinal);
        Assert.Contains("new DashboardPresentationSession()", compositionRoot, StringComparison.Ordinal);
        Assert.Contains("presentation.BuildManagementSummary", dashboardView, StringComparison.Ordinal);
    }

    private static DashboardSnapshot Snapshot() => new(
        2,
        2,
        30,
        new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc),
        [
            new("r025", "R025", "Completed", 10, new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 25, 8, 1, 0, DateTimeKind.Utc)),
            new("r022", "R022", "Completed", 5, new DateTime(2026, 8, 25, 8, 2, 0, DateTimeKind.Utc), new DateTime(2026, 8, 25, 8, 3, 0, DateTimeKind.Utc)),
            new("r025-2", "R025", "Completed", 15, new DateTime(2026, 8, 25, 8, 4, 0, DateTimeKind.Utc), new DateTime(2026, 8, 25, 8, 5, 0, DateTimeKind.Utc))
        ],
        new(DashboardHealthSeverity.Healthy, 100m, new DateTime(2026, 8, 25, 1, 0, 0, DateTimeKind.Utc), 0, 50m, []),
        []);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
