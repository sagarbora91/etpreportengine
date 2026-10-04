namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// Decision 13 Q3 (lane L3) with the 1.9.4 merge gate note: the inner R020 query is narrowed to the caller's store and
/// date range (performance only, the R020 row still joins its invoice on store and invdate = transaction_date) and reads
/// only current R020 files (not superseded, data-truth version 1). Decision 21 (1.9.5): all blank-agency cheque rows of an
/// invoice are added up and fill the R022 shortfall when the total equals it exactly, or else when one row equals it exactly;
/// otherwise nothing is filled. The behaviour is pinned at the elevated gate by EveningReportsSqlTests.
/// </summary>
public sealed class R020TcTenderSqlTests
{
    [Fact]
    public void The_inner_r020_scan_takes_the_callers_date_range_and_store_condition()
    {
        var sql = SqlReportingQueries.R020TcTenders("@from", "@to", "rf.store_code=@store");

        Assert.Contains("AND r.invdate>=@from AND r.invdate<=@to AND (rf.store_code=@store)", sql, StringComparison.Ordinal);
        Assert.Contains("rf.report_code='R020' AND rf.is_superseded=0 AND rf.data_truth_version=1", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(LTRIM(RTRIM(r.agencyname)),N'') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("i.transaction_date=tc.invdate", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_cheque_rule_sums_the_rows_or_takes_one_exact_row_and_fills_only_the_shortfall()
    {
        var sql = SqlReportingQueries.R020TcTenders("@from", "@to", "1=1");

        // Each blank-agency row is read on its own (no MAX or pre-aggregation), then grouped per invoice.
        Assert.Contains(",r.invdate,r.chequeamount amount", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("MAX(r.chequeamount)", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY i.sales_invoice_id,s.shortfall", sql, StringComparison.Ordinal);
        // Shortfall = R022 NetValue less the invoice's reporting tenders.
        Assert.Contains("SELECT (SELECT SUM(c.source_net_value) FROM dbo.sales_invoice_controls c WHERE c.sales_invoice_id=i.sales_invoice_id)", sql, StringComparison.Ordinal);
        Assert.Contains("-COALESCE((SELECT SUM(x.source_amount) FROM dbo.reporting_sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id),0) shortfall", sql, StringComparison.Ordinal);
        // 100 + 29 vs 129 fills 129 (total exact); 129 among 50 and 70 fills 129 (one exact row); 100 + 50 vs 129 fills nothing.
        Assert.Contains("HAVING SUM(tc.amount)=s.shortfall OR MAX(CASE WHEN tc.amount=s.shortfall THEN 1 ELSE 0 END)=1", sql, StringComparison.Ordinal);
        // The amount filled is the shortfall itself, one row per invoice: never more than R022 lacks, never twice.
        Assert.StartsWith("SELECT i.sales_invoice_id,CONVERT(nvarchar(80),N'TC_R020') tender_type,s.shortfall source_amount", sql.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("WHERE s.shortfall<>0", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_tenders_wrap_the_filtered_r020_query_and_the_report_query_uses_its_own_scope()
    {
        var effective = SqlReportingQueries.EffectiveTenders("@date", "@date", "rf.store_code=@store");
        Assert.StartsWith("SELECT sales_tender_id,sales_invoice_id,tender_type,source_amount FROM dbo.reporting_sales_tenders", effective.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("AND r.invdate>=@date AND r.invdate<=@date AND (rf.store_code=@store)", effective, StringComparison.Ordinal);

        Assert.Contains("AND r.invdate>=@dateFrom AND r.invdate<=@dateTo AND (@storesJson IS NULL OR rf.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))",
            SqlReportingQueries.Tenders, StringComparison.Ordinal);
    }
}
