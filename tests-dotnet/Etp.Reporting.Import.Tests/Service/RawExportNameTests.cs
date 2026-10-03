using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>Lane L9: dating a raw Service export from the window in its file name (end date = snapshot date).</summary>
public sealed class RawExportNameTests
{
    [Theory]
    [InlineData("JOB REPORT 30.09.2026 TO 03.10.2026.csv")]
    [InlineData("R R RWR 30.09.2026 TO 03.10.2026 .csv")]
    [InlineData("REPAIR REPORT 30.09.2026 TO 03.10.2026 .csv")]
    [InlineData("PURCHASE REGISTER  INNVOICED RECCCIVED DATE 30.09.2026 TO 03.10.2026.csv")]
    [InlineData("EMPOWERMENT REPORT 30.09.2026 TO 03.10.2026.xlsx")]
    [InlineData("JOB REPORT 30.09.2026 to 03.10.2026.csv")]
    [InlineData("JOB REPORT 30.09.2026TO03.10.2026.csv")]
    [InlineData("JOB REPORT 30.09.2026   TO   03.10.2026.CSV")]
    [InlineData(@"C:\Raw pack\JOB REPORT 30.09.2026 TO 03.10.2026.csv")]
    [InlineData(" JOB REPORT 30.09.2026 TO 03.10.2026.csv ")]
    public void A_window_gives_its_end_date_and_keeps_its_start(string name)
    {
        var time = ExportNameParser.Parse(name);
        var window = ExportNameParser.ParseWindow(name);

        Assert.Equal(ExportBasis.Date, time.Basis);
        Assert.Equal(new DateOnly(2026, 10, 3), time.ExportDate);
        Assert.Equal(new ExportWindow(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 3)), window);
    }

    [Fact]
    public void A_month_long_window_gives_its_end_date()
    {
        Assert.Equal(new DateOnly(2026, 10, 3), ExportNameParser.Parse("TATA REPORT 03.09.2026 TO 03.10.2026 .xlsx").ExportDate);
        Assert.Equal(new DateOnly(2026, 9, 3), ExportNameParser.ParseWindow("TATA REPORT 03.09.2026 TO 03.10.2026 .xlsx")!.Value.From);
    }

    [Theory]
    [InlineData("PENDING REPORT 03.10.2026.csv", 2026, 10, 3)]
    [InlineData("CLOSING STOCK 03.10.2026.csv", 2026, 10, 3)]
    [InlineData("PENDING DELIVERY 09.10.2026 .csv", 2026, 10, 9)]
    public void A_single_date_gives_that_date_and_no_window(string name, int year, int month, int day)
    {
        Assert.Equal(ExportBasis.Date, ExportNameParser.Parse(name).Basis);
        Assert.Equal(new DateOnly(year, month, day), ExportNameParser.Parse(name).ExportDate);
        Assert.Null(ExportNameParser.ParseWindow(name));
    }

    [Fact]
    public void The_service_timestamp_suffix_still_gives_the_second()
    {
        var time = ExportNameParser.Parse("EmpowermentReport_20261003144656.xlsx");

        Assert.Equal(ExportBasis.Second, time.Basis);
        Assert.Equal(new DateTime(2026, 10, 3, 14, 46, 56), time.Instant);
        Assert.Null(ExportNameParser.ParseWindow("EmpowermentReport_20261003144656.xlsx"));
    }

    [Theory]
    // An end before the start, or a date that is not real, is no window; the first real dotted date then dates the file as before.
    [InlineData("JOB REPORT 03.10.2026 TO 30.09.2026.csv", 2026, 10, 3)]
    [InlineData("JOB REPORT 30.09.2026 TO 31.09.2026.csv", 2026, 9, 30)]
    public void A_window_that_is_not_real_falls_back_to_the_first_date(string name, int year, int month, int day)
    {
        Assert.Null(ExportNameParser.ParseWindow(name));
        Assert.Equal(new DateOnly(year, month, day), ExportNameParser.Parse(name).ExportDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("JOB REPORT.csv")]
    [InlineData("JOB REPORT 30.09.2026 - 03.10.2026.csv")]
    public void No_window_without_TO_between_two_dates(string? name)
    {
        Assert.Null(ExportNameParser.ParseWindow(name));
    }

    /// <summary>What v1.9.3 gave for Retail and consolidated names; the range rule must not change any of them.</summary>
    [Theory]
    [InlineData("202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx", "2026-09-29T14:49 (Minute)")]
    [InlineData(@"V:\ETP\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026\202608252051_SDB-VariantwiseSales.xlsx", "2026-08-25T20:51 (Minute)")]
    [InlineData("202607021507_BinWise Stock - PENDING 01.09.2026.xlsx", "2026-07-02T15:07 (Minute)")]
    [InlineData("202609291449_PENDING 01.07.2026 TO 25.08.2026.xlsx", "2026-09-29T14:49 (Minute)")]
    [InlineData("R025_SDB_VariantwiseSales.xlsx", "unknown")]
    [InlineData("R010_BinWise_Stock.xlsx", "unknown")]
    [InlineData("S009_PendingRepair.xlsx", "unknown")]
    [InlineData("ClosingStock_20260901133606.csv", "2026-09-01T13:36:06 (Second)")]
    [InlineData("PENDING REPAIR 29.09.2026.csv", "2026-09-29 (Date)")]
    [InlineData("29.09.2026 PENDING REPAIR.xlsx", "2026-09-29 (Date)")]
    [InlineData("PENDING 01.09.2026_20260929144901.csv", "2026-09-29T14:49:01 (Second)")]
    public void Retail_and_older_names_give_what_they_gave_at_1_9_3(string name, string expected)
    {
        Assert.Equal(expected, ExportNameParser.Parse(name).ToString());
    }

    [Fact]
    public void An_undated_family_takes_the_window_end_before_the_folder_date()
    {
        // Tier 4 (the export name) comes before tier 6 (the folder), so a raw export kept in a dated folder is dated by its window.
        var sheet = new WorkbookSheet("PENDING REPORT", 1, ["A"], [new WorkbookRow(2, [new WorkbookCell("x", "x")])]);
        var workbook = new WorkbookSnapshot("R R DELIVERD 06.10.2026 TO 09.10.2026.csv", 1, new string('a', 64), [sheet],
            Path.Combine(Path.GetTempPath(), "Service Centre till 05 oct 2026", "R R DELIVERD 06.10.2026 TO 09.10.2026.csv"));

        var dating = new SnapshotDateResolver().Resolve(workbook, sheet);

        Assert.False(dating.HasBlockers);
        var block = Assert.Single(dating.Blocks);
        Assert.Equal(new DateOnly(2026, 10, 9), block.SnapshotDate);
        Assert.Equal(SnapshotDateBasis.ExportName, block.Basis);
        Assert.DoesNotContain(dating.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.SnapshotDateFromFolder);
    }
}
