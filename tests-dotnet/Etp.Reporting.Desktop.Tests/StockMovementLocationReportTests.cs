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
            Assert.Equal(["Store", "Item", "Location", "Movement Type", "Signed Quantity"], data.Columns.Select(column => column.Header).ToArray());
            Assert.Equal(["RETAILBIN", "DEFECTIVEBIN", null], data.Rows.Select(row => row[2]).ToArray());
            Assert.Equal(5m, data.Rows.Sum(row => (decimal)row[4]!));
        });
    }

    private sealed class MovementQuery : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockMovementRecord>>([
                new("WLMHW", "ITEM", "Purchase Receipt", 3m, "RETAILBIN"),
                new("WLMHW", "ITEM", "Stock Receipt", 1m, "DEFECTIVEBIN"),
                new("WLMHW", "ITEM", "SR", 1m)]);
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
