using Etp.Reporting.Application.Collections;
using Etp.Reporting.Desktop.Modules.Collections;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 wave 2, lane U4: "Collections: expected vs evidence" and "Deposits in transit" per role (fake services,
/// synthetic store COLA).</summary>
public sealed class CollectionsStatementAndDepositsModelTests
{
    private static readonly DateOnly Business = new(2026, 8, 5);

    private static CollectionsStatementModel Statement(FakeStatementQuery query, ShellAccess access, string? store = "COLA") =>
        new(() => query, access, store, Business, () => new DateOnly(2026, 8, 6));

    private static CollectionsStatementLine CreditedCash() =>
        new("COLA", new DateOnly(2026, 8, 1), "Cash", null, 12500m, 12500m, 0m, CollectionsLineState.DepositCredited, "Bank credited on 01 Aug 2026",
            14000m, new DateOnly(2026, 8, 1), "DEP0001");

    private static CollectionsStatementLine Upi() =>
        new("COLA", new DateOnly(2026, 8, 1), "UPI", "PHONEPE", 300m, null, null, CollectionsLineState.NoSettlementLinesYet, "No settlement lines for this day yet. Totals only, not matched yet.");

    private static CollectionsStatementLine Card() =>
        new("COLA", new DateOnly(2026, 8, 1), "Card", "CARD_ACQUIRER", 900m, 800m, -100m, CollectionsLineState.TotalsOnly, "Totals only, not matched yet.");

    private static CollectionsStatementLine NotSetUp() =>
        new("COLA", new DateOnly(2026, 8, 1), "Gift Card", null, 50m, null, null, CollectionsLineState.ProviderNotSetUp, "Provider not set up (D19)");

    // ---- statement ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_statement_reads_the_scope_on_screen_and_shows_money_under_a_header_with_an_em_dash_for_a_missing_figure()
    {
        var query = new FakeStatementQuery { Statement = new([CreditedCash(), Upi(), Card(), NotSetUp()], false) };
        var model = Statement(query, ShellAccess.Owner);

        await model.LoadAsync();

        var request = Assert.Single(query.Requests);
        Assert.Equal(["COLA"], request.StoreCodes);
        Assert.Equal((new DateOnly(2026, 8, 1), Business, new DateOnly(2026, 8, 6)), (request.From, request.To, request.AsOf));
        Assert.Equal("01 Aug 2026 – 05 Aug 2026 · COLA", model.ScopeText);
        var cash = model.Rows[0];
        Assert.Equal(("12,500.00", "12,500.00", "0.00", "14,000.00", "DEP0001"), (cash.Expected, cash.Evidence, cash.Difference, cash.CashTenders, cash.BankReference));
        var upi = model.Rows[1];
        Assert.Equal(("300.00", "—", "—"), (upi.Expected, upi.Evidence, upi.Difference));
        Assert.Equal("-100.00", model.Rows[2].Difference);
    }

    [Fact]
    public async Task Totals_only_is_a_visible_word_and_a_notice_and_the_provider_chip_has_no_decision_code()
    {
        var query = new FakeStatementQuery { Statement = new([Card(), NotSetUp()], false) };
        var model = Statement(query, ShellAccess.Owner);
        await model.LoadAsync();

        Assert.True(model.ShowsTotalsOnlyNotice);
        Assert.Equal("Totals only, not matched yet", CollectionsStatementModel.TotalsOnlyNoticeTitle);
        var card = model.Rows[0];
        Assert.Equal("Totals only, not matched yet", card.StatusWord);
        Assert.Equal(StatusTone.Information, card.Tone);
        var gift = model.Rows[1];
        Assert.Equal("Provider not set up", gift.StatusWord);
        Assert.Equal(StatusTone.Warning, gift.Tone);
        Assert.Equal("Provider not set up", gift.Detail);
        Assert.DoesNotContain("D19", gift.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Viewers_statement_has_no_bank_references_even_when_a_line_carries_one()
    {
        var query = new FakeStatementQuery { Statement = new([CreditedCash()], true) };
        var model = Statement(query, ShellAccess.Viewer);

        await model.LoadAsync();

        Assert.True(model.WithoutBankReferences);
        Assert.Equal(string.Empty, Assert.Single(model.Rows).BankReference);
        Assert.Contains("Bank references are not shown for your role.", model.FooterText, StringComparison.Ordinal);
        Assert.Contains("All amounts in INR", model.FooterText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_mode_filter_keeps_the_lines_of_one_mode_and_a_load_that_fails_shows_one_sentence()
    {
        var query = new FakeStatementQuery { Statement = new([CreditedCash(), Upi(), Card()], false) };
        var model = Statement(query, ShellAccess.StoreManager);
        await model.LoadAsync();

        Assert.Equal(["All modes", "Cash", "UPI", "Card"], model.Modes);
        model.ModeFilter = "UPI";
        Assert.Equal("UPI", Assert.Single(model.Rows).Mode);
        model.ModeFilter = "All modes";
        Assert.Equal(3, model.Rows.Count);

        query.Failure = new UnauthorizedAccessException("Your Windows account has no ETP role. Ask the owner to add you in Settings > Users.");
        await model.LoadAsync();
        Assert.Empty(model.Rows);
        Assert.Equal(StatusTone.Critical, model.StatusTone);
        Assert.Equal("Your Windows account has no ETP role. Ask the owner to add you in Settings > Users.", model.StatusText);
    }

    [Fact]
    public async Task Dates_are_checked_before_the_service_is_asked_and_an_unsigned_user_gets_one_sentence()
    {
        var query = new FakeStatementQuery();
        var model = Statement(query, ShellAccess.Owner, store: null);
        model.To = model.From.AddDays(-1);
        await model.LoadAsync();
        Assert.Equal("The end date is before the start date.", model.StatusText);
        model.To = model.From.AddDays(400);
        await model.LoadAsync();
        Assert.Equal("Choose at most one year.", model.StatusText);
        Assert.Empty(query.Requests);

        var unsigned = Statement(query, ShellAccess.DatabaseSetup);
        await unsigned.LoadAsync();
        Assert.Equal(CollectionsScreens.NotSignedInText, unsigned.StatusText);
        Assert.Empty(query.Requests);

        model.To = Business;
        await model.LoadAsync();
        Assert.Null(Assert.Single(query.Requests).StoreCodes);
        Assert.Equal("Nothing to show for these dates. Import the daily sheets, or bank and settlement files, first.", model.StatusText);
    }

    // ---- deposits in transit -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Owner_sees_deposits_oldest_first_with_the_proposed_bank_line_and_accepts_it_with_a_reason()
    {
        var older = FakeDepositService.Deposit("COLA", 1, 12500m, 4);
        var newer = FakeDepositService.Deposit("COLA", 3, 8000m, 2);
        var service = new FakeDepositService { Transit = [newer, older], Proposals = [FakeDepositService.Proposal(older, 301)] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, "cola");

        await model.LoadAsync();

        Assert.Equal(["01 Aug 2026", "03 Aug 2026"], model.Rows.Select(row => row.Date));
        Assert.Equal(["4 days", "2 days"], model.Rows.Select(row => row.AgeWord));
        Assert.Equal([StatusTone.Warning, StatusTone.Neutral], model.Rows.Select(row => row.AgeTone));
        Assert.Equal(["COLA"], service.StoreScopes[0]);
        var first = model.Rows[0];
        Assert.Equal("One bank line fits", first.ProposalWord);
        Assert.Contains("₹12,500.00 on 02 Aug 2026", first.BankLine, StringComparison.Ordinal);
        Assert.Contains("ref DEP000301", first.BankLine, StringComparison.Ordinal);
        Assert.Equal("SYNTHETIC NARRATION", first.Narration);
        Assert.Equal("No bank line yet", model.Rows[1].ProposalWord);
        Assert.Equal("2 deposits in transit · ₹20,500.00 · oldest first", model.FooterText);

        Assert.False(model.CanAcceptRow(model.Rows[1]));
        Assert.Equal("No bank line fits this deposit yet. Import the bank statement first.", model.AcceptDisabledReason(model.Rows[1]));
        Assert.False(await model.AcceptAsync(first, "   "));
        Assert.Empty(service.Accepted);

        Assert.True(await model.AcceptAsync(first, "Seen on the bank statement"));
        var accepted = Assert.Single(service.Accepted);
        Assert.Equal(("COLA", new DateOnly(2026, 8, 1), 301L, "Seen on the bank statement"), (accepted.StoreCode, accepted.DepositDate, accepted.BankLineId, accepted.Reason));
        Assert.Single(model.Rows);
        Assert.Equal(StatusTone.Success, model.NoticeTone);
        Assert.StartsWith("Marked as credited: COLA, 01 Aug 2026", model.NoticeText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deposit_with_several_fitting_bank_lines_has_a_row_per_line_each_marked_Several_fit()
    {
        var deposit = FakeDepositService.Deposit("COLA", 1, 5000m, 1);
        var service = new FakeDepositService
        {
            Transit = [deposit],
            Proposals = [FakeDepositService.Proposal(deposit, 401, ambiguous: true), FakeDepositService.Proposal(deposit, 402, ambiguous: true)]
        };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, null);

        await model.LoadAsync();

        Assert.Equal(2, model.Rows.Count);
        Assert.All(model.Rows, row => Assert.Equal("Several fit", row.ProposalWord));
        Assert.All(model.Rows, row => Assert.True(row.IsAmbiguous));
        Assert.Equal("1 deposit in transit · ₹5,000.00 · oldest first", model.FooterText);
        Assert.Null(service.StoreScopes[0]);
    }

    [Fact]
    public async Task A_Store_Manager_sees_the_deposits_without_a_proposal_or_narration_and_the_proposals_are_never_asked_for()
    {
        var deposit = FakeDepositService.Deposit("COLA", 1, 12500m, 9);
        var service = new FakeDepositService { Transit = [deposit], Proposals = [FakeDepositService.Proposal(deposit, 301)] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.StoreManager, "COLA");

        await model.LoadAsync();

        Assert.Equal(1, service.TransitCalls);
        Assert.Equal(0, service.ProposalCalls);
        var row = Assert.Single(model.Rows);
        Assert.Null(row.Proposal);
        Assert.Equal("—", row.BankLine);
        Assert.Equal("—", row.Narration);
        Assert.Equal(StatusTone.Critical, row.AgeTone);
        Assert.False(model.CanAccept);
        Assert.Equal(DepositsInTransitModel.OwnerAcceptsText, model.NoticeText);
        Assert.False(await model.AcceptAsync(row, "Seen on the bank statement"));
        Assert.Empty(service.Accepted);
    }

    [Fact]
    public async Task A_Viewer_is_refused_in_one_sentence_and_the_service_is_not_called()
    {
        var service = new FakeDepositService { Transit = [FakeDepositService.Deposit("COLA", 1, 100m, 1)] };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Viewer, null);

        await model.LoadAsync();

        Assert.Equal(CollectionsScreens.DepositsRefusedText, model.StatusText);
        Assert.Empty(model.Rows);
        Assert.Equal(0, service.TransitCalls);
    }

    [Fact]
    public async Task A_stale_accept_shows_the_services_sentence_after_the_list_is_read_again()
    {
        var deposit = FakeDepositService.Deposit("COLA", 1, 12500m, 1);
        var service = new FakeDepositService
        {
            Transit = [deposit], Proposals = [FakeDepositService.Proposal(deposit, 301)],
            AcceptFailure = new InvalidOperationException("This bank line no longer fits the deposit. Refresh the proposals.")
        };
        var model = new DepositsInTransitModel(() => service, ShellAccess.Owner, null);
        await model.LoadAsync();

        Assert.False(await model.AcceptAsync(model.Rows[0], "Seen on the bank statement"));

        Assert.Equal(StatusTone.Critical, model.NoticeTone);
        Assert.Equal("This bank line no longer fits the deposit. Refresh the proposals.", model.NoticeText);
        Assert.Equal(2, service.TransitCalls);
        Assert.Single(model.Rows);
    }

    [Theory]
    [InlineData(0, "Today", StatusTone.Neutral)]
    [InlineData(1, "1 day", StatusTone.Neutral)]
    [InlineData(3, "3 days", StatusTone.Neutral)]
    [InlineData(4, "4 days", StatusTone.Warning)]
    [InlineData(7, "7 days", StatusTone.Warning)]
    [InlineData(8, "8 days", StatusTone.Critical)]
    public void The_age_chip_is_quiet_then_amber_then_red(int age, string word, StatusTone tone)
    {
        var row = new DepositRow(FakeDepositService.Deposit("COLA", 1, 100m, age), null);
        Assert.Equal((word, tone), (row.AgeWord, row.AgeTone));
    }
}
