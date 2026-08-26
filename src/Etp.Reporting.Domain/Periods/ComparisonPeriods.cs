namespace Etp.Reporting.Domain.Periods;

public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new ArgumentException("End date cannot precede start date.", nameof(end));
        Start = start;
        End = end;
    }

    public DateOnly Start { get; }
    public DateOnly End { get; }
    public int InclusiveDayCount => End.DayNumber - Start.DayNumber + 1;
}

public sealed record ComparisonPeriod(DateRange Current, DateRange Comparison, string PolicyVersion);

public interface IComparisonPeriodPolicy
{
    string Version { get; }
    ComparisonPeriod Resolve(DateRange currentPeriod);
}
