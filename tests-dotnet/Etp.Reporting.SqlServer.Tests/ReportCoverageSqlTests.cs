using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// 1.9.7 report audit (RA-TENDER-02, RA-STOCK-03, RA-SALES-01): a report must name a store whose source is not imported
/// instead of dropping it. The SQL shape is pinned here; the behaviour on a database is pinned by the integration suite.
/// </summary>
public sealed class ReportCoverageSqlTests
{
    [Fact]
    public void Tender_gap_invoices_are_valued_from_the_sales_lines_of_the_uncovered_store_days()
    {
        var sql = SqlReportingQueries.TenderGapInvoices;

        // The same gap days as the coverage check: sales from a current file, no current R022 covering the day.
        Assert.Contains("JOIN dbo.import_files sf ON sf.import_file_id=sl.import_file_id AND sf.is_superseded=0", sql, StringComparison.Ordinal);
        Assert.Contains("f.report_code='R022'", sql, StringComparison.Ordinal);
        Assert.Contains("AND f.is_superseded=0 AND f.data_truth_version=1", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN dbo.sales_invoices i ON i.store_code=d.store_code AND i.transaction_date=d.transaction_date", sql, StringComparison.Ordinal);
        // GST-inclusive line value (D1), one row per document and financial year.
        Assert.Contains("SELECT i.store_code,i.document_number,i.invoice_year,SUM(l.source_gross_amount)", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY i.store_code,i.document_number,i.invoice_year", sql, StringComparison.Ordinal);
        Assert.StartsWith("WITH sales_days AS", SqlReportingQueries.TenderCoverageGaps.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("FROM gap_days d", SqlReportingQueries.TenderCoverageGaps, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_ledger_coverage_is_seeded_from_the_requested_or_active_stores()
    {
        var sql = SqlReportingQueries.StockLedgerCoverage;

        Assert.Contains("SELECT CONVERT(varchar(30),[value]) store_code FROM OPENJSON(COALESCE(@storesJson,'[]'))", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE @storesJson IS NULL AND s.is_active=1 AND COALESCE(u.business_unit_code,'RETAIL')<>'SERVICE'", sql, StringComparison.Ordinal);
        // The stores with rows are still included, so a store with movements but outside the active list is not lost.
        Assert.Contains("SELECT m.store_code FROM dbo.stock_movements m", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT s.store_code FROM dbo.v_stock_snapshots_effective s", sql, StringComparison.Ordinal);
    }

    private static readonly string[] Stores = ["WLMHW", "HEMW"];

    [Fact]
    public void Dsr_period_outside_the_r025_coverage_stays_null_and_names_the_last_covered_day()
    {
        var period = new IndianFinancialYearPeriodPolicy().Resolve(new(2026, 10, 8), ReportingPeriodKind.Ftd);
        var facts = OperationalReportRepository.ApplySourceCoverage(new(StringComparer.OrdinalIgnoreCase),
            [("WLMHW", new(2024, 9, 16), new(2026, 9, 29)), ("HEMW", new(2024, 9, 16), new(2026, 8, 25)), ("HEMW", new(2026, 8, 26), new(2026, 9, 28))],
            Stores, period);

        Assert.Null(facts["WLMHW"].TySales);
        Assert.Equal(new DateOnly(2026, 9, 29), facts["WLMHW"].SourceCoversTo);
        Assert.Null(facts["HEMW"].TySales);
        Assert.Equal(new DateOnly(2026, 9, 28), facts["HEMW"].SourceCoversTo);
    }

    [Fact]
    public void Dsr_period_inside_the_r025_coverage_with_no_sales_reads_zero_and_a_store_with_no_file_reads_null()
    {
        var period = new IndianFinancialYearPeriodPolicy().Resolve(new(2026, 9, 29), ReportingPeriodKind.Ftd);
        var facts = OperationalReportRepository.ApplySourceCoverage(new(StringComparer.OrdinalIgnoreCase),
            [("WLMHW", new(2024, 9, 16), new(2026, 9, 29))], Stores, period);

        Assert.Equal(0m, facts["WLMHW"].TySales);
        Assert.Equal(0, facts["WLMHW"].TyInvoices);
        Assert.Null(facts["HEMW"].TySales);
        Assert.Null(facts["HEMW"].SourceCoversTo);
    }

    [Fact]
    public void Dsr_rows_carry_the_last_covered_day_per_store_and_none_for_combined()
    {
        var kind = ReportingPeriodKind.Ftd;
        var rows = OperationalReportRepository.BuildDsrPeriodRows("FTD", Stores, new IndianFinancialYearPeriodPolicy().Resolve(new(2026, 10, 8), kind),
            new Dictionary<string, Facts>(StringComparer.OrdinalIgnoreCase)
            {
                ["WLMHW"] = new(SourceCoversTo: new DateOnly(2026, 9, 29)),
                ["HEMW"] = new(SourceCoversTo: new DateOnly(2026, 9, 28))
            },
            new Dictionary<string, WalkIns>(StringComparer.OrdinalIgnoreCase), new ManagementMetricEngine());

        Assert.Equal([new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 28), null], rows.Select(x => x.SourceCoversTo));
        Assert.All(rows, row => Assert.Null(row.TySales));
        Assert.Equal(new DateOnly(2026, 9, 29), SqlServerApplicationReportQuery.Map(rows[0]).SourceCoversTo);
    }
}
