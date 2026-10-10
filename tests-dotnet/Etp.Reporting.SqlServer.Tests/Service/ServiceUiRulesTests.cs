using Etp.Reporting.Application.Service;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// Service UI 1.10.0 (decision 25), lane sql: the pure rules of ServiceUiRules.cs - age bands on days since booking, the
/// per-stage overdue limits 7/15/30/15/7 with EDD first (Q4), TAT medians with Booking and Quick Billing apart (Q2), the
/// Pending board and Service Today counts, claims and parts grouping, freshness colours (amber after 7 days, red after 14,
/// Q14). Synthetic job numbers only.
/// </summary>
public sealed class ServiceUiRulesTests
{
    private static readonly DateOnly AsAt = new(2026, 10, 5);

    private static ServiceJobSummary Job(string number, string stage, int? daysInStage = null, int? ageDays = null, DateOnly? edd = null,
        bool quick = false, int? tat = null, int? tatRepair = null, bool claimRaised = false, DateOnly? booked = null, DateOnly? delivered = null,
        DateOnly? rwr = null, DateOnly? stageDate = null, DateOnly? indent = null, string? spare = null)
    {
        var open = !ServiceStages.Closed.Contains(stage) && !(stage is ServiceStages.DcIssued or ServiceStages.RaIssued && claimRaised);
        return new ServiceJobSummary(number, booked, quick ? ServiceJobTypes.QuickBilling : ServiceJobTypes.Booking, quick, null, "Sample Brand", "Model",
            "Watch", "Sample Customer", "Out_of_Warranty", "B2C", edd, stage, stageDate, "AW330", spare, indent, null, null, null, delivered, rwr, null,
            null, "DCSYN", null, "RASYN", claimRaised, null, null, null, null, null, 0, tat, tatRepair, ageDays, daysInStage,
            ServiceAgeing.OverdueBy(stage, edd, daysInStage, AsAt), open, AsAt, AsAt);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, "0-7")]
    [InlineData(7, "0-7")]
    [InlineData(8, "8-15")]
    [InlineData(15, "8-15")]
    [InlineData(16, "16-30")]
    [InlineData(30, "16-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "60+")]
    public void Age_bands_are_0_7_8_15_16_30_31_60_and_over_60(int? days, string? band) => Assert.Equal(band, ServiceAgeing.Band(days));

    [Fact]
    public void The_age_band_counts_days_since_booking_not_days_in_stage()
    {
        var job = Job("JOAW330SYN0101", ServiceStages.InTransitBack, daysInStage: 3, ageDays: 200);
        Assert.Equal("60+", job.AgeBand);
        Assert.Equal("60+", ServiceBoard.ToBoardRow(job).AgeBand);
    }

    [Theory]
    [InlineData(ServiceStages.OnBench, 7)]
    [InlineData(ServiceStages.IndentRaised, 15)]
    [InlineData(ServiceStages.SrnOut, 30)]
    [InlineData(ServiceStages.InTransitBack, 15)]
    [InlineData(ServiceStages.ReadyForDelivery, 7)]
    [InlineData(ServiceStages.Booked, null)]
    [InlineData(ServiceStages.DcIssued, null)]
    [InlineData(ServiceStages.Delivered, null)]
    public void Stage_limits_are_the_Q4_numbers(string stage, int? limit) => Assert.Equal(limit, ServiceAgeing.StageLimit(stage));

    [Fact]
    public void Overdue_uses_the_EDD_when_there_is_one_and_the_stage_limit_otherwise()
    {
        // EDD passed by 3 days: overdue 3, whatever the days in stage.
        Assert.Equal(3, ServiceAgeing.OverdueBy(ServiceStages.OnBench, AsAt.AddDays(-3), 1, AsAt));
        // EDD today or later: not overdue, even after 40 days on the bench.
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, AsAt, 40, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, AsAt.AddDays(2), 40, AsAt));
        // No EDD: days in stage over the limit.
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.OnBench, null, 7, AsAt));
        Assert.Equal(1, ServiceAgeing.OverdueBy(ServiceStages.OnBench, null, 8, AsAt));
        Assert.Equal(5, ServiceAgeing.OverdueBy(ServiceStages.SrnOut, null, 35, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.IndentRaised, null, null, AsAt));
        // A stage without a limit and without an EDD is never overdue; closed stages never are.
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.Booked, null, 400, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.Delivered, AsAt.AddDays(-30), null, AsAt));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.Rwr, null, 90, AsAt));
        // R-SQL-05: a DC/RA job closed by its claim (Q3) is not overdue, whatever its EDD; without the claim it is.
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.DcIssued, AsAt.AddDays(-10), 20, AsAt, claimRaised: true));
        Assert.Null(ServiceAgeing.OverdueBy(ServiceStages.RaIssued, AsAt.AddDays(-10), 20, AsAt, claimRaised: true));
        Assert.Equal(10, ServiceAgeing.OverdueBy(ServiceStages.DcIssued, AsAt.AddDays(-10), 20, AsAt));
        Assert.Equal(10, ServiceAgeing.OverdueBy(ServiceStages.OnBench, AsAt.AddDays(-10), 20, AsAt, claimRaised: true));
    }

    [Fact]
    public void Today_defaults_to_the_latest_day_the_exports_hold_data_for_not_the_export_day()
    {
        // R-SQL-01: the 05 Oct raw pack holds bookings to 04 Oct, a delivery on 03 Oct and S004 to 04 Oct.
        var jobs = new[]
        {
            Job("JOAW330SYN0301", ServiceStages.OnBench, booked: new(2026, 10, 4)),
            Job("JOAW330SYN0302", ServiceStages.Delivered, booked: new(2026, 9, 20), delivered: new(2026, 10, 3)),
            // A delivery date on a job that is not delivered does not count (Today counts delivered jobs only).
            Job("JOAW330SYN0303", ServiceStages.ReadyForDelivery, booked: new(2026, 9, 1), delivered: new(2026, 10, 5)),
        };
        Assert.Equal(new DateOnly(2026, 10, 4), ServiceBoard.LatestDataDate(jobs, new DateOnly(2026, 10, 2), AsAt));
        Assert.Equal(new DateOnly(2026, 10, 4), ServiceBoard.LatestDataDate(jobs, null, AsAt));
        Assert.Equal(new DateOnly(2026, 10, 5), ServiceBoard.LatestDataDate(jobs, new DateOnly(2026, 10, 5), AsAt));
        // Nothing after the as-at date counts; nothing dated falls back to the as-at date.
        Assert.Equal(new DateOnly(2026, 10, 4), ServiceBoard.LatestDataDate(jobs, new DateOnly(2026, 10, 9), AsAt));
        Assert.Equal(AsAt, ServiceBoard.LatestDataDate([], null, AsAt));
        // On that day Today has counts.
        var today = ServiceBoard.Today(new DateOnly(2026, 10, 4), AsAt, jobs, [], [], []);
        Assert.Equal(1, today.BookedToday);
    }

    [Fact]
    public void TAT_median_and_the_summary_keep_Quick_Billing_apart()
    {
        Assert.Null(ServiceTat.Median([]));
        Assert.Equal(3, ServiceTat.Median([5, 1, 3]));
        Assert.Equal(2.5, ServiceTat.Median([4, 1, 3, 2]));
        var jobs = new[]
        {
            Job("JOAW330SYN0201", ServiceStages.Delivered, tat: 2, tatRepair: 1),
            Job("JOAW330SYN0202", ServiceStages.Delivered, tat: 10, tatRepair: 8),
            Job("JOAW330SYN0203", ServiceStages.Delivered, tat: 20, tatRepair: 12),
            Job("JOAW330SYN0204", ServiceStages.Rwr, tat: 30),
            Job("JOAW330SYN0205", ServiceStages.Delivered, quick: true, tat: 0, tatRepair: 0),
            Job("JOAW330SYN0206", ServiceStages.Delivered, quick: true, tat: 0, tatRepair: 0),
            Job("JOAW330SYN0207", ServiceStages.OnBench, daysInStage: 50, ageDays: 50),
        };
        var tat = ServiceTat.Summarise(jobs);
        // R-SQL-06: the Q2 headline is booking to delivered; the RWR job (TAT 30) counts as closed but not in the headline.
        Assert.Equal((6, 3, 2), (tat.ClosedJobs, tat.BookingJobs, tat.QuickBillingJobs));
        Assert.Equal(10, tat.BookingMedianDays);
        Assert.Equal(8, tat.BookingRepairMedianDays);
        Assert.Equal(32 / 3.0, tat.BookingAverageDays!.Value, 6);
        Assert.Equal(0, tat.QuickBillingMedianDays);
        Assert.Equal(1, tat.Over15Days);
    }

    [Fact]
    public void The_board_shows_open_jobs_in_stage_order_then_longest_in_stage_first()
    {
        var jobs = new[]
        {
            Job("JOAW330SYN0301", ServiceStages.SrnOut, daysInStage: 5, ageDays: 40),
            Job("JOAW330SYN0302", ServiceStages.OnBench, daysInStage: 2, ageDays: 2),
            Job("JOAW330SYN0303", ServiceStages.OnBench, daysInStage: 9, ageDays: 9),
            Job("JOAW330SYN0304", ServiceStages.Delivered, tat: 3),
            Job("JOAW330SYN0305", ServiceStages.DcIssued, daysInStage: 100, ageDays: 120, claimRaised: true),
            Job("JOAW330SYN0306", ServiceStages.DcIssued, daysInStage: 100, ageDays: 120),
            Job("JOAW330SYN0307", ServiceStages.InTransitBack, daysInStage: 20, ageDays: 31),
            Job("JOAW330SYN0308", ServiceStages.IndentRaised, daysInStage: 1, ageDays: 3, edd: AsAt.AddDays(-1)),
            Job("JOAW330SYN0309", ServiceStages.Booked, daysInStage: 4, ageDays: 4),
        };
        var board = ServiceBoard.Build(jobs, AsAt);
        Assert.Equal(["JOAW330SYN0309", "JOAW330SYN0303", "JOAW330SYN0302", "JOAW330SYN0308", "JOAW330SYN0301", "JOAW330SYN0307", "JOAW330SYN0306"],
            board.Rows.Select(row => row.JobOrderNumber));
        Assert.Equal(7, board.OpenJobs);
        Assert.Equal(3, board.Overdue);  // bench 9 > 7, indent EDD passed, in transit 20 > 15; DC and booked have no limit
        Assert.Equal(3, board.Over30Days);
        Assert.Equal(1, board.InTransit);
        Assert.Equal(1, board.PartsAwaited);
    }

    [Fact]
    public void Today_counts_bookings_deliveries_the_bench_and_the_money_of_the_date()
    {
        var month = new DateOnly(2026, 10, 1);
        var jobs = new[]
        {
            Job("JOAW330SYN0401", ServiceStages.Delivered, booked: AsAt, delivered: AsAt, quick: true, tat: 0),
            Job("JOAW330SYN0402", ServiceStages.OnBench, booked: AsAt, daysInStage: 0, ageDays: 0),
            Job("JOAW330SYN0403", ServiceStages.IndentRaised, booked: month, daysInStage: 3, ageDays: 20, edd: AsAt.AddDays(-2)),
            Job("JOAW330SYN0404", ServiceStages.Rwr, booked: new DateOnly(2026, 9, 1), rwr: AsAt, tat: 34),
            Job("JOAW330SYN0405", ServiceStages.InTransitBack, booked: new DateOnly(2026, 8, 1), daysInStage: 40, ageDays: 65),
            Job("JOAW330SYN0406", ServiceStages.ReadyForDelivery, booked: new DateOnly(2026, 9, 30), daysInStage: 1, ageDays: 5),
            Job("JOAW330SYN0407", ServiceStages.Delivered, booked: new DateOnly(2026, 9, 2), delivered: month, tat: 29),
        };
        var s004 = new[] { new ServiceS004TenderAmount(AsAt, "CASH", 100m), new ServiceS004TenderAmount(AsAt, "CARD", 50m),
            new ServiceS004TenderAmount(AsAt, "UPI", 25m), new ServiceS004TenderAmount(AsAt, "CHEQUE", 0m),
            new ServiceS004TenderAmount(AsAt.AddDays(-1), "CASH", 999m) };
        var manual = new[] { new ServiceManualMoneyEntry(AsAt, "SYN01", "Synthetic shop", "SERVICE_CASH", 100m, true),
            new ServiceManualMoneyEntry(AsAt, "SYN02", "Other shop", "SERVICE_CASH", 70m, false) };
        var claims = new[] { Claim(ServiceClaimTypes.Wdc, month, "DSAW330SYN01", "JOAW330SYN0901", 10m),
            Claim(ServiceClaimTypes.Wdc, month, "DSAW330SYN01", "JOAW330SYN0902", 5m), Claim(ServiceClaimTypes.Gprc, new DateOnly(2026, 9, 30), "GPAW330SYN01", null, 99m) };
        var today = ServiceBoard.Today(AsAt, AsAt, jobs, s004, manual, claims);
        Assert.Equal((2, 1, 1), (today.BookedToday, today.BookedTodayBooking, today.BookedTodayQuickBilling));
        Assert.Equal(3, today.BookedThisMonth);
        Assert.Equal((1, 2, 1), (today.DeliveredToday, today.DeliveredThisMonth, today.RwrToday));
        Assert.Equal((2, 1, 1), (today.OnBench, today.IndentRaised, today.EddPassed));
        Assert.Equal((1, 1), (today.InTransit, today.ReadyAtCentre));
        Assert.Equal(175m, today.CollectionToday);
        Assert.True(today.ManualEntered);
        Assert.Equal(100m, today.ManualAmount);
        Assert.Equal(2, today.JobsOver15Days);
        Assert.Equal(1, today.ClaimsRaisedThisMonth);
        Assert.Equal(15m, today.ClaimsValueThisMonth);
    }

    [Fact]
    public void Today_without_an_S004_reading_shows_no_collection_rather_than_zero()
    {
        var today = ServiceBoard.Today(AsAt, null, [], [], [], []);
        Assert.Null(today.CollectionToday);
        Assert.False(today.ManualEntered);
        Assert.Null(today.ClaimsValueThisMonth);
    }

    private static ServiceClaimLine Claim(string type, DateOnly date, string? document, string? job, decimal net, string code = "S039") =>
        new(type, date, document, job, "SYN-ITEM", 1m, net, net * 2, "GPRC", code, AsAt);

    [Fact]
    public void Claims_summary_counts_documents_lines_and_jobs_per_month_and_type()
    {
        var lines = new[]
        {
            Claim(ServiceClaimTypes.Wdc, new(2026, 9, 3), "DSAW330SYN01", "JOAW330SYN0501", 10m),
            Claim(ServiceClaimTypes.Wdc, new(2026, 9, 3), "DSAW330SYN01", "JOAW330SYN0502", 20m),
            Claim(ServiceClaimTypes.Wdc, new(2026, 9, 20), "DSAW330SYN02", "JOAW330SYN0501", 5m),
            Claim(ServiceClaimTypes.Gprc, new(2026, 9, 1), "GPAW330SYN01", null, 7m, "S041"),
            Claim(ServiceClaimTypes.Wra, new(2026, 10, 1), "RSAW330SYN01", "JOAW330SYN0503", 1m, "S040"),
        };
        var summary = ServiceClaimRules.Summarise(lines);
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)], summary.Select(row => row.ClaimMonth));
        var wdc = summary.Single(row => row.ClaimType == ServiceClaimTypes.Wdc);
        Assert.Equal((2, 3, 2, 35m, 70m), (wdc.Documents, wdc.Lines, wdc.Jobs, wdc.NetAmountInclTax, wdc.UcpValue));
        Assert.Equal(0, summary.Single(row => row.ClaimType == ServiceClaimTypes.Gprc).Jobs);
        Assert.Equal(ServiceClaimTypes.Gprc, summary[1].ClaimType);
    }

    [Fact]
    public void Not_yet_claimed_lists_DC_and_RA_jobs_without_a_document_oldest_first()
    {
        var jobs = new[]
        {
            Job("JOAW330SYN0601", ServiceStages.DcIssued, stageDate: AsAt.AddDays(-10), daysInStage: 10),
            Job("JOAW330SYN0602", ServiceStages.DcIssued, stageDate: AsAt.AddDays(-50), daysInStage: 50, claimRaised: true),
            Job("JOAW330SYN0603", ServiceStages.RaIssued, stageDate: AsAt.AddDays(-30), daysInStage: 30),
            Job("JOAW330SYN0604", ServiceStages.OnBench, daysInStage: 99),
        };
        var unclaimed = ServiceClaimRules.NotYetClaimed(jobs);
        Assert.Equal(["JOAW330SYN0603", "JOAW330SYN0601"], unclaimed.Select(job => job.JobOrderNumber));
        Assert.Equal((ServiceClaimTypes.Wra, 30, "RASYN"), (unclaimed[0].ClaimType, unclaimed[0].DaysSince, unclaimed[0].DcNumber));
        Assert.Equal((ServiceClaimTypes.Wdc, "DCSYN"), (unclaimed[1].ClaimType, unclaimed[1].DcNumber));
    }

    [Theory]
    [InlineData("2026-10-02", "2026-09-29", false)]
    [InlineData("2026-09-29", "2026-09-29", false)]
    [InlineData("2026-08-05", "2026-09-29", true)]
    [InlineData(null, "2026-09-29", true)]
    [InlineData(null, null, false)]
    public void The_GPRC_gap_warning_compares_the_latest_GPRC_and_DC_RA_readings(string? gprc, string? dcRa, bool warning) =>
        Assert.Equal(warning, ServiceClaimRules.GprcGap(gprc is null ? null : DateOnly.Parse(gprc), dcRa is null ? null : DateOnly.Parse(dcRa)));

    [Fact]
    public void Parts_group_lines_per_invoice_open_first_oldest_first()
    {
        var rows = new[]
        {
            new ServicePartsRow("INVSYN01", new(2026, 9, 1), "SYN-A", 2m, 2m, 100m, "GRNSYN01", new(2026, 9, 8), new(2026, 9, 8), "Received", "CCPT", 7, AsAt),
            new ServicePartsRow("INVSYN01", new(2026, 9, 1), "SYN-B", 1m, 1m, 50m, "GRNSYN01", new(2026, 9, 8), new(2026, 9, 8), "Received", "CCPT", 7, AsAt),
            new ServicePartsRow("INVSYN02", new(2026, 9, 20), "SYN-C", 3m, null, 30m, null, null, null, "Open", "CCPT", 15, AsAt),
            new ServicePartsRow("INVSYN03", new(2026, 8, 1), "SYN-D", 1m, null, 10m, null, null, null, "Open", "CCPT", 65, AsAt),
            new ServicePartsRow("INVSYN04", new(2026, 10, 2), "SYN-E", 1m, 1m, 5m, "GRNSYN04", new(2026, 10, 3), new(2026, 10, 3), "Received", "CCPT", 1, AsAt),
        };
        var git = new[] { new ServiceGitLine("STMSYN1", AsAt.AddDays(-3), "SYN-A", 1m, "CCPT", "AW330", 10m, AsAt),
            new ServiceGitLine("STMSYN2", AsAt.AddDays(-40), "SYN-B", 1m, "CCPT", "AW330", 10m, AsAt) };
        var jobs = new[] { Job("JOAW330SYN0701", ServiceStages.IndentRaised, indent: AsAt.AddDays(-12), spare: "Crown", daysInStage: 12),
            Job("JOAW330SYN0702", ServiceStages.OnBench, daysInStage: 1) };
        var parts = ServicePartsRules.Build(rows, git, new ServiceStockSummary(AsAt, 4, 10m, 1000m), jobs, AsAt);
        Assert.Equal(["INVSYN03", "INVSYN02", "INVSYN01", "INVSYN04"], parts.Invoices.Select(invoice => invoice.InvoiceNumber));
        var first = parts.Invoices.Single(invoice => invoice.InvoiceNumber == "INVSYN01");
        Assert.Equal((2, 3m, 150m, "Received"), (first.Items, first.ShippedQuantity!.Value, first.NetAmount!.Value, first.Status));
        Assert.Null(parts.Invoices.Single(invoice => invoice.InvoiceNumber == "INVSYN02").ReceivedQuantity);
        Assert.Equal((2, 40m, 65), (parts.OpenInvoices, parts.OpenValue, parts.OldestOpenDays!.Value));
        Assert.Equal(1, parts.ReceivedThisMonth);
        Assert.Equal(1, parts.GitLinesLast30Days);
        var waiting = Assert.Single(parts.WaitingJobs);
        Assert.Equal(("JOAW330SYN0701", "Crown", 12), (waiting.JobOrderNumber, waiting.SpareRequired, waiting.DaysWaiting!.Value));
    }

    [Fact]
    public void Freshness_chips_take_the_oldest_family_of_a_group_and_turn_amber_after_7_and_red_after_14_days()
    {
        Assert.Equal(ServiceFreshnessColour.Fresh, ServiceFreshness.Colour(AsAt.AddDays(-7), AsAt));
        Assert.Equal(ServiceFreshnessColour.Amber, ServiceFreshness.Colour(AsAt.AddDays(-8), AsAt));
        Assert.Equal(ServiceFreshnessColour.Amber, ServiceFreshness.Colour(AsAt.AddDays(-14), AsAt));
        Assert.Equal(ServiceFreshnessColour.Red, ServiceFreshness.Colour(AsAt.AddDays(-15), AsAt));
        Assert.Equal(ServiceFreshnessColour.NoData, ServiceFreshness.Colour(null, AsAt));

        var imported = new DateTime(2026, 10, 5, 4, 30, 0, DateTimeKind.Utc);
        ServiceRefresh R(string code, DateOnly date) => new(code, date, 1, 1, imported);
        var refreshes = new List<ServiceRefresh>
        {
            R("S002", AsAt.AddDays(-2)), R("S036", AsAt.AddDays(-2)), R("S037", AsAt.AddDays(-2)), R("S002", AsAt.AddDays(-30)),
            R("S009", AsAt.AddDays(-2)), R("S010", AsAt.AddDays(-9)),
        };
        refreshes.AddRange(new[] { "S014", "S015", "S016", "S017", "S018", "S031", "S032", "S033", "S034", "S035" }.Select(code => R(code, AsAt.AddDays(-20))));
        var chips = ServiceFreshness.Build(refreshes, AsAt, new Dictionary<string, string> { ["S002"] = "RAW", ["S010"] = "RAW", ["S014"] = "CONSOLIDATED" });
        Assert.Equal(ServiceFreshness.Groups.Select(group => group.Group), chips.Select(chip => chip.Group));
        var jobs = chips.Single(chip => chip.Group == "Jobs");
        Assert.Equal((AsAt.AddDays(-2), ServiceFreshnessColour.Fresh), (jobs.LatestSnapshotDate!.Value, jobs.Colour));
        var pending = chips.Single(chip => chip.Group == "Pending lists");
        Assert.Equal((AsAt.AddDays(-9), ServiceFreshnessColour.Amber, "RAW"), (pending.LatestSnapshotDate!.Value, pending.Colour, pending.SourceKind));
        Assert.Equal("last export 26 Sep 2026 (raw)", pending.Text);
        Assert.Equal(ServiceFreshnessColour.Red, chips.Single(chip => chip.Group == "Status views").Colour);
        Assert.Equal(ServiceFreshnessColour.NoData, chips.Single(chip => chip.Group == "Claims").Colour);
        Assert.Equal("no export yet", chips.Single(chip => chip.Group == "Claims").Text);
    }

    [Fact]
    public void Consolidated_only_families_are_judged_against_a_monthly_cadence()
    {
        // R-SQL-14 / Q14: S011-S013 (SRN) come monthly; 20 days old is fresh for them, a daily raw family at 20 days is red.
        Assert.Equal(ServiceFreshnessColour.Fresh, ServiceFreshness.Colour(AsAt.AddDays(-38), AsAt, monthly: true));
        Assert.Equal(ServiceFreshnessColour.Amber, ServiceFreshness.Colour(AsAt.AddDays(-39), AsAt, monthly: true));
        Assert.Equal(ServiceFreshnessColour.Red, ServiceFreshness.Colour(AsAt.AddDays(-46), AsAt, monthly: true));
        var imported = new DateTime(2026, 10, 5, 4, 30, 0, DateTimeKind.Utc);
        ServiceRefresh R(string code, DateOnly date) => new(code, date, 1, 1, imported);
        var chips = ServiceFreshness.Build([R("S011", AsAt.AddDays(-20)), R("S012", AsAt.AddDays(-20)), R("S013", AsAt.AddDays(-20)),
            R("S003", AsAt.AddDays(-2)), R("S004", AsAt.AddDays(-20))], AsAt);
        Assert.Equal(ServiceFreshnessColour.Fresh, chips.Single(chip => chip.Group == "SRN").Colour);
        Assert.Equal(ServiceFreshnessColour.Red, chips.Single(chip => chip.Group == "Money").Colour);
        Assert.Contains("S011", ServiceFreshness.MonthlyFamilies);
        Assert.DoesNotContain("S009", ServiceFreshness.MonthlyFamilies);
    }

    [Fact]
    public async Task The_default_freshness_of_the_contract_comes_from_the_refresh_log()
    {
        IServiceReportQuery query = new RefreshOnlyQuery();
        var chips = await query.LoadFreshnessAsync(AsAt);
        Assert.Equal(9, chips.Count);
        Assert.Equal(ServiceFreshnessColour.Fresh, chips.Single(chip => chip.Group == "Tests").Colour);
        // The 1.10 members a fake leaves out return empty results, never throw (except lane history's job members).
        Assert.Empty((await query.LoadPendingBoardAsync()).Rows);
        Assert.Empty((await query.LoadClaimsAsync(null, null)).Lines);
        Assert.Empty((await query.LoadPartsAsync()).Invoices);
    }

    [Fact]
    public void Stage_order_and_labels_match_lane_history_and_Quick_Billing_is_read_from_the_export()
    {
        Assert.Equal(ServiceJobStages.InOrder, ServiceStages.Order);
        foreach (var stage in ServiceStages.Order)
        {
            Assert.Equal(ServiceJobStages.Label(stage), ServiceStages.Label(stage));
            Assert.Equal(ServiceJobStages.Rank(stage), ServiceStages.Rank(stage));
        }
        Assert.Equal(ServiceStages.Order.Count, ServiceStages.Rank("NOT_A_STAGE"));
        Assert.Equal(ServiceJobTypes.QuickBilling, ServiceJobTypes.Normalise("Quick Billing"));
        Assert.Equal(ServiceJobTypes.Booking, ServiceJobTypes.Normalise(null));
        var header = ServiceJobProjection.Header(Job("JOAW330SYN0801", ServiceStages.OnBench, daysInStage: 9));
        Assert.True(header.IsOverdue);
        Assert.Equal(("Out_of_Warranty", "B2C", "Sample Customer"), (header.Guarantee, header.CustomerType, header.CustomerName));
    }

    private sealed class RefreshOnlyQuery : IServiceReportQuery
    {
        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceRefresh>>([new("S030", AsAt.AddDays(-1), 3, 7, new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc))]);
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceJobRow>>([]);
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServicePendingRow>>([]);
        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceJobEvent>>([]);
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceMoneyDay>>([]);
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceMoneyChange>>([]);
    }
}
