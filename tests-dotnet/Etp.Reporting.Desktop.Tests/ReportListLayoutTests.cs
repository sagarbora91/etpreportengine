using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportListLayoutTests
{
    [Theory]
    // A 1366x768 screen at 125% leaves roughly 964 DIP for the catalogue once the rail
    // and the workspace inset are taken off. The old rule stepped from three columns to
    // one below 1000 DIP, so this exact width produced a single scrolling column on the
    // acceptance VM, which is what the owner reported.
    [InlineData(964, 2)]
    // The 816x480 touch case from A3.3, again after the rail and the inset.
    [InlineData(688, 2)]
    [InlineData(1400, 3)]
    [InlineData(990, 3)]
    [InlineData(659, 1)]
    [InlineData(0, 1)]
    public void Columns_fit_the_available_width(double width, int expected)
        => Assert.Equal(expected, ReportListView.ColumnsFor(width));

    [Fact]
    public void Columns_never_collapse_or_run_away()
    {
        for (var width = 0d; width < 4000d; width += 7d)
        {
            var columns = ReportListView.ColumnsFor(width);
            Assert.InRange(columns, 1, 3);
            // Never squeeze titles below the readable width by adding a column too early.
            if (columns > 1) Assert.True(width / columns >= ReportListView.MinimumColumnWidth - 1);
        }
    }
}
