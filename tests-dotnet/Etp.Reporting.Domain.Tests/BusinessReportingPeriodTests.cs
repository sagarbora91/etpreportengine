using Etp.Reporting.Domain.Periods;

namespace Etp.Reporting.Domain.Tests;

public sealed class BusinessReportingPeriodTests
{
    private readonly IndianFinancialYearPeriodPolicy policy = new();

    [Fact]
    public void Ftd_uses_business_date_and_not_import_timestamp()
    {
        var result = policy.Resolve(new DateOnly(2026, 8, 25), ReportingPeriodKind.Ftd);

        Assert.Equal(new DateOnly(2026, 8, 25), result.Current.Start);
        Assert.Equal(new DateOnly(2026, 8, 25), result.Current.End);
        Assert.Equal(new DateOnly(2025, 8, 25), result.LastYear.Start);
    }

    [Fact]
    public void Mtd_compares_equivalent_partial_prior_year_period()
    {
        var result = policy.Resolve(new DateOnly(2026, 8, 25), ReportingPeriodKind.Mtd);

        Assert.Equal(new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 25)), result.Current);
        Assert.Equal(new DateRange(new DateOnly(2025, 8, 1), new DateOnly(2025, 8, 25)), result.LastYear);
    }

    [Theory]
    [InlineData(2026, 4, 1, 2026)]
    [InlineData(2027, 3, 31, 2026)]
    public void Ytd_uses_indian_april_financial_year(int year, int month, int day, int expectedStartYear)
    {
        var result = policy.Resolve(new DateOnly(year, month, day), ReportingPeriodKind.Ytd);

        Assert.Equal(new DateOnly(expectedStartYear, 4, 1), result.Current.Start);
        Assert.Equal(result.Current.Start.AddYears(-1), result.LastYear.Start);
        Assert.Equal(result.Current.End.AddYears(-1), result.LastYear.End);
    }

    [Fact]
    public void Leap_day_last_year_period_is_calendar_safe()
    {
        var result = policy.Resolve(new DateOnly(2028, 2, 29), ReportingPeriodKind.Ftd);

        Assert.Equal(new DateOnly(2027, 2, 28), result.LastYear.Start);
    }
}
