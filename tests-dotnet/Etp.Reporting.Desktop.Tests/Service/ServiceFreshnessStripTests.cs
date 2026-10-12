using System.Threading;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>
/// The freshness strip on the Service frame (design 3.7, decision 25 Q14): the chips come from the contract
/// (ServiceFreshness.Build, lane sql); this class pins the Desktop side: colours, wording and the chip control.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ServiceFreshnessStripTests
{
    private static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static ServiceRefresh Refresh(string code, DateOnly date) => new(code, date, 10, 100 + date.DayNumber, ImportedUtc);

    [Theory]
    [InlineData(0, ServiceFreshnessColour.Fresh)]
    [InlineData(7, ServiceFreshnessColour.Fresh)]
    [InlineData(8, ServiceFreshnessColour.Amber)]
    [InlineData(14, ServiceFreshnessColour.Amber)]
    [InlineData(15, ServiceFreshnessColour.Red)]
    [InlineData(60, ServiceFreshnessColour.Red)]
    public void Amber_after_7_days_red_after_14(int ageDays, ServiceFreshnessColour expected)
    {
        var chips = ServiceFreshness.Build([Refresh("S009", Today.AddDays(-ageDays)), Refresh("S010", Today.AddDays(-ageDays))], Today,
            new Dictionary<string, string> { ["S009"] = "RAW", ["S010"] = "RAW" });
        var chip = chips.Single(chip => chip.Group == "Pending lists");
        Assert.Equal(expected, chip.Colour);
        Assert.Equal($"Pending lists: last export {Today.AddDays(-ageDays):dd MMM yyyy} (raw)", ServiceFreshnessStrip.TextFor(chip));
    }

    [Fact]
    public void Colours_map_to_the_theme_brushes_and_a_spoken_level()
    {
        Assert.Equal(("SurfaceSecondary", "SecondaryText"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessColour.NoData));
        Assert.Equal(("SuccessSoft", "Success"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessColour.Fresh));
        Assert.Equal(("WarningSoft", "Warning"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessColour.Amber));
        Assert.Equal(("CriticalSoft", "Critical"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessColour.Red));
        Assert.Equal("out of date", ServiceFreshnessStrip.DescribeColour(ServiceFreshnessColour.Red));
        Assert.Equal("getting out of date", ServiceFreshnessStrip.DescribeColour(ServiceFreshnessColour.Amber));
    }

    [Fact]
    public void The_chip_control_carries_the_chip_and_names_itself_for_assistive_technology()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var chip = new ServiceFreshnessChip("SRN", ["S011", "S012", "S013"], new DateOnly(2026, 9, 24), "CONSOLIDATED", ServiceFreshnessColour.Red, "last export 24 Sep 2026 (consolidated)");
                var border = ServiceFreshnessStrip.CreateChip(chip);
                Assert.Same(chip, border.Tag);
                Assert.Equal("SRN: last export 24 Sep 2026 (consolidated)", ((TextBlock)border.Child).Text);
                Assert.Equal("SRN: last export 24 Sep 2026 (consolidated), out of date", AutomationProperties.GetName(border));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void A_group_with_no_reading_prints_no_export_yet_and_the_status_views_follow_the_stalest_list()
    {
        var refreshes = new List<ServiceRefresh> { Refresh("S004", new(2026, 10, 3)) };
        foreach (var code in new[] { "S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035" })
            refreshes.Add(Refresh(code, code == "S015" ? new(2026, 9, 29) : new(2026, 10, 3)));  // S015 is a daily raw family (S014 is monthly, R-SQL-14)
        var chips = ServiceFreshness.Build(refreshes, Today);
        Assert.Equal(9, chips.Count);
        Assert.Equal("Tests: no export yet", ServiceFreshnessStrip.TextFor(chips.Single(chip => chip.Group == "Tests")));
        Assert.Equal(ServiceFreshnessColour.NoData, chips.Single(chip => chip.Group == "Tests").Colour);
        var status = chips.Single(chip => chip.Group == "Status views");
        Assert.Equal(new DateOnly(2026, 9, 29), status.LatestSnapshotDate);
        Assert.Equal(ServiceFreshnessColour.Amber, status.Colour);
        Assert.Equal("Status views: last export 29 Sept 2026", ServiceFreshnessStrip.TextFor(status));
    }

    private static ServiceFreshnessChip Chip(string group, ServiceFreshnessColour colour, string text, params string[] never) =>
        new(group, ["S023", "S041"], new DateOnly(2026, 10, 5), "CONSOLIDATED", colour, text, never.Length == 0 ? null : never);

    [Fact]
    public void A_group_with_a_never_exported_family_is_drawn_at_least_amber_but_keeps_its_contract_colour()
    {
        var claims = Chip("Claims", ServiceFreshnessColour.Fresh, "last export 05 Oct 2026 (consolidated) · never exported: S041", "S041");
        Assert.Equal(ServiceFreshnessColour.Fresh, claims.Colour);
        Assert.Equal(ServiceFreshnessColour.Amber, ServiceFreshnessStrip.EffectiveColour(claims));
        Assert.True(ServiceFreshnessStrip.NeedsLook(claims));
        Assert.EndsWith("file not yet exported", ServiceFreshnessStrip.InlineTextFor(claims), StringComparison.Ordinal);
        var red = Chip("SRN", ServiceFreshnessColour.Red, "last export 24 Sep 2026");
        Assert.Equal(ServiceFreshnessColour.Red, ServiceFreshnessStrip.EffectiveColour(red));
        Assert.Equal("SRN: last export 24 Sep 2026 - out of date", ServiceFreshnessStrip.InlineTextFor(red));
    }

    [Fact]
    public void The_summary_chip_counts_fresh_groups_and_takes_the_worst_colour()
    {
        var fresh = Chip("Jobs", ServiceFreshnessColour.Fresh, "last export 05 Oct 2026");
        var amber = Chip("Status views", ServiceFreshnessColour.Amber, "last export 29 Sep 2026");
        Assert.Equal("All 2 groups fresh", ServiceFreshnessStrip.SummaryText([fresh, fresh]));
        Assert.Equal("1 of 2 groups fresh", ServiceFreshnessStrip.SummaryText([fresh, amber]));
        Assert.Equal(ServiceFreshnessColour.Fresh, ServiceFreshnessStrip.SummaryColour([fresh, fresh]));
        Assert.Equal(ServiceFreshnessColour.Amber, ServiceFreshnessStrip.SummaryColour([fresh, amber]));
        Assert.Equal(ServiceFreshnessColour.Red, ServiceFreshnessStrip.SummaryColour([amber, Chip("SRN", ServiceFreshnessColour.Red, "x")]));
        Assert.False(ServiceFreshnessStrip.NeedsLook(fresh));
    }
}
