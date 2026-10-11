using System.Globalization;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Decision 32 (RA-TENDER-16): the last-year window of the Staff report. A range compares with the same range a year
/// earlier. A single-day run would compare with one day, which is often blank, so it shows last year's month to date
/// (the first of that month up to the same calendar day) and says so.
/// </summary>
public readonly record struct StaffLastYearWindow(DateOnly From, DateOnly To, bool IsMonthToDate)
{
    public static StaffLastYearWindow For(DateOnly dateFrom, DateOnly dateTo)
    {
        var to = dateTo.AddYears(-1);
        return dateFrom == dateTo
            ? new(new DateOnly(to.Year, to.Month, 1), to, true)
            : new(dateFrom.AddYears(-1), to, false);
    }

    /// <summary>Text appended to the report message; empty for a range.</summary>
    public static string Note(StaffLastYearWindow window) => window.IsMonthToDate
        ? string.Create(CultureInfo.InvariantCulture,
            $" Single-day run: LY Sales is last year's month to date ({window.From:dd MMM yyyy} to {window.To:dd MMM yyyy}), not the same day; Growth % is not shown.")
        : string.Empty;
}
