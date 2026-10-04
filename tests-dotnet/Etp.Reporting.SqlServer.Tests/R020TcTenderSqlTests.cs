namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// Decision 13 Q3 (lane L3) with the 1.9.4 merge gate note: the inner R020 query is narrowed to the caller's store and
/// date range (performance only, the R020 row still joins its invoice on store and invdate = transaction_date) and reads
/// only current R020 files (not superseded, data-truth version 1). The rule itself, MAX(CHEQUEAMOUNT) per invoice equal
/// to the R022 shortfall, is unchanged. The behaviour is pinned at the elevated gate by EveningReportsSqlTests.
/// </summary>
public sealed class R020TcTenderSqlTests
{
    [Fact]
    public void The_inner_r020_scan_takes_the_callers_date_range_and_store_condition()
    {
        var sql = SqlReportingQueries.R020TcTenders("@from", "@to", "rf.store_code=@store");

        Assert.Contains("AND r.invdate>=@from AND r.invdate<=@to AND (rf.store_code=@store)", sql, StringComparison.Ordinal);
        Assert.Contains("rf.report_code='R020' AND rf.is_superseded=0 AND rf.data_truth_version=1", sql, StringComparison.Ordinal);
        // Unchanged rule: the largest blank-agency cheque amount, taken only when it equals the R022 shortfall.
        Assert.Contains("MAX(r.chequeamount) amount", sql, StringComparison.Ordinal);
        Assert.Contains("NULLIF(LTRIM(RTRIM(r.agencyname)),N'') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE tc.amount=(SELECT SUM(c.source_net_value)", sql, StringComparison.Ordinal);
        Assert.Contains("i.transaction_date=tc.invdate", sql, StringComparison.Ordinal);
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
