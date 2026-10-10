using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// Pins the Help Centre text written for 1.9.7 to 1.10.0 (svcui/help). Where the code holds the number or label the
/// help quotes (Service freshness days, age bands, stage limits, Today card names), the test reads it from the code, so
/// a rule change that forgets the help fails here.
/// </summary>
public sealed class HelpReleaseTextTests
{
    private static string Overview(string id) => HelpCentreRegistry.Find(id)!.Overview;

    [Fact]
    public void Today_help_describes_the_whats_missing_panel_and_its_buttons()
    {
        var text = Overview("dashboard");
        Assert.Contains("\"What's missing\"", text, StringComparison.Ordinal);
        foreach (var expected in new[] { "R025", "R022", "R011", "R030", "Service Centre raw pack", "walk-ins", "opening cash", "expenses", "cash deposit",
                     "staff targets", "Import", "Enter walk-ins", "Enter cash", "Set monthly target", "Set staff targets", "hides when nothing is missing" })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(HelpCentreRegistry.Search("what's missing"), topic => topic.Id == "dashboard");
    }

    [Fact]
    public void Troubleshooting_help_explains_ref_codes_and_the_startup_causes()
    {
        var text = Overview("troubleshooting");
        foreach (var expected in new[] { "\"Ref:\"", "support package", "Settings → Database → Support package", "could not be reached", "took too long",
                     "refused the Windows login", "Settings → Users", "could not be opened", "Test connection", "run the latest ETP setup" })
            Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(HelpCentreRegistry.Search("ref"), topic => topic.Id == "troubleshooting");
    }

    [Fact]
    public void Report_help_uses_the_new_status_wording_sales_label_and_exports_folder()
    {
        var sales = Overview("sales-reports");
        foreach (var expected in new[] { "Passed, Blocked or Failed", "\"Sales incl. GST\"", "\"Unmapped: <brand>\"", @"Documents\ETP Reporting Engine\Exports", "Open export folder" })
            Assert.Contains(expected, sales, StringComparison.Ordinal);
        Assert.DoesNotContain("Net Sales", sales, StringComparison.Ordinal);

        var dsr = Overview("daily-sales-report");
        Assert.Contains("Summary shows a few key figures and one chart", dsr, StringComparison.Ordinal);
        Assert.Contains("FTD and MTD are blank, not zero", dsr, StringComparison.Ordinal);
    }

    [Fact]
    public void Stock_help_describes_the_latest_snapshot_fallback()
    {
        var text = Overview("stock-reports");
        Assert.Contains("\"Snapshot of <date> (latest on or before <date>)\"", text, StringComparison.Ordinal);
        Assert.Contains("Stock Variance, the daily pack and Enter stock by brand still need the same day's snapshot", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("staff-cro")]
    [InlineData("administration")]
    public void Target_help_says_owner_only_and_describes_copy_from_previous_month(string topicId)
    {
        var text = Overview(topicId);
        Assert.Contains("Owner-only", text, StringComparison.Ordinal);
        Assert.Contains("Copy from previous month", text, StringComparison.Ordinal);
        Assert.Contains("Save copied targets", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Store Manager can maintain staff targets", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accounting_help_names_the_tally_cost_centre_field()
    {
        Assert.Contains("Settings → Integrations → Tally companies as STORE=Name", Overview("accounting"), StringComparison.Ordinal);
    }

    [Fact]
    public void Service_help_freshness_days_match_the_freshness_rule()
    {
        var text = Overview("service-centre");
        Assert.Contains($"amber after {ServiceFreshness.AmberAfterDays} days and red after {ServiceFreshness.RedAfterDays}", text, StringComparison.Ordinal);
        Assert.Contains($"amber after {ServiceFreshness.MonthlyAmberAfterDays} days and red after {ServiceFreshness.MonthlyRedAfterDays}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_help_age_bands_and_stage_limits_match_the_board_rules()
    {
        var text = Overview("service-centre");
        Assert.Contains(string.Join(", ", ServiceAgeing.Bands) + " days, or Over 15 days", text, StringComparison.Ordinal);
        Assert.Contains($"bench {ServiceAgeing.StageLimit(ServiceStages.OnBench)}, indent {ServiceAgeing.StageLimit(ServiceStages.IndentRaised)}, " +
            $"SRN out {ServiceAgeing.StageLimit(ServiceStages.SrnOut)}, in transit {ServiceAgeing.StageLimit(ServiceStages.InTransitBack)}, " +
            $"ready {ServiceAgeing.StageLimit(ServiceStages.ReadyForDelivery)} days", text, StringComparison.Ordinal);
        foreach (var filter in new[] { "brand", "guarantee", "Booking or Quick Billing", "overdue only" })
            Assert.Contains(filter, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Service_help_names_every_today_card_and_the_closed_srn_rule()
    {
        var text = Overview("service-centre");
        foreach (var label in new[] { ServiceTodayView.BookedLabel, ServiceTodayView.DeliveredLabel, ServiceTodayView.OnBenchLabel, ServiceTodayView.ReadyLabel,
                     ServiceTodayView.CollectionLabel, ServiceTodayView.Over15Label, ServiceTodayView.ClaimsLabel })
            Assert.Contains(label, text, StringComparison.Ordinal);
        Assert.Contains("DC issued when the SRN status says a DC was created", text, StringComparison.Ordinal);
        Assert.Contains("not the calendar date", text, StringComparison.Ordinal);
        Assert.Contains("raised only", text, StringComparison.Ordinal);
    }
}
