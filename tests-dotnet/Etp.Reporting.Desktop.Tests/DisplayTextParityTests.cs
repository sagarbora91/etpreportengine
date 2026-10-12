using Etp.Reporting.Application.Formatting;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>The Application project cannot see the Reporting project, so its date text is a twin of <see cref="IndianFormat"/>; they must never drift.</summary>
public sealed class DisplayTextParityTests
{
    [Fact]
    public void Application_dates_read_exactly_like_the_one_formatter()
    {
        for (var month = 1; month <= 12; month++)
        {
            var date = new DateOnly(2026, month, 7);
            Assert.Equal(IndianFormat.Date(date), DisplayText.Date(date));
            Assert.Equal(IndianFormat.Month(date), DisplayText.Month(date));
        }
        Assert.Equal("07 Sept 2026", DisplayText.Date(new DateOnly(2026, 9, 7)));
    }

    [Fact]
    public void The_Desktop_presentation_culture_is_the_one_formatter_culture() =>
        Assert.Same(IndianFormat.Culture, PresentationCulture.Indian);
}
