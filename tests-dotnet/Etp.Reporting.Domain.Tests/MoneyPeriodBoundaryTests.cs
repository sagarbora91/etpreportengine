using Etp.Reporting.Domain.Periods;

namespace Etp.Reporting.Domain.Tests;

/// <summary>
/// 1.12.1 lane M2 (money test gap #10): the period boundaries around 29 Feb, 28 Feb of a non-leap year, 1 Mar and 1 Apr.
/// The last-year window is the same calendar date one year earlier (DateOnly.AddYears(-1)), so 29 Feb maps to 28 Feb.
/// </summary>
public sealed class MoneyPeriodBoundaryTests
{
    private readonly IndianFinancialYearPeriodPolicy policy = new();

    [Theory]
    // MTD on 28 Feb 2029 (not a leap year): 1-28 Feb 2029 against 1-28 Feb 2028. 29 Feb 2028 is left out of the comparison.
    [InlineData(2029, 2, 28, ReportingPeriodKind.Mtd, 2029, 2, 1, 2029, 2, 28, 2028, 2, 1, 2028, 2, 28)]
    // MTD on 29 Feb 2028: 1-29 Feb 2028 against 1-28 Feb 2027 (the equivalent date of 29 Feb is 28 Feb).
    [InlineData(2028, 2, 29, ReportingPeriodKind.Mtd, 2028, 2, 1, 2028, 2, 29, 2027, 2, 1, 2027, 2, 28)]
    // YTD on 29 Feb 2028: the financial year began 1 Apr 2027; LY is 1 Apr 2026 to 28 Feb 2027.
    [InlineData(2028, 2, 29, ReportingPeriodKind.Ytd, 2027, 4, 1, 2028, 2, 29, 2026, 4, 1, 2027, 2, 28)]
    // YTD on 1 Apr 2027 is the single day 1 Apr 2027 against the single day 1 Apr 2026.
    [InlineData(2027, 4, 1, ReportingPeriodKind.Ytd, 2027, 4, 1, 2027, 4, 1, 2026, 4, 1, 2026, 4, 1)]
    // MTD on 1 Mar 2028 is 1 Mar only, against 1 Mar 2027 only.
    [InlineData(2028, 3, 1, ReportingPeriodKind.Mtd, 2028, 3, 1, 2028, 3, 1, 2027, 3, 1, 2027, 3, 1)]
    // YTD on 31 Mar 2028 is the last day of the financial year that began 1 Apr 2027.
    [InlineData(2028, 3, 31, ReportingPeriodKind.Ytd, 2027, 4, 1, 2028, 3, 31, 2026, 4, 1, 2027, 3, 31)]
    // FTD on 29 Feb 2028 compares with 28 Feb 2027.
    [InlineData(2028, 2, 29, ReportingPeriodKind.Ftd, 2028, 2, 29, 2028, 2, 29, 2027, 2, 28, 2027, 2, 28)]
    public void Boundary_periods(
        int year, int month, int day, ReportingPeriodKind kind,
        int cy1, int cm1, int cd1, int cy2, int cm2, int cd2,
        int ly1, int lm1, int ld1, int ly2, int lm2, int ld2)
    {
        var result = policy.Resolve(new DateOnly(year, month, day), kind);

        Assert.Equal(new DateRange(new DateOnly(cy1, cm1, cd1), new DateOnly(cy2, cm2, cd2)), result.Current);
        Assert.Equal(new DateRange(new DateOnly(ly1, lm1, ld1), new DateOnly(ly2, lm2, ld2)), result.LastYear);
    }

    [Fact]
    public void Day_counts_of_the_boundary_periods_are_hand_counted()
    {
        // 1-28 Feb 2029 = 28 days; its LY 1-28 Feb 2028 = 28 days (29 Feb 2028 is not in it).
        var mtd = policy.Resolve(new DateOnly(2029, 2, 28), ReportingPeriodKind.Mtd);
        Assert.Equal(28, mtd.Current.InclusiveDayCount);
        Assert.Equal(28, mtd.LastYear.InclusiveDayCount);

        // 1 Apr 2027 to 29 Feb 2028: Apr 30 + May 31 + Jun 30 + Jul 31 + Aug 31 + Sep 30 + Oct 31 + Nov 30 + Dec 31 + Jan 31 + Feb 29 = 335.
        var ytd = policy.Resolve(new DateOnly(2028, 2, 29), ReportingPeriodKind.Ytd);
        Assert.Equal(335, ytd.Current.InclusiveDayCount);
        // 1 Apr 2026 to 28 Feb 2027 is one day shorter: 334.
        Assert.Equal(334, ytd.LastYear.InclusiveDayCount);
    }
}
