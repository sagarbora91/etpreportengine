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
    public void Receipt_date_comes_from_the_ledger_inward_types_on_or_before_the_date()
    {
        // Owner answer Q8: the same receipt types as StockAgeing.ReceiptTypes, inward quantities only.
        var sql = OperationalReportRepository.StockInventorySql;
        Assert.Contains("MAX(m.document_date) last_receipt_date", sql, StringComparison.Ordinal);
        Assert.Contains("m.document_date<=@date AND m.transaction_quantity>0", sql, StringComparison.Ordinal);
        Assert.Contains("IN(" + string.Join(",", StockAgeing.ReceiptTypes.Select(type => $"N'{type}'")) + ")", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN receipt r ON r.store_code=s.store_code AND r.product_code=s.product_code", sql, StringComparison.Ordinal);
    }
}
