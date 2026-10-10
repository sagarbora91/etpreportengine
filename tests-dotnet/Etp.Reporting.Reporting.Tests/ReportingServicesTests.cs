using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ReportingServicesTests
{
    private static readonly ApprovedSalesReportingPolicy SalesPolicy = new("approved-v1",
        new HashSet<ReportingTransactionType> { ReportingTransactionType.Sale, ReportingTransactionType.Return });

    [Theory]
    [InlineData(SalesSummaryDimension.Daily, "2026-07-01")]
    [InlineData(SalesSummaryDimension.Store, "S1")]
    [InlineData(SalesSummaryDimension.Brand, "Brand A")]
    [InlineData(SalesSummaryDimension.BrandSegment, "Brand A / Premium")]
    [InlineData(SalesSummaryDimension.Item, "S1 / ITEM-1")]
    public void Sales_summaries_preserve_source_signs(SalesSummaryDimension dimension, string expectedKey)
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 2m, 200m),
            Line("INV-2", "1", ReportingTransactionType.Return, -1m, -75m)
        };

        var result = new SalesReportingService().Summarize(lines, dimension, SalesPolicy);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        var row = Assert.Single(result.Rows);
        Assert.Equal(expectedKey, row.Key);
        Assert.Equal(1m, row.SourceSignedQuantity);
        Assert.Equal(125m, row.SourceSignedNetAmount);
        Assert.Equal(1, row.Invoices);
        Assert.Equal(1, row.Returns);
    }

    // Owner decision 13 Q6: Invoices counts INV documents only (like the DSR) and Returns counts SR and BC documents.
    [Fact]
    public void Sales_summary_counts_invoices_and_returns_separately()
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 1m, 100m),
            Line("INV-1", "2", ReportingTransactionType.Sale, 1m, 50m),
            Line("INV-2", "1", ReportingTransactionType.Sale, 1m, 80m),
            Line("SR-1", "1", ReportingTransactionType.Return, -1m, -100m),
            Line("BC-1", "1", ReportingTransactionType.Return, -1m, -80m)
        };

        var row = Assert.Single(new SalesReportingService().Summarize(lines, SalesSummaryDimension.Store, SalesPolicy).Rows);

        Assert.Equal(2, row.Invoices);
        Assert.Equal(2, row.Returns);
        Assert.Equal(50m, row.SourceSignedNetAmount);
    }

    [Fact]
    public void A_cancelled_bill_is_one_invoice_and_one_return()
    {
        var lines = new[]
        {
            Line("INV-9", "1", ReportingTransactionType.Sale, 1m, 100m),
            Line("INV-9", "2", ReportingTransactionType.Return, -1m, -100m)
        };

        var row = Assert.Single(new SalesReportingService().Summarize(lines, SalesSummaryDimension.Daily, SalesPolicy).Rows);

        Assert.Equal(1, row.Invoices);
        Assert.Equal(1, row.Returns);
    }

    // RA-SALES-08 (1.9.8): the Returns report keys by store and brand, keeps the source sign, and its Invoices column
    // counts the distinct return documents of the row instead of the INV lines it never holds (always 0 before).
    [Fact]
    public void Returns_summary_includes_only_classified_returns()
    {
        var result = new SalesReportingService().Summarize(
            [Line("INV-1", "1", ReportingTransactionType.Sale, 2m, 200m),
             Line("INV-2", "1", ReportingTransactionType.Return, -1m, -75m)],
            SalesSummaryDimension.Returns, SalesPolicy);

        var row = Assert.Single(result.Rows);
        Assert.Equal("S1 / Brand A", row.Key);
        Assert.Equal(-1m, row.SourceSignedQuantity);
        Assert.Equal(-75m, row.SourceSignedNetAmount);
        Assert.Equal(1, row.Invoices);
        Assert.Equal(1, row.Returns);
    }

    [Fact]
    public void Returns_summary_is_keyed_by_store_and_brand_and_counts_return_documents()
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 3m, 300m),
            Line("SR-1", "1", ReportingTransactionType.Return, -1m, -75m),
            Line("SR-1", "2", ReportingTransactionType.Return, -1m, -25m),
            Line("SR-2", "1", ReportingTransactionType.Return, -1m, -50m),
            Line("SR-3", "1", ReportingTransactionType.Return, -2m, -90m) with { Brand = "Brand B" },
            Line("SR-4", "1", ReportingTransactionType.Return, -1m, -40m) with { StoreCode = "S2" },
            Line("SR-5", "1", ReportingTransactionType.Return, -1m, -10m) with { Brand = " " }
        };

        var result = new SalesReportingService().Summarize(lines, SalesSummaryDimension.Returns, SalesPolicy);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(["S1 / Brand A", "S1 / Brand B", "S1 / Unmapped", "S2 / Brand A"], result.Rows.Select(row => row.Key));
        var brandA = result.Rows[0];
        Assert.Equal(-3m, brandA.SourceSignedQuantity);
        Assert.Equal(-150m, brandA.SourceSignedNetAmount);
        Assert.Equal(2, brandA.Invoices);
        Assert.Equal(2, brandA.Returns);
        Assert.Equal((-90m, 1, 1), (result.Rows[1].SourceSignedNetAmount, result.Rows[1].Invoices, result.Rows[1].Returns));
        Assert.Equal((-40m, 1), (result.Rows[3].SourceSignedNetAmount, result.Rows[3].Invoices));
        Assert.Equal(-290m, result.Rows.Sum(row => row.SourceSignedNetAmount));
    }

    [Fact]
    public void Unknown_sales_type_blocks_the_entire_summary()
    {
        var result = new SalesReportingService().Summarize(
            [Line("INV-1", "1", ReportingTransactionType.Unknown, 1m, 10m)],
            SalesSummaryDimension.Daily, SalesPolicy);

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Invoice_tender_control_reconciles_each_document_and_total()
    {
        var result = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", 100m), new("S1", "I2", -20m)],
            [new("S1", "I1", "CARD", 60m, true), new("S1", "I1", "CASH", 40m, true),
             new("S1", "I2", "REFUND", -20m, true)],
            new ApprovedControlRule("approved-v1", 0.01m));

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(80m, result.InvoiceTotal);
        Assert.Equal(80m, result.TenderTotal);
        Assert.All(result.Documents, x => Assert.Equal(ReconciliationStatus.Passed, x.Status));
    }

    [Fact]
    public void Unknown_tender_type_blocks_reconciliation()
    {
        var result = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "I1", 100m)], [new("S1", "I1", "NEW_TYPE", 100m, false)],
            new ApprovedControlRule("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public void Empty_invoice_and_tender_evidence_cannot_pass_control()
    {
        var result = new InvoiceTenderReconciliationService().Reconcile([], [], new("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public void Explicit_zero_invoice_and_tender_evidence_passes_control()
    {
        var result = new InvoiceTenderReconciliationService().Reconcile(
            [new("S1", "ZERO", 0m)], [new("S1", "ZERO", "CASH", 0m, true)], new("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Single(result.Documents);
    }

    [Fact]
    public void Empty_stock_position_evidence_cannot_pass_control()
    {
        var result = new StockReconciliationService().Reconcile([], [], new("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Explicit_stock_position_with_no_movements_can_pass_control()
    {
        var result = new StockReconciliationService().Reconcile(
            [new("S1", "ITEM-1", 10m, 10m)], [], new("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(10m, Assert.Single(result.Items).ExpectedClosing);
    }

    [Fact]
    public void Stock_control_adds_source_signed_movements()
    {
        var result = new StockReconciliationService().Reconcile(
            [new("S1", "ITEM-1", 10m, 12m)],
            [new("S1", "ITEM-1", "RECEIPT", 5m, true), new("S1", "ITEM-1", "ISSUE", -3m, true)],
            new ApprovedStockControlRule("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        var item = Assert.Single(result.Items);
        Assert.Equal(2m, item.SourceSignedMovements);
        Assert.Equal(12m, item.ExpectedClosing);
    }

    [Fact]
    public void Unknown_stock_movement_blocks_reconciliation()
    {
        var result = new StockReconciliationService().Reconcile(
            [new("S1", "ITEM-1", 10m, 10m)], [new("S1", "ITEM-1", "UNKNOWN", 1m, false)],
            new ApprovedStockControlRule("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Stock_movement_without_a_position_blocks_reconciliation()
    {
        var result = new StockReconciliationService().Reconcile(
            [], [new("S1", "ITEM-1", "RECEIPT", 1m, true)],
            new ApprovedStockControlRule("approved-v1", 0m));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Items);
    }

    // 1.9.8 (RA-SALES-03/04, RA-OPS-05): the Brand dimension is the owner's brand row, as the DSR keys it; a line no row
    // claims keeps its own Unmapped row (brand, plus the cluster when the cluster is not the brand) instead of one "Other".
    [Fact]
    public void Brand_dimension_keys_on_the_owners_brand_row_and_splits_unmapped_lines_by_brand_and_cluster()
    {
        var lines = new[]
        {
            Line("I1", "1", ReportingTransactionType.Sale, 1m, 23_900m, brand: "HELIOS", segment: "CTZNG", row: "CITIZEN"),
            Line("I2", "1", ReportingTransactionType.Sale, 1m, 1_000m, brand: "HELIOS", segment: "SEKOG", row: "SEIKO"),
            Line("I3", "1", ReportingTransactionType.Sale, 1m, 31_690.5m, brand: "HELIOS", segment: "CGSHG", row: null),
            Line("I4", "1", ReportingTransactionType.Sale, 1m, 500m, brand: "HELIOS", segment: "CTZNL", row: null),
            Line("I5", "1", ReportingTransactionType.Sale, 1m, 16_427m, brand: "ZOOP", segment: "zoop", row: null, store: "S2"),
            Line("I6", "1", ReportingTransactionType.Sale, 1m, 7_490m, brand: "CLOCKY", segment: "", row: null, store: "S2")
        };

        var result = new SalesReportingService().Summarize(lines, SalesSummaryDimension.Brand, SalesPolicy);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(["CITIZEN", "SEIKO", "Unmapped: CLOCKY", "Unmapped: HELIOS / CGSHG", "Unmapped: HELIOS / CTZNL", "Unmapped: ZOOP"],
            result.Rows.Select(x => x.Key));
        Assert.Equal(lines.Sum(x => x.SourceSignedNetAmount), result.Rows.Sum(x => x.SourceSignedNetAmount));
        Assert.Equal(31_690.5m, result.Rows.Single(x => x.Key == "Unmapped: HELIOS / CGSHG").SourceSignedNetAmount);
        // The status line names every unmapped brand per store with its value and says where to map it.
        Assert.Contains("shown as Unmapped rows (4, 56,107.50 in this period): S1 HELIOS / CGSHG 31,690.50; S1 HELIOS / CTZNL 500.00; S2 ZOOP 16,427.00; S2 CLOCKY 7,490.00.", result.Message);
        Assert.Contains("Map them in Settings > Stores & masters > Brands and targets.", result.Message);
    }

    [Fact]
    public void Brand_rows_of_the_same_label_merge_across_stores_and_a_fully_mapped_period_has_no_unmapped_note()
    {
        var lines = new[]
        {
            Line("I1", "1", ReportingTransactionType.Sale, 1m, 100m, brand: "TITAN", segment: "TITAN", row: "TITAN", store: "S1"),
            Line("I2", "1", ReportingTransactionType.Sale, 1m, 200m, brand: "TITAN", segment: "TITAN", row: "TITAN", store: "S2")
        };

        var result = new SalesReportingService().Summarize(lines, SalesSummaryDimension.Brand, SalesPolicy);

        var row = Assert.Single(result.Rows);
        Assert.Equal("TITAN", row.Key);
        Assert.Equal(300m, row.SourceSignedNetAmount);
        Assert.DoesNotContain("Unmapped", result.Message);
    }

    [Fact]
    public void Brand_segment_dimension_uses_the_brand_row_and_does_not_repeat_the_cluster_of_an_unmapped_line()
    {
        var lines = new[]
        {
            Line("I1", "1", ReportingTransactionType.Sale, 1m, 100m, brand: "HELIOS", segment: "CTZNG", row: "CITIZEN"),
            Line("I2", "1", ReportingTransactionType.Sale, 1m, 200m, brand: "HELIOS", segment: "CTZNL", row: null)
        };

        var result = new SalesReportingService().Summarize(lines, SalesSummaryDimension.BrandSegment, SalesPolicy);

        Assert.Equal(["CITIZEN / CTZNG", "Unmapped: HELIOS / CTZNL"], result.Rows.Select(x => x.Key));
        Assert.Contains("S1 HELIOS / CTZNL 200.00", result.Message);
    }

    [Fact]
    public void Unmapped_note_is_only_written_for_the_brand_dimensions()
    {
        var lines = new[] { Line("I1", "1", ReportingTransactionType.Sale, 1m, 100m, row: null) };

        Assert.DoesNotContain("Unmapped", new SalesReportingService().Summarize(lines, SalesSummaryDimension.Store, SalesPolicy).Message);
        Assert.Contains("Unmapped", new SalesReportingService().Summarize(lines, SalesSummaryDimension.Brand, SalesPolicy).Message);
    }

    [Fact]
    public void Brand_dimension_is_satisfied_by_a_brand_row_when_the_source_brand_is_blank()
    {
        var mapped = new[] { Line("I1", "1", ReportingTransactionType.Sale, 1m, 100m, brand: "", row: "SEIKO") };
        var blank = new[] { Line("I1", "1", ReportingTransactionType.Sale, 1m, 100m, brand: "", row: null) };

        Assert.Equal("SEIKO", Assert.Single(new SalesReportingService().Summarize(mapped, SalesSummaryDimension.Brand, SalesPolicy).Rows).Key);
        Assert.Equal(ReconciliationStatus.Blocked, new SalesReportingService().Summarize(blank, SalesSummaryDimension.Brand, SalesPolicy).Status);
    }

    // The fixture's lines are mapped to a brand row of the same name as their brand, so the mapped key reads as before.
    // RA-SALES-10 (1.9.9): Item-wise rows are keyed by store and item, so one item code sold in two stores stays two rows.
    [Fact]
    public void Item_summary_keeps_the_same_item_of_two_stores_apart()
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 1m, 100m),
            Line("INV-2", "1", ReportingTransactionType.Sale, 2m, 300m, store: "S2")
        };

        var rows = new SalesReportingService().Summarize(lines, SalesSummaryDimension.Item, SalesPolicy).Rows;

        Assert.Equal([("S1 / ITEM-1", 100m), ("S2 / ITEM-1", 300m)], rows.Select(x => (x.Key, x.SourceSignedNetAmount)));
    }

    // RA-SALES-12 / RA-UI-15 (1.9.9): the status message is plain words with the period's document counts, no policy jargon.
    [Theory]
    [InlineData(SalesSummaryDimension.Daily)]
    [InlineData(SalesSummaryDimension.Store)]
    [InlineData(SalesSummaryDimension.Brand)]
    [InlineData(SalesSummaryDimension.BrandSegment)]
    [InlineData(SalesSummaryDimension.Item)]
    public void Sales_status_message_gives_plain_counts(SalesSummaryDimension dimension)
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 1m, 100m),
            Line("INV-1", "2", ReportingTransactionType.Sale, 1m, 50m, brand: "Brand B", row: "Brand B"),
            Line("INV-2", "1", ReportingTransactionType.Sale, 1m, 80m),
            Line("SR-1", "1", ReportingTransactionType.Return, -1m, -100m)
        };

        var message = new SalesReportingService().Summarize(lines, dimension, SalesPolicy).Message;

        Assert.StartsWith("2 invoice(s), 1 return(s); returns and bill cancellations are already taken off the totals.", message);
        Assert.DoesNotContain("source-signed", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transformation", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Returns_status_message_counts_return_documents()
    {
        var lines = new[]
        {
            Line("INV-1", "1", ReportingTransactionType.Sale, 1m, 100m),
            Line("SR-1", "1", ReportingTransactionType.Return, -1m, -60m),
            Line("SR-1", "2", ReportingTransactionType.Return, -1m, -40m),
            Line("SR-2", "1", ReportingTransactionType.Return, -1m, -10m, store: "S2")
        };

        var message = new SalesReportingService().Summarize(lines, SalesSummaryDimension.Returns, SalesPolicy).Message;

        Assert.Equal("2 return document(s); return values keep their minus sign.", message);
    }

    [Fact]
    public void Sales_status_message_does_not_claim_returns_are_netted_when_the_policy_leaves_them_out()
    {
        var salesOnly = new ApprovedSalesReportingPolicy("approved-v1", new HashSet<ReportingTransactionType> { ReportingTransactionType.Sale });
        var lines = new[] { Line("INV-1", "1", ReportingTransactionType.Sale, 1m, 100m) };

        Assert.Equal("1 invoice(s), 0 return(s).", new SalesReportingService().Summarize(lines, SalesSummaryDimension.Store, salesOnly).Message);
    }

    private static SalesReportingLine Line(string invoice, string line, ReportingTransactionType type,
        decimal quantity, decimal amount, string brand = "Brand A", string segment = "Premium", string? row = "Brand A", string store = "S1") =>
        new(new DateOnly(2026, 7, 1), store, invoice, line, brand, segment, "ITEM-1", type, quantity, amount, BrandRow: row);
}
