using Etp.Reporting.Application.Service;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// The decision-16 Service money check (lane L10): only the Service-money shop's SERVICE_CASH, SERVICE_CARD and SERVICE_UPI
/// entries are compared with S004 Cash, Card and UPI by billing date and tender; differences are shown, never corrected;
/// entries at other shops are listed separately and never added; SERVICE_WDC is left out; advances are taken as zero.
/// Synthetic store codes only (TITANSHOP, OTHERSHOP): the rule is the flag the read view sets, not a code in C#.
/// </summary>
public sealed class ServiceMoneyCheckTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 25);
    private static readonly DateOnly Day2 = new(2026, 8, 26);

    private static ServiceManualMoneyEntry Shop(DateOnly date, string field, decimal amount) =>
        new(date, "TITANSHOP", "Titan World", field, amount, IsServiceMoneyShop: true);

    private static ServiceManualMoneyEntry Other(DateOnly date, string field, decimal amount, string? name = "Helios") =>
        new(date, "OTHERSHOP", name, field, amount, IsServiceMoneyShop: false);

    private static ServiceS004TenderAmount S004(DateOnly date, string tender, decimal? amount) => new(date, tender, amount);

    [Fact]
    public void A_day_that_agrees_shows_zero_differences_per_tender()
    {
        // The 25 Aug shape (cash, card and UPI on separate bills): S004 equals the shop's entries tender by tender.
        var days = ServiceMoneyCheck.Compare(
            [S004(Day1, "CASH", 700), S004(Day1, "CARD", 1807), S004(Day1, "UPI", 577), S004(Day1, "CHEQUE", 0), S004(Day1, "RTGS", 0), S004(Day1, "ADVANCE", 0)],
            [Shop(Day1, "SERVICE_CASH", 700), Shop(Day1, "SERVICE_CARD", 1807), Shop(Day1, "SERVICE_UPI", 577)]);

        Assert.Equal(["CASH", "CARD", "UPI"], days.Select(day => day.Tender));
        Assert.All(days, day =>
        {
            Assert.Equal(Day1, day.BusinessDate);
            Assert.Equal(0m, day.Difference);
            Assert.Equal(["TITANSHOP"], day.ManualStores);
        });
        Assert.Equal(3084m, days.Sum(day => day.S004Amount));
    }

    [Fact]
    public void A_difference_is_S004_minus_manual_and_neither_side_is_changed()
    {
        var days = ServiceMoneyCheck.Compare([S004(Day1, "CASH", 700), S004(Day1, "CARD", 1807)],
            [Shop(Day1, "SERVICE_CASH", 650), Shop(Day1, "SERVICE_CARD", 1900)]);

        var cash = Assert.Single(days, day => day.Tender == "CASH");
        Assert.Equal((700m, 650m, 50m), (cash.S004Amount, cash.ManualAmount, cash.Difference));
        var card = Assert.Single(days, day => day.Tender == "CARD");
        Assert.Equal((1807m, 1900m, -93m), (card.S004Amount, card.ManualAmount, card.Difference));
    }

    [Fact]
    public void Entries_at_another_shop_are_never_added_to_the_comparison()
    {
        var manual = new[] { Shop(Day1, "SERVICE_CASH", 700), Other(Day1, "SERVICE_CASH", 300), Other(Day1, "SERVICE_CARD", 125) };
        var days = ServiceMoneyCheck.Compare([S004(Day1, "CASH", 700), S004(Day1, "CARD", 0)], manual);

        var cash = Assert.Single(days, day => day.Tender == "CASH");
        Assert.Equal(700m, cash.ManualAmount);
        Assert.Equal(0m, cash.Difference);
        Assert.Equal(["TITANSHOP"], cash.ManualStores);
        var card = Assert.Single(days, day => day.Tender == "CARD");
        Assert.Null(card.ManualAmount);
        Assert.Empty(card.ManualStores);
        Assert.DoesNotContain(days, day => day.ManualStores.Contains("OTHERSHOP"));
    }

    [Fact]
    public void Entries_at_another_shop_are_listed_separately_with_a_plain_note()
    {
        var unmatched = ServiceMoneyCheck.Unmatched(
        [
            Shop(Day1, "SERVICE_CASH", 700), Other(Day2, "SERVICE_UPI", 40), Other(Day1, "SERVICE_WDC", 90),
            Other(Day1, "SERVICE_CASH", 300), Other(Day1, "OPENING_CASH", 5000), Other(Day1, "SERVICE_CARD", 10, name: null),
        ]);

        Assert.Equal(
        [
            (Day1, "OTHERSHOP", "SERVICE_CASH", 300m), (Day1, "OTHERSHOP", "SERVICE_CARD", 10m), (Day1, "OTHERSHOP", "SERVICE_WDC", 90m),
            (Day2, "OTHERSHOP", "SERVICE_UPI", 40m),
        ], unmatched.Select(entry => (entry.BusinessDate, entry.StoreCode, entry.FieldCode, entry.Amount)));
        Assert.Equal("Service entry at Helios: not matched to AW330.", unmatched[0].Note);
        // The catalogue name of any entry of the group is used; the code only when the shop has no name at all.
        Assert.Equal("Service entry at OTHERSHOP: not matched to AW330.", ServiceMoneyCheck.UnmatchedNote(null, "OTHERSHOP"));
        Assert.Empty(ServiceMoneyCheck.Unmatched([Shop(Day1, "SERVICE_CASH", 700), Shop(Day1, "SERVICE_WDC", 90)]));
    }

    [Fact]
    public void SERVICE_WDC_stays_out_of_the_tender_comparison()
    {
        var days = ServiceMoneyCheck.Compare([S004(Day1, "CASH", 700)], [Shop(Day1, "SERVICE_CASH", 700), Shop(Day1, "SERVICE_WDC", 250)]);

        var day = Assert.Single(days);
        Assert.Equal(("CASH", 700m, 0m), (day.Tender, day.ManualAmount, day.Difference));
        Assert.Equal("SERVICE_WDC", ServiceMoneyCheck.ExcludedField);
        Assert.False(ServiceMoneyCheck.ComparedFields.ContainsKey(ServiceMoneyCheck.ExcludedField));
    }

    [Fact]
    public void Advances_are_zero_so_only_a_non_zero_other_tender_is_shown_and_it_has_no_manual_side()
    {
        var days = ServiceMoneyCheck.Compare(
            [S004(Day1, "CASH", 700), S004(Day1, "ADVANCE", 0), S004(Day1, "CHEQUE", null), S004(Day2, "ADVANCE", 200), S004(Day2, "UPI", 0)],
            [Shop(Day1, "SERVICE_CASH", 700), Shop(Day2, "SERVICE_UPI", 0)]);

        Assert.Equal([(Day1, "CASH"), (Day2, "UPI"), (Day2, "ADVANCE")], days.Select(day => (day.BusinessDate, day.Tender)));
        var advance = days[^1];
        Assert.Equal(200m, advance.S004Amount);
        Assert.Null(advance.ManualAmount);
        Assert.Null(advance.Difference);
        // The cash difference is not reduced by any advance.
        Assert.Equal(0m, days[0].Difference);
    }

    [Fact]
    public void A_missing_side_is_shown_empty_and_never_read_as_zero()
    {
        var days = ServiceMoneyCheck.Compare([S004(Day1, "CASH", 700)], [Shop(Day2, "SERVICE_CARD", 400)]);

        Assert.Equal(2, days.Count);
        Assert.Equal((700m, (decimal?)null, (decimal?)null), (days[0].S004Amount, days[0].ManualAmount, days[0].Difference));
        Assert.Equal(((decimal?)null, 400m, (decimal?)null), (days[1].S004Amount, days[1].ManualAmount, days[1].Difference));
        Assert.Equal(["TITANSHOP"], days[1].ManualStores);
    }

    [Fact]
    public void Tender_and_field_spellings_are_tolerated()
    {
        var days = ServiceMoneyCheck.Compare([S004(Day1, " cash ", 100)], [Shop(Day1, "service_cash", 100)]);
        var day = Assert.Single(days);
        Assert.Equal(("CASH", 0m), (day.Tender, day.Difference));
    }

    [Fact]
    public async Task The_contract_lists_no_unmatched_entries_unless_the_query_overrides_it()
    {
        IServiceReportQuery query = new LegacyQuery();
        Assert.Empty(await query.LoadUnmatchedServiceEntriesAsync(Day1, Day2));
    }

    // A query written before decision 16 (as lane L4's and L5's fakes are) still compiles and lists nothing.
    private sealed class LegacyQuery : IServiceReportQuery
    {
        public Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceRefresh>>([]);
        public Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceJobRow>>([]);
        public Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServicePendingRow>>([]);
        public Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceJobEvent>>([]);
        public Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceMoneyDay>>([]);
        public Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ServiceMoneyChange>>([]);
    }
}
