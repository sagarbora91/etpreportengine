using System.Threading;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.9.4, WLMHW report audit FIX-14: the Stock Movement report shows the ledger bin (LOCATION) of each group.</summary>
[Collection(WpfViewCollection.Name)]
public sealed class StockMovementLocationReportTests
{
    [Fact]
    public void Stock_movement_report_shows_each_bin_as_its_own_row()
    {
        RunSta(async () =>
        {
            var previews = new List<ReportPresentationSnapshot>();
            var view = new ReportsWorkspaceView(() => "synthetic", _ => new MovementQuery(),
                _ => throw new InvalidOperationException("Unexpected operational query"),
                _ => throw new InvalidOperationException("Unexpected trend query"),
                new ReportExportCoordinator(), new UnusedDiagnostic());
            view.SetStores(TestStoreCatalog.Create());
            view.ApplyScope(new(2026, 9, 1), new(2026, 9, 10), "WLMHW");
            view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (snapshot, _, _) => previews.Add(snapshot), _ => { }, _ => { });

            await view.RunReportAsync("stock-movement");

            var data = Assert.Single(previews).ExportData!;
            Assert.Equal(["Store", "Item", "Location", "Movement Type", "Signed Quantity", "Snapshot"], data.Columns.Select(column => column.Header).ToArray());
            Assert.Equal(["RETAILBIN", "DEFECTIVEBIN", null], data.Rows.Select(row => row[2]).ToArray());
            Assert.Equal(5m, data.Rows.Sum(row => (decimal)row[4]!));
        });
    }

    [Fact]
    public void Stock_movement_of_a_store_without_a_snapshot_is_listed_and_marked()
    {
        // Owner answer Q9 (decision 13): the movements are shown, marked "no snapshot", and the text names the store.
        RunSta(async () =>
        {
            var previews = new List<ReportPresentationSnapshot>();
            var view = new ReportsWorkspaceView(() => "synthetic", _ => new MovementQuery(hasSnapshot: false),
                _ => throw new InvalidOperationException("Unexpected operational query"),
                _ => throw new InvalidOperationException("Unexpected trend query"),
                new ReportExportCoordinator(), new UnusedDiagnostic());
            view.SetStores(TestStoreCatalog.Create());
            view.ApplyScope(new(2026, 9, 1), new(2026, 9, 10), "WLMHW");
            view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (snapshot, _, _) => previews.Add(snapshot), _ => { }, _ => { });

            await view.RunReportAsync("stock-movement");

            var snapshot = Assert.Single(previews);
            Assert.Equal(3, snapshot.ExportData!.Rows.Count);
            Assert.All(snapshot.ExportData.Rows, row => Assert.Equal("no snapshot", row[5]));
            Assert.Contains("No closing-stock snapshot for WLMHW on 10 Sep 2026; movements are shown, closing stock cannot be checked.", snapshot.ExportMetadata!.Message);
        });
    }

    [Fact]
    public void Snapshot_notes_name_only_the_stores_without_one()
    {
        Assert.Equal("", ReportsWorkspaceView.StockMovementSnapshotNote([new("A", "I", "INV", -1m)], new(2026, 9, 10)));
        Assert.Equal(" No closing-stock snapshot for B on 10 Sep 2026; movements are shown, closing stock cannot be checked.",
            ReportsWorkspaceView.StockMovementSnapshotNote([new("A", "I", "INV", -1m), new("B", "I", "INV", -1m, null, false)], new(2026, 9, 10)));
        Assert.Equal(" No closing-stock snapshot for B on 10 Sep 2026.", ReportsWorkspaceView.MissingSnapshotText(["A", "B"], ["A"], new(2026, 9, 10)));
        Assert.Equal(" No closing-stock snapshot for any store on 10 Sep 2026.", ReportsWorkspaceView.MissingSnapshotText(null, [], new(2026, 9, 10)));
        Assert.Equal("", ReportsWorkspaceView.MissingSnapshotText(null, ["A"], new(2026, 9, 10)));
    }

    private sealed class MovementQuery(bool hasSnapshot = true) : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockMovementRecord>>([
                new("WLMHW", "ITEM", "Purchase Receipt", 3m, "RETAILBIN", hasSnapshot),
                new("WLMHW", "ITEM", "Stock Receipt", 1m, "DEFECTIVEBIN", hasSnapshot),
                new("WLMHW", "ITEM", "SR", 1m, null, hasSnapshot)]);
    }

    private sealed class UnusedDiagnostic : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }
}
