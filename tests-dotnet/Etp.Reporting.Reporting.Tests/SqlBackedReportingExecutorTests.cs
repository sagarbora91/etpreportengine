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

    // 1.9.8 (RA-SALES-04): the query's brand-row column reaches the Brand key; a null row becomes an Unmapped row.
    [Fact]
    public async Task Executor_keys_the_brand_dimension_on_the_query_rows_brand_row()
    {
        var repository = new FakeRepository
        {
            Sales = [new(new(2026, 7, 1), "S1", "I1", "1", "P1", "HELIOS", "CTZNG", "SALE", 1m, 118m, 100m, 2027, "CITIZEN"),
                new(new(2026, 7, 1), "S1", "I2", "1", "P1", "HELIOS", "CGSHG", "SALE", 1m, 59m, 50m, 2027, null)]
        };

        var result = await Executor(repository).ExecuteSalesSummaryAsync(Scope(), SalesSummaryDimension.Brand);

        Assert.Equal(["CITIZEN", "Unmapped: HELIOS / CGSHG"], result.Rows.Select(x => x.Key));
        Assert.Contains("S1 HELIOS / CGSHG 50.00", result.Message);
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
        Assert.All(result.Documents, row => Assert.Equal(100m, Math.Abs(row.Variance!.Value)));
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
        Assert.Contains("Compared source-signed invoice and tender values", result.Message);
        Assert.DoesNotContain("failed", result.Message);
    }

    [Fact]
    public async Task Blocked_tender_reconciliation_keeps_the_failed_documents_and_the_reconciliation_message()
    {
        // A missing R022 day must not hide that documents on other days failed (review of 1.9.4 L3).
        var repository = new FakeRepository
        {
            InvoiceControls = [new("S1", "I1", 1000m), new("S1", "I2", 500m)],
            Tenders = [new("S1", "I1", "CARD", 500m), new("S1", "I2", "CARD", 500m)],
            Gaps = [new("S1", new(2024, 8, 5))]
        };

        var result = await Executor(repository).ExecuteTenderReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("R022 missing / not imported for 1 store-day(s)", result.Message);
        Assert.Contains("1 document(s) failed; variance 500.00.", result.Message);
        Assert.Contains("Compared source-signed invoice and tender values", result.Message);
        Assert.Contains("Tender modes: CARD: 1,000.00", result.Message);
        Assert.Single(result.Documents, x => x.Status == ReconciliationStatus.Failed);
    }

    [Fact]
    public async Task Invoices_of_a_store_without_r022_are_listed_with_a_blank_tender_and_counted_in_the_invoice_total()
    {
        // RA-TENDER-02 / RA-UI-01: S2 has sales but no R022, so it has neither controls nor tenders. Its invoices come from
        // the sales lines, are listed Blocked with no tender, and the headline invoice total includes them.
        var repository = new FakeRepository
        {
            InvoiceControls = [new("S1", "I1", 80m)],
            Tenders = [new("S1", "I1", "CARD", 80m)],
            Gaps = [new("S2", new(2026, 9, 1))],
            GapInvoices = [new("S2", "T1", 500m, 2027), new("S2", "T2", -20m, 2027)]
        };

        var result = await Executor(repository).ExecuteTenderReconciliationAsync(new(new(2026, 9, 1), new(2026, 9, 28)));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Equal(560m, result.InvoiceTotal);
        Assert.Equal(80m, result.TenderTotal);
        Assert.Equal(0m, result.Variance);
        Assert.Equal([("S1", "I1", ReconciliationStatus.Passed), ("S2", "T1", ReconciliationStatus.Blocked), ("S2", "T2", ReconciliationStatus.Blocked)],
            result.Documents.Select(x => (x.StoreCode, x.DocumentNumber, x.Status)));
        var listed = result.Documents.Single(x => x.DocumentNumber == "T1");
        Assert.Equal(500m, listed.InvoiceAmount);
        Assert.Null(listed.TenderAmount);
        Assert.Null(listed.Variance);
        Assert.Equal(2027, listed.InvoiceYear);
        Assert.Contains("Listed with a blank tender, not reconciled: S2 2 invoice(s) 480.00.", result.Message);
        Assert.Contains("Compared source-signed invoice and tender values", result.Message);
        Assert.EndsWith("Tender modes: CARD: 80.00", result.Message);
    }

    [Fact]
    public async Task Store_without_any_r022_lists_its_invoices_instead_of_no_evidence_and_an_empty_tender_modes_line()
    {
        // RA-UI-02: WLMHW alone for September read "invoice 0.00, tender 0.00 ... No invoice or tender evidence ... Tender modes: ".
        var repository = new FakeRepository
        {
            Gaps = [new("S2", new(2026, 9, 1)), new("S2", new(2026, 9, 2))],
            GapInvoices = [new("S2", "T1", 500m), new("S2", "T2", 300m)]
        };

        var result = await Executor(repository).ExecuteTenderReconciliationAsync(new(new(2026, 9, 1), new(2026, 9, 28), ["S2"]));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Equal(800m, result.InvoiceTotal);
        Assert.Equal(0m, result.TenderTotal);
        Assert.Equal(2, result.Documents.Count);
        Assert.All(result.Documents, x => Assert.Null(x.TenderAmount));
        Assert.StartsWith("R022 missing / not imported for 2 store-day(s) with sales (S2: 01 Sep 2026, 02 Sep 2026).", result.Message);
        Assert.Contains("S2 2 invoice(s) 800.00", result.Message);
        Assert.DoesNotContain("No invoice or tender evidence", result.Message);
        Assert.DoesNotContain("Tender modes:", result.Message);
    }

    [Fact]
    public async Task No_evidence_at_all_keeps_the_no_evidence_message_without_a_tender_modes_suffix()
    {
        var result = await Executor(new FakeRepository()).ExecuteTenderReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Equal("No invoice or tender evidence is available for reconciliation.", result.Message);
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
        var item = Assert.Single(result.Items);
        Assert.Null(item.ReportedClosing);
        Assert.Equal(ReconciliationStatus.Blocked, item.Status);
    }

    [Fact]
    public async Task Store_without_a_closing_snapshot_on_the_to_date_is_blocked_with_the_date_and_its_items_kept()
    {
        // Titan report audit R-04: a sold-out item's closing is 0 only when the store has a snapshot that day; with none, it
        // is null. Owner answer Q9: those items are listed, marked "no snapshot", not hidden.
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, null)], [new("S1", "P1", "ISSUE", -1m)], [new("S1", new DateOnly(2026, 8, 25))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(Scope());

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Closing stock missing for 25 Aug 2026 (S1); items are listed without a closing figure (no snapshot).", result.Message);
        var item = Assert.Single(result.Items);
        Assert.Equal((1m, -1m, 0m), (item.Opening, item.SourceSignedMovements, item.ExpectedClosing));
        Assert.Null(item.ReportedClosing);
        Assert.Null(item.Variance);
        Assert.Equal(ReconciliationStatus.Blocked, item.Status);
    }

    [Fact]
    public async Task Stores_with_a_snapshot_keep_their_check_beside_a_store_without_one()
    {
        var repository = new FakeRepository
        {
            Stock = new([new("S1", "P1", 1m, null), new("S2", "P2", 2m, 1m), new("S2", "P3", 1m, 0m)],
                [new("S1", "P1", "ISSUE", -1m), new("S2", "P2", "ISSUE", -1m)],
                [new("S1", new DateOnly(2026, 8, 25)), new("S2", new DateOnly(2026, 8, 25))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(new(new(2026, 7, 1), new(2026, 8, 25)));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Closing stock missing for 25 Aug 2026 (S1);", result.Message);
        Assert.DoesNotContain("S2", result.Message.Split(';')[0]);
        Assert.Equal([("S1", "P1", ReconciliationStatus.Blocked), ("S2", "P2", ReconciliationStatus.Passed), ("S2", "P3", ReconciliationStatus.Failed)],
            result.Items.Select(x => (x.StoreCode, x.ItemCode, x.Status)));
        Assert.Equal((decimal?)1m, result.Items.Single(x => x.ItemCode == "P3").Variance);
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
    public async Task Store_in_scope_with_no_movement_in_the_period_is_named_beside_the_store_with_rows()
    {
        // RA-STOCK-03: WLMHW for 1-28 Sep had no ledger row in the period (its ledger ends 25 Aug) and no snapshot on 28 Sep,
        // so it had no position, and the coverage read only looked at stores with rows. Now the coverage row is seeded
        // from the scoped stores, so the short ledger is reported next to HEMW's missing snapshot.
        var repository = new FakeRepository
        {
            Stock = new([new("S2", "P1", 1m, null)], [new("S2", "P1", "ISSUE", -1m)],
                [new("S1", new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26)), new("S2", new DateOnly(2026, 9, 28))])
        };

        var result = await Executor(repository).ExecuteStockReconciliationAsync(new(new(2026, 9, 1), new(2026, 9, 28)));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Ledger covers to 25 Aug 2026 for S1 (sales on 26 Aug 2026 are not in it), before the To date 28 Sep 2026.", result.Message);
        Assert.Contains("Closing stock missing for 28 Sep 2026 (S2)", result.Message);
        Assert.Equal("S2", Assert.Single(result.Items).StoreCode);
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
        public IReadOnlyList<TenderCoverageGapRow> Gaps { get; init; } = [];
        public IReadOnlyList<TenderGapInvoiceRow> GapInvoices { get; init; } = [];
        public Task<IReadOnlyList<TenderCoverageGapRow>> LoadTenderCoverageGapsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Gaps);
        public Task<IReadOnlyList<TenderGapInvoiceRow>> LoadTenderGapInvoicesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(GapInvoices);
        public Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Sales);
        public Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(InvoiceControls);
        public Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Tenders);
        public Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) => Task.FromResult(Stock);
    }
}
