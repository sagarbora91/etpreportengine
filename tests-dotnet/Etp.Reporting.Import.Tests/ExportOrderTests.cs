using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

public sealed class ExportOrderTests
{
    // Every pair of bases (unknown, minute, second, date), each in both directions.
    [Theory]
    // Unknown on either side, whatever the other side holds.
    [InlineData("", "", ExportOrderResult.Unknown)]
    [InlineData("", "2026-09-29T14:49", ExportOrderResult.Unknown)]
    [InlineData("", "2026-09-29T14:49:10", ExportOrderResult.Unknown)]
    [InlineData("", "2026-09-29", ExportOrderResult.Unknown)]
    [InlineData("2026-09-29T14:49", "", ExportOrderResult.Unknown)]
    [InlineData("2026-09-29T14:49:10", "", ExportOrderResult.Unknown)]
    [InlineData("2026-09-29", "", ExportOrderResult.Unknown)]
    // Minute and minute: the minutes decide; an equal minute is the same export time.
    [InlineData("2026-09-29T14:49", "2026-09-29T14:48", ExportOrderResult.Newer)]
    [InlineData("2026-09-06T21:07", "2026-09-29T14:49", ExportOrderResult.Older)]
    [InlineData("2026-09-29T14:49", "2026-09-29T14:49", ExportOrderResult.Same)]
    // Second and second: the seconds decide.
    [InlineData("2026-09-01T13:36:07", "2026-09-01T13:36:06", ExportOrderResult.Newer)]
    [InlineData("2026-09-01T13:36:06", "2026-09-01T13:36:07", ExportOrderResult.Older)]
    [InlineData("2026-09-01T13:36:06", "2026-09-01T13:36:06", ExportOrderResult.Same)]
    // Minute and second: only the minute is comparable.
    [InlineData("2026-09-01T13:37", "2026-09-01T13:36:59", ExportOrderResult.Newer)]
    [InlineData("2026-09-01T13:36:59", "2026-09-01T13:37", ExportOrderResult.Older)]
    [InlineData("2026-09-01T13:36", "2026-09-01T13:36:45", ExportOrderResult.Same)]
    [InlineData("2026-09-01T13:36:45", "2026-09-01T13:36", ExportOrderResult.Same)]
    // Date and date: the dates decide; an equal date orders nothing.
    [InlineData("2026-09-29", "2026-08-07", ExportOrderResult.Newer)]
    [InlineData("2026-08-07", "2026-09-29", ExportOrderResult.Older)]
    [InlineData("2026-09-29", "2026-09-29", ExportOrderResult.Unknown)]
    // Date and minute: the dates decide; the same day orders nothing.
    [InlineData("2026-09-30", "2026-09-29T23:59", ExportOrderResult.Newer)]
    [InlineData("2026-09-29T00:01", "2026-09-30", ExportOrderResult.Older)]
    [InlineData("2026-09-29", "2026-09-29T14:49", ExportOrderResult.Unknown)]
    [InlineData("2026-09-29T14:49", "2026-09-29", ExportOrderResult.Unknown)]
    // Date and second: likewise.
    [InlineData("2026-09-02", "2026-09-01T13:36:06", ExportOrderResult.Newer)]
    [InlineData("2026-09-01T13:36:06", "2026-09-02", ExportOrderResult.Older)]
    [InlineData("2026-09-01", "2026-09-01T13:36:06", ExportOrderResult.Unknown)]
    [InlineData("2026-09-01T13:36:06", "2026-09-01", ExportOrderResult.Unknown)]
    public void Compare_orders_by_export_time_and_basis(string a, string b, ExportOrderResult expected)
    {
        Assert.Equal(expected, ExportOrder.Compare(Time(a), Time(b)));
        Assert.Equal(expected == ExportOrderResult.Newer, ExportOrder.IsNewer(Time(a), Time(b)));
    }

    [Fact]
    public void Compare_is_antisymmetric_over_every_basis()
    {
        string[] samples =
        [
            "", "2026-09-29T14:49", "2026-09-29T14:48", "2026-09-29T14:49:10", "2026-09-29T14:49:50", "2026-09-29",
            "2026-09-28", "2026-09-30T00:00", "2026-08-07T18:58", "2026-08-07"
        ];
        foreach (var a in samples)
        foreach (var b in samples)
        {
            var forward = ExportOrder.Compare(Time(a), Time(b));
            var backward = ExportOrder.Compare(Time(b), Time(a));
            Assert.Equal(Inverse(forward), backward);
        }
    }

    [Fact]
    public void Export_names_order_like_their_times()
    {
        var earlier = ExportNameParser.Parse("202608252051_SDB-VariantwiseSales.xlsx");
        var later = ExportNameParser.Parse("202609291449_SDB-VariantwiseSales.xlsx");
        var renamed = ExportNameParser.Parse("R025_SDB_VariantwiseSales.xlsx");

        Assert.Equal(ExportOrderResult.Newer, ExportOrder.Compare(later, earlier));
        Assert.Equal(ExportOrderResult.Older, ExportOrder.Compare(earlier, later));
        Assert.Equal(ExportOrderResult.Unknown, ExportOrder.Compare(later, renamed));
    }

    private static ExportTime Time(string text) =>
        text.Length == 0 ? ExportTime.Unknown
            : ExportTime.TryParseContract(text, out var time) ? time : throw new ArgumentException(text);

    private static ExportOrderResult Inverse(ExportOrderResult result) => result switch
    {
        ExportOrderResult.Newer => ExportOrderResult.Older,
        ExportOrderResult.Older => ExportOrderResult.Newer,
        _ => result
    };
}
