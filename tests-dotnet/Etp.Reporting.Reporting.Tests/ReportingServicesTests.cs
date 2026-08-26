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
    [InlineData(SalesSummaryDimension.Item, "ITEM-1")]
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
        Assert.Equal(2, row.DistinctInvoices);
    }

    [Fact]
    public void Returns_summary_includes_only_classified_returns()
    {
        var result = new SalesReportingService().Summarize(
            [Line("INV-1", "1", ReportingTransactionType.Sale, 2m, 200m),
             Line("INV-2", "1", ReportingTransactionType.Return, -1m, -75m)],
            SalesSummaryDimension.Returns, SalesPolicy);

        var row = Assert.Single(result.Rows);
        Assert.Equal(-1m, row.SourceSignedQuantity);
        Assert.Equal(-75m, row.SourceSignedNetAmount);
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

    private static SalesReportingLine Line(string invoice, string line, ReportingTransactionType type,
        decimal quantity, decimal amount) => new(new DateOnly(2026, 7, 1), "S1", invoice, line,
        "Brand A", "Premium", "ITEM-1", type, quantity, amount);
}
