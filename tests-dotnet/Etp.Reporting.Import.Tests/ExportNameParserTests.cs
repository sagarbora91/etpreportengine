using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

public sealed class ExportNameParserTests
{
    [Theory]
    [InlineData("202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", 2026, 9, 29, 14, 49)]
    [InlineData(@"V:\ETP\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\202608252051_SDB-VariantwiseSales.xlsx", 2026, 8, 25, 20, 51)]
    [InlineData("202607021507_BinWise Stock - PENDING 01.09.2026.xlsx", 2026, 7, 2, 15, 7)]
    public void Retail_prefix_gives_the_minute(string name, int year, int month, int day, int hour, int minute)
    {
        var time = ExportNameParser.Parse(name);

        Assert.Equal(ExportBasis.Minute, time.Basis);
        Assert.Equal(new DateTime(year, month, day, hour, minute, 0), time.Instant);
        Assert.Equal(DateTimeKind.Unspecified, time.Instant!.Value.Kind);
    }

    [Theory]
    [InlineData("ClosingStock_20260901133606.csv")]
    [InlineData("ClosingStock_20260901133606.XLSX")]
    [InlineData(@"C:\Service pack\ClosingStock_20260901133606.xlsx")]
    public void Service_suffix_gives_the_second(string name)
    {
        var time = ExportNameParser.Parse(name);

        Assert.Equal(ExportBasis.Second, time.Basis);
        Assert.Equal(new DateTime(2026, 9, 1, 13, 36, 6), time.Instant);
    }

    [Theory]
    [InlineData("PENDING REPAIR 29.09.2026.csv")]
    [InlineData("29.09.2026 PENDING REPAIR.xlsx")]
    public void Dotted_date_gives_the_date_only(string name)
    {
        var time = ExportNameParser.Parse(name);

        Assert.Equal(ExportBasis.Date, time.Basis);
        Assert.Equal(new DateOnly(2026, 9, 29), time.ExportDate);
    }

    [Theory]
    [InlineData("R025_SDB_VariantwiseSales.xlsx")]
    [InlineData(@"V:\ETP\till 29 sep 2026\R010_BinWise_Stock.xlsx")]
    [InlineData(@"V:\ETP\202609291449_pack\R010_BinWise_Stock.xlsx")]
    [InlineData("2026092914491_SDB.xlsx")]
    [InlineData("SDB_202609291449.xlsx")]
    [InlineData("202613291449_month-thirteen.xlsx")]
    [InlineData("ClosingStock_20260931133606.csv")]
    [InlineData("ClosingStock_20260901133606.xls")]
    [InlineData("PENDING 31.02.2026.csv")]
    [InlineData("PENDING 129.09.20261.csv")]
    [InlineData("100000068000_invoice-number-shaped.xlsx")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_else_is_unknown_and_never_estimated(string? name)
    {
        var time = ExportNameParser.Parse(name);

        Assert.Equal(ExportBasis.Unknown, time.Basis);
        Assert.False(time.IsKnown);
        Assert.Null(time.Instant);
        Assert.Equal(ExportTime.Unknown, time);
    }

    [Fact]
    public void Retail_prefix_wins_over_a_date_in_the_rest_of_the_name()
    {
        Assert.Equal(ExportBasis.Minute, ExportNameParser.Parse("202609291449_PENDING REPAIR 01.09.2026.csv").Basis);
        Assert.Equal(ExportBasis.Second, ExportNameParser.Parse("PENDING 01.09.2026_20260929144901.csv").Basis);
    }

    [Theory]
    [InlineData("2026-09-29T14:49", ExportBasis.Minute)]
    [InlineData("2026-09-01T13:36:06", ExportBasis.Second)]
    [InlineData("2026-09-29", ExportBasis.Date)]
    public void Contract_export_time_round_trips(string text, ExportBasis basis)
    {
        Assert.True(ExportTime.TryParseContract(text, out var time));
        Assert.Equal(basis, time.Basis);
        Assert.Equal(text, time.ToContractText());
        Assert.Equal(time, ExportTime.FromStored(time.Instant, time.Basis));
    }

    [Theory]
    [InlineData("")]
    [InlineData("29-09-2026")]
    [InlineData("2026-09-29 14:49")]
    [InlineData("2026-09-29T14:49+05:30")]
    [InlineData("2026-02-30")]
    [InlineData(null)]
    public void Contract_export_time_accepts_only_the_three_formats(string? text)
    {
        Assert.False(ExportTime.TryParseContract(text, out var time));
        Assert.Equal(ExportTime.Unknown, time);
    }
}
