using Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// 1.12 lane D2: the "Collections: expected vs evidence" wording and arithmetic (CollectionsStatementBuilder), the deposit rule
/// R-DEP-1 and the setup rules. Pure Application code, tested here because this project already references Application.
/// </summary>
public sealed class CollectionsStatementBuilderTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 1);
    private static readonly DateOnly Day2 = new(2026, 8, 2);

    [Fact]
    public void Cash_compares_the_deposit_sent_with_the_bank_credit_and_shows_the_tenders_for_information()
    {
        var expected = new[]
        {
            new ExpectedCollection("COLA", Day1, "Cash", null, null, "CASH_DEPOSIT", 15000m),
            new ExpectedCollection("COLA", Day2, "Cash", null, null, "CASH_DEPOSIT", 9000m),
            new ExpectedCollection("COLB", Day1, "Cash", null, null, "CASH_DEPOSIT", 4000m)
        };
        var deposits = new[]
        {
            new CashDepositStatus("COLA", Day1, 12500m, 12500m, Day2, "DEP0001"),
            new CashDepositStatus("COLA", Day2, 8000m, null, null, null),
            new CashDepositStatus("COLB", Day2, 3000m, 2900m, Day2.AddDays(1), "X"),
            new CashDepositStatus("COLB", new DateOnly(2026, 8, 3), null, 100m, new DateOnly(2026, 8, 4), "Y")
        };

        var lines = CollectionsStatementBuilder.Build(expected, [], deposits, new DateOnly(2026, 8, 6));

        var credited = Assert.Single(lines, line => line is { StoreCode: "COLA", BusinessDate.Day: 1 });
        Assert.Equal(CollectionsLineState.DepositCredited, credited.State);
        Assert.Equal("Bank credited on 02 Aug 2026", credited.ShownAs);
        Assert.Equal(12500m, credited.Expected);
        Assert.Equal(0m, credited.Difference);
        Assert.Equal(15000m, credited.TenderTotal);
        Assert.Equal("DEP0001", credited.BankReference);

        var transit = Assert.Single(lines, line => line is { StoreCode: "COLA", BusinessDate.Day: 2 });
        Assert.Equal(CollectionsLineState.DepositInTransit, transit.State);
        Assert.Equal("Deposit in transit, 4 days", transit.ShownAs);
        Assert.Equal(4, transit.AgeDays);
        Assert.Null(transit.Evidence);

        Assert.Equal(CollectionsLineState.NoDepositRecorded, Assert.Single(lines, line => line is { StoreCode: "COLB", BusinessDate.Day: 1 }).State);
        var different = Assert.Single(lines, line => line is { StoreCode: "COLB", BusinessDate.Day: 2 });
        Assert.Equal(CollectionsLineState.DepositCreditedDifferentAmount, different.State);
        Assert.Equal(-100m, different.Difference);
        Assert.Equal(CollectionsLineState.CreditedNotOnSheet, Assert.Single(lines, line => line is { StoreCode: "COLB", BusinessDate.Day: 3 }).State);
    }

    [Fact]
    public void Upi_and_card_are_totals_only_per_provider_and_other_modes_say_why_no_evidence_is_compared()
    {
        var expected = new[]
        {
            new ExpectedCollection("COLA", Day1, "Card", "CARD_ACQUIRER", null, "PROVIDER_SETTLEMENT", 900m),
            new ExpectedCollection("COLA", Day1, "Service Card", "CARD_ACQUIRER", null, "PROVIDER_SETTLEMENT", 100m),
            new ExpectedCollection("COLA", Day1, "UPI", "PHONEPE", null, "PROVIDER_SETTLEMENT", 300m),
            new ExpectedCollection("COLA", Day1, "UPI", null, null, "PROVIDER_NOT_SET", 40m),
            new ExpectedCollection("COLA", Day1, "Gift Card", "GIFT_CARD", null, "NONE", 50m),
            new ExpectedCollection("COLA", Day1, "Unmapped", null, "SYNTHPAY", "UNMAPPED", 20m)
        };
        var evidence = new[]
        {
            new SettlementEvidence("COLA", Day1, "CARD_ACQUIRER", 800m, 780m, 3),
            new SettlementEvidence("COLA", Day2, "AIRPAY", null, 95m, 1)
        };

        var lines = CollectionsStatementBuilder.Build(expected, evidence, [], Day2);

        var card = Assert.Single(lines, line => line.ProviderKey == "CARD_ACQUIRER");
        Assert.Equal("Card + Service Card", card.Mode);
        Assert.Equal(1000m, card.Expected);
        Assert.Equal(800m, card.Evidence);
        Assert.Equal(-200m, card.Difference);
        Assert.Equal(CollectionsLineState.TotalsOnly, card.State);
        Assert.StartsWith(CollectionsStatementBuilder.TotalsOnlyText, card.ShownAs, StringComparison.Ordinal);

        var upi = Assert.Single(lines, line => line.ProviderKey == "PHONEPE");
        Assert.Equal(CollectionsLineState.NoSettlementLinesYet, upi.State);
        Assert.Contains(CollectionsStatementBuilder.TotalsOnlyText, upi.ShownAs, StringComparison.Ordinal);

        Assert.Equal(CollectionsStatementBuilder.ProviderNotSetUpText, Assert.Single(lines, line => line.State == CollectionsLineState.ProviderNotSetUp).ShownAs);
        Assert.Equal(CollectionsStatementBuilder.NoOutsideEvidenceText, Assert.Single(lines, line => line.Mode == "Gift Card").ShownAs);
        Assert.Contains("SYNTHPAY", Assert.Single(lines, line => line.State == CollectionsLineState.TenderCodeNotMapped).ShownAs, StringComparison.Ordinal);

        var orphan = Assert.Single(lines, line => line.State == CollectionsLineState.SettlementWithoutSales);
        Assert.Equal(95m, orphan.Evidence);
        Assert.Null(orphan.Expected);
    }

    [Fact]
    public void A_viewer_gets_the_totals_without_bank_references()
    {
        var deposits = new[] { new CashDepositStatus("COLA", Day1, 100m, 100m, Day1, "DEP-REF") };

        var line = Assert.Single(CollectionsStatementBuilder.Build([], [], deposits, Day2, withoutBankReferences: true));

        Assert.Null(line.BankReference);
        Assert.Equal(100m, line.Evidence);
    }

    [Fact]
    public void Deposit_rule_proposes_the_same_amount_credited_0_to_3_days_later_on_the_stores_or_a_shared_account()
    {
        var deposit = new DepositInTransit("COLA", Day1, 12500m, 2);
        DepositCandidateLine Line(long id, DateOnly date, decimal amount, string? store) => new(id, 1, "SYN_MAIN", "Synthetic main", store, date, amount, "R" + id, null);

        Assert.True(DepositProposalRule.Fits(deposit, Line(1, Day1, 12500m, "COLA")));
        Assert.True(DepositProposalRule.Fits(deposit, Line(2, Day1.AddDays(3), 12500m, null)));
        Assert.False(DepositProposalRule.Fits(deposit, Line(3, Day1.AddDays(4), 12500m, "COLA")));
        Assert.False(DepositProposalRule.Fits(deposit, Line(4, Day1.AddDays(-1), 12500m, "COLA")));
        Assert.False(DepositProposalRule.Fits(deposit, Line(5, Day1, 12500.01m, "COLA")));
        Assert.False(DepositProposalRule.Fits(deposit, Line(6, Day1, 12500m, "COLB")));

        var single = DepositProposalRule.Propose([deposit], [Line(1, Day2, 12500m, "COLA"), Line(9, Day2, 99m, "COLA")]);
        Assert.False(Assert.Single(single).IsAmbiguous);

        var twoLines = DepositProposalRule.Propose([deposit], [Line(1, Day1, 12500m, "COLA"), Line(2, Day2, 12500m, null)]);
        Assert.Equal(2, twoLines.Count);
        Assert.All(twoLines, proposal => Assert.True(proposal.IsAmbiguous));

        var other = new DepositInTransit("COLA", Day2, 12500m, 1);
        var sharedLine = DepositProposalRule.Propose([deposit, other], [Line(7, Day2, 12500m, "COLA")]);
        Assert.Equal(2, sharedLine.Count);
        Assert.All(sharedLine, proposal => Assert.True(proposal.IsAmbiguous));
    }

    [Fact]
    public void Setup_rules_tidy_keys_and_refuse_full_account_numbers()
    {
        var account = CollectionsSetupRules.Normalise(new BankAccountDraft(null, " syn_main ", "Synthetic main", "Synthetic Bank", "0001", "cola", true, true));
        Assert.Equal("SYN_MAIN", account.AccountKey);
        Assert.Equal("COLA", account.StoreCode);

        Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(account with { AccountLast4 = "12345" }));
        Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(account with { DisplayName = "Current a/c 50100012345678" }));
        Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(account with { AccountKey = "SYN-MAIN" }));
        Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(new ProviderDraft("AIRPAY", "Airpay", "Wallet", "BANK", "UNKNOWN", "UNKNOWN", null, null, true)));
        Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Reason("  "));
        Assert.Equal(10, CollectionsSetupRules.Modes.Count);
    }
}
