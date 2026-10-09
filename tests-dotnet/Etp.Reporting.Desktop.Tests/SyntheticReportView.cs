using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using TenderVarianceCause = Etp.Reporting.Application.Reports.TenderVarianceCause;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// A <see cref="ReportsWorkspaceView"/> over synthetic queries that answer every catalogue report with one row (or none
/// when <c>empty</c>), scoped to 25 Aug 2026 / Titan World. Shared by the grid-presentation tests (1.9.7) and the
/// export-path tests (1.9.8) so the fakes exist once.
/// </summary>
internal static class SyntheticReportView
{
    public static ReportsWorkspaceView Create(out Func<ReportPresentationSnapshot> latest, Action<ReportPresentationSnapshot, System.Collections.IEnumerable?, string>? preview = null, bool empty = false, IReportExportCoordinator? exporter = null)
    {
        ReportPresentationSnapshot? current = null;
        var operational = new OperationalQuery(empty);
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new ControlledQuery(empty), _ => operational,
            _ => new TrendQuery(empty), exporter ?? new ReportExportCoordinator(), new Diagnostic(empty),
            (_, store, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>(empty ? [] : [new(new(2026, 8, 25), store, 100m, "Synthetic opening", new Dictionary<string, decimal> { ["Cash"] = 118m }, 20m, 10m, 5m, 5m, 10m, 0m, 223m, null, 0m, 0m, "Complete")]));
        view.SetStores(TestStoreCatalog.Create());
        view.ApplyScope(new(2026, 8, 25), new(2026, 8, 25), "Titan World");
        view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (snapshot, rows, status) => { current = snapshot; preview?.Invoke(snapshot, rows, status); }, _ => { }, _ => { });
        latest = () => current ?? throw new InvalidOperationException("No report was presented.");
        return view;
    }

    private sealed class ControlledQuery(bool empty) : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, empty ? [] : [new("TITAN", 2m, 236m, 2, 1)], "test", "Aggregated source-signed values without sign transformation."));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenderReconciliationReport(ReportStatus.Failed, empty ? [] : [new("WLMHW", "D1", 118m, 0m, 118m, ReportStatus.Failed, 2027)], 118m, 0m, 118m, "test", "Compared invoice and tender values."));
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StockReconciliationReport(ReportStatus.Passed, empty ? [] : [new("WLMHW", "ITEM-1", 10m, -2m, 8m, 8m, 0m, ReportStatus.Passed)], "test", "Stock reconciles."));
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockMovementRecord>>(empty ? [] : [new("WLMHW", "ITEM-1", "SR", -1m, "RETAILBIN")]);
    }

    private sealed class TrendQuery(bool empty) : IManagementTrendQuery
    {
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagementTrendRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", 236m, 2m, 2, 1, 0m, 0)]);
    }

    private sealed class Diagnostic(bool empty) : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) =>
            new(ReportStatus.Failed, empty ? [] : [new("WLMHW", "D1", 118m, 0m, 118m, TenderVarianceCause.MissingTender, "Import the R022.")], empty ? 0 : 1, 118m, "test", "Classified.");
    }

    private sealed class OperationalQuery(bool empty) : IOperationalReportQuery<DailySalesReportDocument>
    {
        public Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockInventoryRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "ITEM-1", "TITAN", "WATCHES", 2m, 1250m, 2500m, new(2026, 3, 1), 177, "EXCEPTION", "R011", null, null, "TITAN")]);
        public Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceSummaryRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "FIRST", "INV", 1m, 118m, 1, "First customer")]);
        public Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceLineageRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "FIRST", "1", "ITEM-1", "TITAN", "WATCHES", "INV", 1m, 118m, "001", "R025.xlsx", "Sheet1", 2)]);
        public Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StaffPerformanceReport(empty ? [] : [new("WLMHW", "001", 200m, null, null, "MissingSource", 3m, 0m, 2, 1.5m, 100m, 1m, null, null, 1, "Synthetic CRO")], 200m, 200m, 0m, ReportStatus.Passed, "Reconciled.", "test"));
        public Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceSalesRecord>>(empty ? [] : [new("FTD", "WLMHW", businessDate, businessDate, 10m, 20m, 30m, 60m, null, null, "MissingSource", 0, 1, 0m)]);
        public Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhysicalStockRecord>>(empty ? [] : [new(storeCode, businessDate, "TITAN", 1m, 2m, 0m, 0m, 3m, 3m, 0m, 3m, 0m, null, "PASS")]);
        public Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyExceptionRecord>>(empty ? [] : [new("WARN", "Cash", "CASH_RECONCILIATION", storeCode, businessDate, null, null, 5m, "R022.xlsx", "Sheet1", 4, "Variance.", "Check the count.")]);
    }
}
