using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// Owner answer Q2 (decision 13), Helios report audit R-10: gift cards are left out of stock. These tests read the shipped
/// queries; StockVarianceSqlTests in the SQL integration suite runs them.
/// </summary>
public sealed class NonMerchandiseSqlTests
{
    [Fact]
    public void Every_stock_query_leaves_gift_cards_out()
    {
        Assert.Contains("AND NOT " + NonMerchandiseSql.StockItemOfS, OperationalReportRepository.StockInventorySql, StringComparison.Ordinal);
        Assert.Contains("AND NOT " + NonMerchandiseSql.LedgerItemOfM, SqlReportingQueries.StockPositions, StringComparison.Ordinal);
        Assert.Contains("AND NOT " + NonMerchandiseSql.LedgerItemOfM, SqlReportingQueries.StockLedgerRows, StringComparison.Ordinal);
        Assert.Contains("AND NOT " + NonMerchandiseSql.LedgerItemOfM, SqlReportingQueries.StockMovements, StringComparison.Ordinal);
    }

    [Fact]
    public void Predicates_never_turn_null_on_a_blank_brand()
    {
        // NOT (NULL = 'GC') is NULL in SQL and would drop the row; every column is wrapped in COALESCE.
        foreach (var predicate in new[] { NonMerchandiseSql.SalesLineOfL, NonMerchandiseSql.StockItemOfS, NonMerchandiseSql.LedgerItemOfM })
        {
            Assert.StartsWith("(", predicate, StringComparison.Ordinal);
            Assert.EndsWith(")", predicate, StringComparison.Ordinal);
            Assert.Equal(predicate.Split("COALESCE(").Length - 1, predicate.Split(".").Length - 1);
        }
        Assert.Contains("N'GIFT CARD'", NonMerchandiseSql.SalesLineOfL, StringComparison.Ordinal);
        Assert.Contains("l.source_brand_code,N''))))=N'GC'", NonMerchandiseSql.SalesLineOfL, StringComparison.Ordinal);
        Assert.Contains("s.brand_code,N''))))=N'GC'", NonMerchandiseSql.StockItemOfS, StringComparison.Ordinal);
    }

    [Fact]
    public void Predicates_take_any_alias_and_refuse_injection()
    {
        Assert.Equal(NonMerchandiseSql.SalesLineOfL.Replace("l.", "x.", StringComparison.Ordinal), NonMerchandiseSql.SalesLine("x"));
        Assert.Equal(NonMerchandiseSql.StockItemOfS.Replace("s.", "", StringComparison.Ordinal), NonMerchandiseSql.StockItem(""));
        Assert.Contains("mv.product_code", NonMerchandiseSql.LedgerItem("mv"), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => NonMerchandiseSql.StockItem("s;DROP"));
    }
}
