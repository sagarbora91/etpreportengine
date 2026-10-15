using Etp.Reporting.Domain.Periods;
using Etp.Reporting.Reporting;
using Facts = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.DsrFacts;
using WalkIns = Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository.WalkInFacts;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>1.12.1 lane F1 (decision 68): the money rules that need no database. Synthetic figures, worked out beside each line.</summary>
public sealed class F1MoneyRulesTests
{
    [Theory]
    // 25 Aug: 31 - 25 = 6 days left; 6,26,138 / 6 = 1,04,356.33... (7 before 1.12.1: 89,448.29).
    [InlineData("2026-08-25", "626138", "104356.33")]
    // 10 Feb 2028 (leap): 29 - 10 = 19 days left; 1,90,000 / 19 = 10,000.
    [InlineData("2028-02-10", "190000", "10000.00")]
    // 28 Feb 2028: one day left; an over-achieved month reads negative: -10,000 / 1 = -10,000.
    [InlineData("2028-02-28", "-10000", "-10000.00")]
    public void Required_daily_sales_divides_by_the_days_left_after_the_business_date(string day, string balance, string expected)
    {
        var value = OperationalReportRepository.RequiredDailySales(decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Math.Round(value!.Value, 2));
    }

    [Fact]
    public void Required_daily_sales_is_blank_on_the_last_day_and_without_a_target()
    {
        Assert.Null(OperationalReportRepository.RequiredDailySales(-10_000m, new DateOnly(2028, 2, 29))); // no day left (divided by 1 before)
        Assert.Null(OperationalReportRepository.RequiredDailySales(5_000m, new DateOnly(2026, 8, 31)));
        Assert.Null(OperationalReportRepository.RequiredDailySales(null, new DateOnly(2026, 8, 25)));
    }
}
