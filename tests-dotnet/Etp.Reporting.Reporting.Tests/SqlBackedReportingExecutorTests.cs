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

    [Fact]
    public async Task Store_without_a_closing_snapshot_on_the_to_date_is_blocked_with_the_date()
    {
        // WLMHW FIX-04: a sold-out item's closing is 0 only when the store has a snapshot that day; with none, it is null.
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, null)], [new("S1", "P1", "ISSUE", -1m)], [new("S1", new DateOnly(2026, 8, 25))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Items);
        Assert.StartsWith("Closing stock missing for 25 Aug 2026 (S1).", result.Message);
    }

    [Fact]
    public async Task Sold_out_item_with_a_zero_closing_is_checked()
    {
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "SOLD", 1m, 0m), new("S1", "GAP", 0m, 0m)],
                [new("S1", "SOLD", "ISSUE", -1m), new("S1", "GAP", "RECEIPT", 1m)], [new("S1", new DateOnly(2026, 8, 25))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Failed, result.Status);
        Assert.Equal(ReconciliationStatus.Passed, result.Items.Single(x => x.ItemCode == "SOLD").Status);
        Assert.Equal(1m, result.Items.Single(x => x.ItemCode == "GAP").Variance);
    }

    [Fact]
    public async Task Ledger_ending_before_the_to_date_blocks_and_says_how_far_it_goes()
    {
        // WLMHW FIX-13: the ledger ends 25 Aug, the check runs to 29 Sep, and S1 has sales on 26 Aug the ledger lacks.
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, 1m), new("S2", "P2", 2m, 1m)],
                [new("S2", "P2", "ISSUE", -1m)],
                [new("S1", new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26)), new("S2", new DateOnly(2026, 9, 29))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(new(new(2026, 7, 1), new(2026, 9, 29)));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Ledger covers to 25 Aug 2026 for S1 (sales on 26 Aug 2026 are not in it), before the To date 29 Sep 2026.", result.Message);
        Assert.DoesNotContain("S2", result.Message);
        // The items stay listed for review.
        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ReconciliationStatus.Passed, item.Status));
    }

    [Fact]
    public async Task Store_with_no_ledger_at_all_is_named()
    {
        var repository = new FakeRepository
        {
            Stock = new([], [], [new("S1", null)])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("No stock ledger is imported for S1, before the To date 25 Aug 2026.", result.Message);
    }

    [Fact]
    public async Task Ledger_covering_the_to_date_keeps_the_result()
    {
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, 1m)], [], [new("S1", new DateOnly(2026, 8, 25))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.DoesNotContain("Ledger covers", result.Message);
    }

    [Fact]
    public async Task Ledger_ending_before_the_to_date_with_no_sales_after_it_is_a_quiet_gap_not_a_block()
    {
        // The import stores a ledger's last movement as its end: a ledger exported to 25 Aug whose store had no stock
        // movement on 25 Aug ends on 24 Aug. With no sale after 24 Aug the result stands, with a note.
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, 1m)], [], [new("S1", new DateOnly(2026, 8, 24), null)])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.DoesNotContain("Ledger covers", result.Message);
        Assert.EndsWith("Last ledger movement 24 Aug 2026 for S1; no sales after it to 25 Aug 2026, so those days are taken as days without stock movement.", result.Message);
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
