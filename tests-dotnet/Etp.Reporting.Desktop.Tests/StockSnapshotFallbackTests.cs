using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.9, RA-STOCK-01: Closing, Brand, Slow and Physical Stock read each store's latest snapshot on or before the To date
/// and say which day they read ("Snapshot of 29 Sep 2026 (latest on or before 05 Oct 2026) for HEMW.") in the status line
/// and the export; they stay Blocked, naming the store, only when no snapshot exists that early.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class StockSnapshotFallbackTests
{
    private static readonly DateOnly Requested = new(2026, 8, 25);
    private static readonly DateOnly Earlier = new(2026, 8, 7);

    [Fact]
    public void Fallback_text_names_each_earlier_snapshot_day_and_its_stores()
    {
        Assert.Equal("", ReportsWorkspaceView.FallbackSnapshotText([("HEMW", Requested), ("WLMHW", Requested)], Requested));
        Assert.Equal("", ReportsWorkspaceView.FallbackSnapshotText([], Requested));
        Assert.Equal(" Snapshot of 07 Aug 2026 (latest on or before 25 Aug 2026) for WLMHW.",
            ReportsWorkspaceView.FallbackSnapshotText([("HEMW", Requested), ("WLMHW", Earlier), ("WLMHW", Earlier)], Requested));
        Assert.Equal(" Snapshot of 02 Jul 2026 (latest on or before 25 Aug 2026) for WLMHW; Snapshot of 07 Aug 2026 (latest on or before 25 Aug 2026) for HEMW, TEST.",
            ReportsWorkspaceView.FallbackSnapshotText([("TEST", Earlier), ("HEMW", Earlier), ("WLMHW", new(2026, 7, 2))], Requested));
    }

    [Fact]
    public void Physical_snapshot_text_names_the_day_read_or_the_missing_snapshot()
    {
        Assert.Equal("", ReportsWorkspaceView.PhysicalSnapshotText("HEMW", [Requested, Requested], Requested));
        Assert.Equal(" Snapshot of 07 Aug 2026 (latest on or before 25 Aug 2026) for HEMW.", ReportsWorkspaceView.PhysicalSnapshotText("HEMW", [Earlier, null], Requested));
        Assert.Equal(" No closing-stock snapshot for HEMW on or before 25 Aug 2026; import the Closing Stock export.",
            ReportsWorkspaceView.PhysicalSnapshotText("HEMW", [null, null], Requested));
        Assert.Equal(" No closing-stock snapshot for HEMW on or before 25 Aug 2026; import the Closing Stock export.",
            ReportsWorkspaceView.PhysicalSnapshotText("HEMW", [], Requested));
    }

    [Theory]
    [InlineData("stock-closing")]
    [InlineData("stock-slow")]
    [InlineData("stock-brand")]
    [InlineData("stock-physical")]
    public void Stock_report_on_a_day_without_a_snapshot_shows_the_earlier_one_and_says_so(string code)
    {
        RunSta(async () =>
        {
            var view = SyntheticReportView.Create(out var latest, snapshotDate: Earlier);
            await view.RunReportAsync(code);
            var text = ((TextBlock)view.FindName("ReportResult")).Text;
            const string note = "Snapshot of 07 Aug 2026 (latest on or before 25 Aug 2026) for WLMHW.";
            Assert.Contains(note, text, StringComparison.Ordinal);
            Assert.DoesNotContain("No closing-stock snapshot", text, StringComparison.Ordinal);
            Assert.DoesNotContain("No data", text, StringComparison.Ordinal);
            Assert.NotEmpty(((DataGrid)view.FindName("ReportGrid")).Items.Cast<object>());
            Assert.Contains(note, latest().ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.NotEqual("Blocked", latest().ExportMetadata!.Status);
        });
    }

    [Theory]
    [InlineData("stock-closing")]
    [InlineData("stock-brand")]
    [InlineData("stock-physical")]
    public void Stock_report_on_a_snapshot_day_adds_no_fallback_note(string code)
    {
        RunSta(async () =>
        {
            var view = SyntheticReportView.Create(out var latest);
            await view.RunReportAsync(code);
            Assert.DoesNotContain("latest on or before", ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.Ordinal);
            Assert.DoesNotContain("latest on or before", latest().ExportMetadata!.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("stock-closing")]
    [InlineData("stock-physical")]
    public void Stock_report_with_no_snapshot_on_or_before_the_date_stays_blocked_and_names_the_store(string code)
    {
        RunSta(async () =>
        {
            var view = SyntheticReportView.Create(out var latest, empty: true);
            await view.RunReportAsync(code);
            Assert.Contains("No closing-stock snapshot for WLMHW on or before 25 Aug 2026; import the Closing Stock export.",
                ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.Ordinal);
            Assert.Equal("Blocked", latest().ExportMetadata!.Status);
        });
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Stock snapshot fallback test failed", failure);
    }
}
