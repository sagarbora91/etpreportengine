using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// The closing-stock query (OperationalReportRepository.StockInventorySql). These tests read the shipped query;
/// SlowStockAgeingSqlTests and StockVarianceSqlTests in the SQL integration suite run it.
/// </summary>
public sealed class StockInventorySqlTests
{
    [Fact]
    public void Receipt_date_comes_from_the_ledger_inward_types_on_or_before_the_snapshot_read()
    {
        // Owner answer Q8: the same receipt types as StockAgeing.ReceiptTypes, inward quantities only.
        var sql = OperationalReportRepository.StockInventorySql;
        Assert.Contains("MAX(m.document_date) last_receipt_date", sql, StringComparison.Ordinal);
        Assert.Contains("m.document_date<=d.snapshot_date AND m.transaction_quantity>0", sql, StringComparison.Ordinal);
        Assert.Contains("IN(" + string.Join(",", StockAgeing.ReceiptTypes.Select(type => $"N'{type}'")) + ")", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN receipt r ON r.store_code=s.store_code AND r.product_code=s.product_code", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_store_reads_its_latest_snapshot_on_or_before_the_date_only_when_asked()
    {
        // RA-STOCK-01 (1.9.9): @latest=1 takes each store's latest effective snapshot on or before @date; @latest=0 only @date.
        var sql = OperationalReportRepository.StockInventorySql;
        Assert.Contains("SELECT e.store_code,MAX(e.snapshot_date) snapshot_date", sql, StringComparison.Ordinal);
        Assert.Contains("FROM dbo.v_stock_snapshots_effective e", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE e.snapshot_date<=@date AND (@latest=1 OR e.snapshot_date=@date)", sql, StringComparison.Ordinal);
        Assert.Contains("JOIN snap d ON d.store_code=s.store_code AND d.snapshot_date=s.snapshot_date", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("s.snapshot_date=@date", sql, StringComparison.Ordinal);
        // The snapshot day is chosen per store before the segment / item filters, so a filter never moves it.
        var snap = sql[..sql.IndexOf("receipt AS", StringComparison.Ordinal)];
        Assert.DoesNotContain("@segments", snap, StringComparison.Ordinal);
        Assert.DoesNotContain("@items", snap, StringComparison.Ordinal);
        // Last sale is aged to the snapshot read, not the requested date.
        Assert.Contains("i.transaction_date<=s.snapshot_date", sql, StringComparison.Ordinal);
    }
}
