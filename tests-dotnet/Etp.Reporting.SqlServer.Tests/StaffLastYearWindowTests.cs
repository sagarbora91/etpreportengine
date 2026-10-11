using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>Decision 32 (RA-TENDER-16): a single-day Staff run shows last year's month to date.</summary>
public sealed class StaffLastYearWindowTests
{
    [Fact]
    public void A_single_day_run_uses_last_years_month_to_date()
    {
        var window = StaffLastYearWindow.For(new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11));
        Assert.True(window.IsMonthToDate);
        Assert.Equal(new DateOnly(2025, 10, 1), window.From);
        Assert.Equal(new DateOnly(2025, 10, 11), window.To);
    }

    [Fact]
    public void A_leap_day_run_ends_on_the_last_day_of_february_last_year()
    {
        var window = StaffLastYearWindow.For(new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 29));
        Assert.Equal(new DateOnly(2027, 2, 1), window.From);
        Assert.Equal(new DateOnly(2027, 2, 28), window.To);
    }

    [Fact]
    public void A_range_still_compares_with_the_same_range_last_year()
    {
        var window = StaffLastYearWindow.For(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 11));
        Assert.False(window.IsMonthToDate);
        Assert.Equal(new DateOnly(2025, 10, 1), window.From);
        Assert.Equal(new DateOnly(2025, 10, 11), window.To);
        Assert.Equal(string.Empty, StaffLastYearWindow.Note(window));
    }

    [Fact]
    public void The_single_day_note_says_what_LY_means()
    {
        var note = StaffLastYearWindow.Note(StaffLastYearWindow.For(new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 11)));
        Assert.Contains("month to date", note);
        Assert.Contains("01 Oct 2025 to 11 Oct 2025", note);
        Assert.Contains("not the same day", note);
    }
}
