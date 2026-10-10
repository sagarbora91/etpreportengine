using Etp.Reporting.Application.Service;
using Etp.Reporting.Desktop.Modules.Service;

namespace Etp.Reporting.Desktop.Tests.Service;

/// <summary>The freshness strip (design 3.7, decision 25 Q14): grouping, the oldest-of-ten rule, colours and wording. Pure C#.</summary>
public sealed class ServiceFreshnessStripTests
{
    private static readonly DateTime ImportedUtc = new(2026, 10, 6, 4, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static ServiceFamilyFreshness Family(string code, DateOnly date, string kind = ServiceSourceKinds.Raw) => new(code, date, kind, 10, ImportedUtc);

    [Fact]
    public void The_nine_groups_cover_every_importable_family_once()
    {
        var codes = ServiceFreshnessStrip.Groups.SelectMany(group => group.Families).ToArray();
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.Equal(["Jobs", "Status views", "Pending lists", "SRN", "Money", "Claims", "Parts", "Tests", "Deftran"], ServiceFreshnessStrip.Groups.Select(group => group.Group));
        Assert.Equal(["S002", "S003", "S004", "S006", "S007", "S008", "S009", "S010", "S011", "S012", "S013", "S014", "S015", "S016", "S017", "S018",
            "S019", "S020", "S021", "S022", "S023", "S024", "S025", "S026", "S029", "S030", "S031", "S032", "S033", "S034", "S035", "S036", "S037",
            "S039", "S040", "S041"].Except(["S019", "S020", "S021", "S022"]).Order(), codes.Order());
    }

    [Theory]
    [InlineData(0, ServiceFreshnessLevel.Fresh)]
    [InlineData(7, ServiceFreshnessLevel.Fresh)]
    [InlineData(8, ServiceFreshnessLevel.Amber)]
    [InlineData(14, ServiceFreshnessLevel.Amber)]
    [InlineData(15, ServiceFreshnessLevel.Red)]
    [InlineData(60, ServiceFreshnessLevel.Red)]
    public void Amber_after_7_days_red_after_14(int ageDays, ServiceFreshnessLevel expected)
    {
        Assert.Equal(expected, ServiceFreshnessStrip.LevelFor(ageDays));
        var chip = ServiceFreshnessStrip.Build([Family("S009", Today.AddDays(-ageDays))], Today).Single(chip => chip.Group == "Pending lists");
        Assert.Equal(expected, chip.Level);
        Assert.Equal(ageDays, chip.AgeDays);
    }

    [Fact]
    public void Status_views_show_the_oldest_of_the_ten_lists_other_groups_the_latest()
    {
        var chips = ServiceFreshnessStrip.Build(
        [
            Family("S014", new(2026, 9, 29), ServiceSourceKinds.Consolidated), Family("S018", new(2026, 10, 3)), Family("S032", new(2026, 10, 3)),
            Family("S002", new(2026, 10, 3)), Family("S036", new(2026, 9, 29), ServiceSourceKinds.Consolidated)
        ], Today);
        var status = chips.Single(chip => chip.Group == "Status views");
        Assert.Equal(new DateOnly(2026, 9, 29), status.SnapshotDate);
        Assert.Equal("Status views: last export 29 Sep 2026 (consolidated)", status.Text);
        Assert.Equal(ServiceFreshnessLevel.Amber, status.Level);
        var jobs = chips.Single(chip => chip.Group == "Jobs");
        Assert.Equal(new DateOnly(2026, 10, 3), jobs.SnapshotDate);
        Assert.Equal("Jobs: last export 03 Oct 2026 (raw)", jobs.Text);
        Assert.Equal(ServiceFreshnessLevel.Fresh, jobs.Level);
    }

    [Fact]
    public void A_group_without_a_reading_says_no_export_yet_in_neutral_colours()
    {
        var chips = ServiceFreshnessStrip.Build([Family("S004", new(2026, 10, 3))], Today);
        var tests = chips.Single(chip => chip.Group == "Tests");
        Assert.Equal("Tests: no export yet", tests.Text);
        Assert.Equal(ServiceFreshnessLevel.NoExport, tests.Level);
        Assert.Null(tests.AgeDays);
        Assert.Equal(("SurfaceSecondary", "SecondaryText"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessLevel.NoExport));
        Assert.Equal(("WarningSoft", "Warning"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessLevel.Amber));
        Assert.Equal(("CriticalSoft", "Critical"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessLevel.Red));
        Assert.Equal(("SuccessSoft", "Success"), ServiceFreshnessStrip.BrushesFor(ServiceFreshnessLevel.Fresh));
    }

    [Fact]
    public void An_unknown_source_kind_prints_no_bracket_so_a_pre_1_10_query_still_reads_well()
    {
        var chip = ServiceFreshnessStrip.Build([Family("S003", new(2026, 10, 3), "")], Today).Single(chip => chip.Group == "Money");
        Assert.Equal("Money: last export 03 Oct 2026", chip.Text);
        Assert.Equal("", ServiceFreshnessStrip.DescribeSourceKind(null));
        Assert.Equal("raw", ServiceFreshnessStrip.DescribeSourceKind(" raw "));
    }

    [Fact]
    public void The_contract_default_derives_freshness_from_the_refresh_log_latest_reading_per_family()
    {
        var query = new RefreshLogOnly();
        var rows = query.LoadFreshnessAsync().GetAwaiter().GetResult();
        Assert.Equal(["S004", "S009"], rows.Select(row => row.ReportCode));
        Assert.Equal(new DateOnly(2026, 10, 5), rows.Single(row => row.ReportCode == "S009").SnapshotDate);
        Assert.All(rows, row => Assert.Equal("", row.SourceKind));
    }

    private sealed class RefreshLogOnly : IServiceReportQuery
    {
        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceRefresh>>(
            [
                new("S009", new(2026, 10, 5), 7, 140, ImportedUtc),
                new("S009", new(2026, 9, 28), 6, 101, ImportedUtc.AddDays(-7)),
                new("S004", new(2026, 10, 5), 9, 141, ImportedUtc)
            ]);
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
