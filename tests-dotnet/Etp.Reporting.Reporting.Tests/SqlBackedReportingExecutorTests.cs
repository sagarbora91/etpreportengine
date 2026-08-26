using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class SqlBackedReportingExecutorTests
{
    [Fact]
    public async Task Executor_loads_and_summarizes_query_rows_using_approved_net_amount()
    {
        var repository = new FakeRepository
        {
            Sales = [new(new(2026, 7, 1), "S1", "I1", "1", "P1", "Brand", "Segment", "SALE", 2m, 250m, 200m)]
        };

        var result = await Executor(repository).ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Brand);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(200m, Assert.Single(result.Rows).SourceSignedNetAmount);
    }

    [Fact]
    public async Task Executor_blocks_unmapped_source_transaction_type()
    {
        var repository = new FakeRepository
        {
            Sales = [new(new(2026, 7, 1), "S1", "I1", "1", "P1", "Brand", "Segment", "NEW", 1m, 10m, 10m)]
        };

        var result = await Executor(repository).ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Daily);

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public async Task Executor_reconciles_sql_invoice_and_tender_inputs()
    {
        var repository = new FakeRepository
        {
            InvoiceControls = [new("S1", "I1", 80m)],
            Tenders = [new("S1", "I1", "CARD", 80m)]
        };

        var result = await Executor(repository).ExecuteTenderReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(80m, result.InvoiceTotal);
    }

    [Fact]
    public async Task Executor_requires_both_stock_snapshots()
    {
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 10m, null)], [])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Items);
    }

    private static ReportingQueryScope Scope() => new(new(2026, 7, 1), new(2026, 8, 25), ["S1"]);

    private static SqlBackedReportingExecutor Executor(IReportingQueryRepository repository)
    {
        const string version = "approved-v1";
        var mapping = new ApprovedReportingMapping(version, ApprovedSalesAmountSource.Net,
            new Dictionary<string, ReportingTransactionType>(StringComparer.OrdinalIgnoreCase)
            { ["SALE"] = ReportingTransactionType.Sale, ["RETURN"] = ReportingTransactionType.Return },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CARD", "CASH" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RECEIPT", "ISSUE" });
        var policy = new ApprovedSalesReportingPolicy(version,
            new HashSet<ReportingTransactionType> { ReportingTransactionType.Sale, ReportingTransactionType.Return });
        return new(repository, mapping, policy, new(version, 0m), new(version, 0m));
    }

    private sealed class FakeRepository : IReportingQueryRepository
    {
        public IReadOnlyList<SalesQueryRow> Sales { get; init; } = [];
        public IReadOnlyList<TenderQueryRow> Tenders { get; init; } = [];
        public IReadOnlyList<InvoiceControlQueryRow> InvoiceControls { get; init; } = [];
        public StockQueryData Stock { get; init; } = new([], []);
        public Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Sales);
        public Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(InvoiceControls);
        public Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Tenders);
        public Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Stock);
    }
}
