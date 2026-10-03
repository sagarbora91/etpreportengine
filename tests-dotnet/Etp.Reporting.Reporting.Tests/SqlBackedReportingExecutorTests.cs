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
    public async Task Executor_warns_and_skips_unmapped_source_transaction_without_blocking_period()
    {
        var repository = new FakeRepository
        {
            Sales = [new(new(2026, 7, 1), "S1", "I1", "1", "P1", "Brand", "Segment", "NEW", 1m, 10m, 10m),
                new(new(2026, 7, 1), "S1", "I2", "1", "P1", "Brand", "Segment", "SALE", 1m, 118m, 100m)]
        };

        var result = await Executor(repository).ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Daily);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(100m, Assert.Single(result.Rows).SourceSignedNetAmount);
        Assert.Contains("skipped 1 rows", result.Message);
    }

    [Fact]
    public async Task Retail_policy_uses_gross_and_treats_bill_cancellation_as_a_return()
    {
        var repository = new FakeRepository
        {
            Sales = [new(new(2026, 7, 1), "S1", "I1", "1", "P1", "Brand", "Segment", "INV", 2m, 236m, 200m),
                new(new(2026, 7, 1), "S1", "BC1", "1", "P1", "Brand", "Segment", "BC", -1m, -118m, -100m)]
        };
        var executor = new SqlBackedReportingExecutor(repository, RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
        var result = await executor.ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Store);
        Assert.Equal(118m, Assert.Single(result.Rows).SourceSignedNetAmount);
        var returns = await executor.ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Returns);
        Assert.Equal(-118m, Assert.Single(returns.Rows).SourceSignedNetAmount);
    }

    [Fact]
    public async Task Repeated_invoice_numbers_in_different_financial_years_do_not_cancel_variances()
    {
        var repository = new FakeRepository
        {
            InvoiceControls = [new("S1", "100000068", 100m, 2026), new("S1", "100000068", 200m, 2027)],
            Tenders = [new("S1", "100000068", "CARD", 200m, 2026), new("S1", "100000068", "CARD", 100m, 2027)]
        };
        var result = await Executor(repository).ExecuteTenderReconciliationAsync(Scope());
        Assert.Equal(ReconciliationStatus.Failed, result.Status);
        Assert.Equal(2, result.Documents.Count);
        Assert.All(result.Documents, row => Assert.Equal(100m, Math.Abs(row.Variance)));
        Assert.Equal(0m, result.Variance);
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
    public async Task Tender_reconciliation_is_blocked_and_names_dates_when_r022_is_missing_for_a_sales_day()
    {
        // WLMHW FIX-07: no R022 means no controls and no tenders; 0 against 0 must not pass.
        var repository = new FakeRepository
        {
            InvoiceControls = [new("S1", "I1", 80m)],
            Tenders = [new("S1", "I1", "CARD", 80m)],
            Gaps = [new("S1", new(2024, 9, 16)), new("S1", new(2024, 9, 17))]
        };

        var result = await Executor(repository).ExecuteTenderReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Contains("R022 missing / not imported", result.Message);
        Assert.Contains("S1: 16 Sep 2024, 17 Sep 2024", result.Message);
        Assert.Equal(80m, result.InvoiceTotal);
    }

    [Fact]
    public void Tender_gap_message_lists_ten_dates_then_counts_the_rest()
    {
        var gaps = Enumerable.Range(1, 12).Select(day => new TenderCoverageGapRow("S1", new(2024, 10, day))).ToArray();
        var message = SqlBackedReportingExecutor.DescribeTenderGaps(gaps);
        Assert.Contains("10 Oct 2024 and 2 more", message);
        Assert.DoesNotContain("11 Oct 2024", message);
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
        public IReadOnlyList<TenderCoverageGapRow> Gaps { get; init; } = [];
        public Task<IReadOnlyList<TenderCoverageGapRow>> LoadTenderCoverageGapsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Gaps);
        public Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Sales);
        public Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(InvoiceControls);
        public Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Tenders);
        public Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Stock);
    }
}
