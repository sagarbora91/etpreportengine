using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class ReportingQueryRepositoryTests
{
    [Theory]
    [MemberData(nameof(Queries))]
    public void Reporting_queries_are_parameterized_and_store_scoped(string sql)
    {
        Assert.Contains("@dateFrom", sql, StringComparison.Ordinal);
        Assert.Contains("@dateTo", sql, StringComparison.Ordinal);
        Assert.Contains("@storesJson", sql, StringComparison.Ordinal);
        Assert.Contains("OPENJSON(@storesJson)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sales_query_reads_source_values_without_calculating_signs()
    {
        Assert.Contains("source_quantity", SqlReportingQueries.Sales, StringComparison.Ordinal);
        Assert.Contains("source_gross_amount", SqlReportingQueries.Sales, StringComparison.Ordinal);
        Assert.Contains("source_net_amount", SqlReportingQueries.Sales, StringComparison.Ordinal);
        Assert.DoesNotContain("ABS(", SqlReportingQueries.Sales, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("*-1", SqlReportingQueries.Sales, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tender_control_uses_revenue_invoice_controls_and_excludes_quarantined_tenders()
    {
        Assert.Contains("sales_invoice_controls", SqlReportingQueries.InvoiceControls, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reporting_sales_tenders", SqlReportingQueries.Tenders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stock_query_uses_exact_boundary_snapshots_and_source_signed_movements()
    {
        Assert.Contains("snapshot_date=@dateTo", SqlReportingQueries.StockPositions, StringComparison.Ordinal);
        Assert.Contains("opening_quantity", SqlReportingQueries.StockPositions, StringComparison.Ordinal);
        Assert.Contains("SUM(m.transaction_quantity)", SqlReportingQueries.StockMovements, StringComparison.Ordinal);
        Assert.Contains("document_date>=@dateFrom", SqlReportingQueries.StockMovements, StringComparison.Ordinal);
    }

    public static TheoryData<string> Queries() => new()
    {
        SqlReportingQueries.Sales,
        SqlReportingQueries.InvoiceControls,
        SqlReportingQueries.Tenders,
        SqlReportingQueries.StockPositions,
        SqlReportingQueries.StockMovements
    };
}
