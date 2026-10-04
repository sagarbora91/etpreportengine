using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// Owner answer Q3/Q5 (decision 14): the owner's hybrid stock rows and the saved-count legacy rule, without SQL.
/// HybridStockRowsSqlTests in the SQL integration suite runs the same through the database.
/// </summary>
public sealed class StockGroupingTests
{
    private static StockInventoryReportRow Row(string product, string? brand, string? brandRow) =>
        new(new(2030, 7, 10), "HYB", product, brand, "CLUSTER", 1m, null, null, null, null, "ACTIVE", BrandRow: brandRow);

    private static readonly StockInventoryReportRow[] System =
    [
        Row("H-GS", "HELIOS", "G SHOCK"),
        Row("H-CL", "HELIOS", "CITIZEN"),
        Row("H-PL", "HELIOS", null),
        Row("S-1", "SEIKO", "SEIKO"),
        Row("K-1", "KENNETH COLE", null),
        Row("U-1", null, null)
    ];

    [Fact]
    public void Stock_group_is_the_brand_row_else_the_brand_else_unmapped()
    {
        Assert.Equal(["G SHOCK", "CITIZEN", "HELIOS", "SEIKO", "KENNETH COLE", "Unmapped"], System.Select(x => x.StockGroup));
    }

    [Fact]
    public void Only_brands_mapped_to_a_row_of_another_name_are_split()
    {
        Assert.Equal(["HELIOS"], StockGrouping.SplitBrands(System));
    }

    [Fact]
    public void New_layout_when_no_saved_count_uses_a_split_brand()
    {
        var choice = StockGrouping.For(System, ["SEIKO", "G SHOCK"]);
        Assert.False(choice.Legacy);
        Assert.Equal("G SHOCK", choice.Key(System[0]));
    }

    [Fact]
    public void Old_layout_when_a_saved_count_uses_a_split_brand_whatever_its_case()
    {
        var choice = StockGrouping.For(System, ["helios"]);
        Assert.True(choice.Legacy);
        Assert.Equal("HELIOS", choice.Key(System[0]));
        Assert.Equal("Unmapped", choice.Key(System[5]));
    }
}
