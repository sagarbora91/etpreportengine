namespace Etp.Reporting.Domain.Periods;

public enum ReportingPeriodKind { Ftd, Mtd, Ytd }

public sealed record BusinessReportingPeriod(
    ReportingPeriodKind Kind,
    DateOnly BusinessDate,
    DateRange Current,
    DateRange LastYear,
    string PolicyVersion);

public sealed class IndianFinancialYearPeriodPolicy
{
    public const string Version = "INDIA_FY_APRIL_EQUIVALENT_DATE_V1";

    public BusinessReportingPeriod Resolve(DateOnly businessDate, ReportingPeriodKind kind)
    {
        var current = kind switch
        {
            ReportingPeriodKind.Ftd => new DateRange(businessDate, businessDate),
            ReportingPeriodKind.Mtd => new DateRange(new DateOnly(businessDate.Year, businessDate.Month, 1), businessDate),
            ReportingPeriodKind.Ytd => new DateRange(FinancialYearStart(businessDate), businessDate),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return new(kind, businessDate, current,
            new DateRange(current.Start.AddYears(-1), current.End.AddYears(-1)), Version);
    }

    public static DateOnly FinancialYearStart(DateOnly businessDate) =>
        new(businessDate.Month >= 4 ? businessDate.Year : businessDate.Year - 1, 4, 1);
}
