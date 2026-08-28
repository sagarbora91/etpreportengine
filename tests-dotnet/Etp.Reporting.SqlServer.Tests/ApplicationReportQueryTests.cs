using Etp.Reporting.Application.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ApplicationReportQueryTests
{
    [Fact]
    public void Scope_mapping_preserves_every_filter_without_interpreting_values()
    {
        string[] stores = ["WLMHW", "HEMW"];
        string[] brands = ["GAUTO"];
        string[] transactions = ["INV", "SR"];
        string[] items = ["SKU-1"];
        var scope = new ReportScope(new(2026, 8, 1), new(2026, 8, 25), stores, brands, transactions, items);

        var mapped = SqlServerApplicationReportQuery.ToScope(scope);

        Assert.Equal(scope.DateFrom, mapped.DateFrom);
        Assert.Equal(scope.DateTo, mapped.DateTo);
        Assert.Same(stores, mapped.StoreCodes);
        Assert.Same(brands, mapped.BrandSegments);
        Assert.Same(transactions, mapped.TransactionTypes);
        Assert.Same(items, mapped.ItemCodes);
    }

    public static TheoryData<ReconciliationStatus, ReportStatus> StatusMappings => new()
    {
        { ReconciliationStatus.NotRun, ReportStatus.NotRun },
        { ReconciliationStatus.Passed, ReportStatus.Passed },
        { ReconciliationStatus.Failed, ReportStatus.Failed },
        { ReconciliationStatus.Blocked, ReportStatus.Blocked },
        { (ReconciliationStatus)999, ReportStatus.NotRun }
    };

    [Theory]
    [MemberData(nameof(StatusMappings))]
    public void Status_mapping_is_central_and_unknown_values_fail_closed(
        ReconciliationStatus source,
        ReportStatus expected) =>
        Assert.Equal(expected, SqlServerApplicationReportQuery.Map(source));

    [Fact]
    public void Controlled_report_mapping_preserves_signed_sales_and_control_metadata()
    {
        var source = new SalesSummaryResult(
            SalesSummaryDimension.BrandSegment,
            ReconciliationStatus.Passed,
            [new SalesSummaryRow("GAUTO", -2m, -12_345.67m, 1)],
            "SALES-V1",
            "Source-signed values.");

        var mapped = SqlServerApplicationReportQuery.Map(source);

        Assert.Equal(ReportSalesDimension.BrandSegment, mapped.Dimension);
        Assert.Equal(ReportStatus.Passed, mapped.Status);
        Assert.Equal("SALES-V1", mapped.PolicyVersion);
        Assert.Equal("Source-signed values.", mapped.Message);
        var row = Assert.Single(mapped.Rows);
        Assert.Equal("GAUTO", row.Key);
        Assert.Equal(-2m, row.SourceSignedQuantity);
        Assert.Equal(-12_345.67m, row.SourceSignedNetAmount);
        Assert.Equal(1, row.DistinctInvoices);
    }

    [Fact]
    public void Tender_and_stock_mapping_preserve_variances_and_per_row_statuses()
    {
        var tender = SqlServerApplicationReportQuery.Map(new InvoiceTenderReconciliation(
            ReconciliationStatus.Failed,
            [new DocumentControlResult("HEMW", "INV-1", 100m, 90m, 10m, ReconciliationStatus.Failed)],
            100m, 90m, 10m, "TENDER-V1", "Variance found."));
        var stock = SqlServerApplicationReportQuery.Map(new StockReconciliationResult(
            ReconciliationStatus.Passed,
            [new StockControlResult("WLMHW", "SKU-1", 10m, -2m, 8m, 8m, 0m, ReconciliationStatus.Passed)],
            "STOCK-V1", "Balanced."));

        Assert.Equal(10m, tender.Variance);
        Assert.Equal(ReportStatus.Failed, Assert.Single(tender.Documents).Status);
        Assert.Equal(-2m, Assert.Single(stock.Items).SourceSignedMovements);
        Assert.Equal(ReportStatus.Passed, stock.Status);
    }

    [Fact]
    public void Operational_mapping_preserves_missing_values_and_source_lineage()
    {
        var date = new DateOnly(2026, 8, 25);
        var dsr = SqlServerApplicationReportQuery.Map(new DsrManagementRow(
            "MTD", "Combined", new(2026, 8, 1), date,
            1_000m, null, null, "LY MTD source required",
            5m, null, 4, null, 1.25m, 250m, null, null, "DSR-POLICY"));
        var lineage = SqlServerApplicationReportQuery.Map(new InvoiceSalesLineageRow(
            date, "WLMHW", "INV-1", "LINE-1", "SKU-1", null, "GAUTO", "SR",
            -1m, null, null, "sales.xlsx", "Data", 42));

        Assert.Null(dsr.LySales);
        Assert.Null(dsr.GrowthPercent);
        Assert.Null(dsr.WalkIns);
        Assert.Equal("LY MTD source required", dsr.GrowthStatus);
        Assert.Equal("DSR-POLICY", dsr.MetricPolicy);
        Assert.Equal(-1m, lineage.Quantity);
        Assert.Null(lineage.NetValue);
        Assert.Equal("sales.xlsx", lineage.SourceWorkbook);
        Assert.Equal("Data", lineage.SourceSheet);
        Assert.Equal(42, lineage.SourceRow);
    }

    [Fact]
    public void Adapter_rejects_an_empty_connection_string() =>
        Assert.Throws<ArgumentException>(() => new SqlServerApplicationReportQuery("   "));
}
