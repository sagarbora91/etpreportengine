using Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// 1.12.1 lane P2: the Owner's settlement rules (D20). Which rule is in force (store beats all stores, provider beats the whole
/// mode, dates inclusive), the overlap refusal, and the rule checks. No rule means nothing is overdue. Synthetic store COLP.
/// </summary>
public sealed class CollectionPolicyResolverTests
{
    private static readonly DateOnly Aug1 = new(2026, 8, 1);

    private static CollectionPolicyRow Rule(int id, string mode, string? store, string? provider, int? overdue, DateOnly from, DateOnly? to = null, int? window = null) =>
        new(id, store, mode, provider, window, overdue, null, from, to);

    [Fact]
    public void Without_a_rule_nothing_is_overdue()
    {
        Assert.Null(CollectionPolicyResolver.CashOverdueAfterDays([], "COLP", Aug1));
        var deposit = new DepositInTransit("COLP", Aug1, 1000m, 40);
        Assert.Null(deposit.OverdueAfterDays);
        Assert.False(deposit.IsOverdue);
    }

    [Fact]
    public void A_store_rule_beats_an_all_stores_rule_and_a_provider_rule_beats_the_whole_mode()
    {
        CollectionPolicyRow[] rules =
        [
            Rule(1, "Cash", null, null, 5, Aug1),
            Rule(2, "Cash", "COLP", null, 2, Aug1),
            Rule(3, "UPI", null, null, 3, Aug1),
            Rule(4, "UPI", null, "PHONEPE", 1, Aug1)
        ];

        Assert.Equal(2, CollectionPolicyResolver.Effective(rules, "COLP", "Cash", null, Aug1)!.Id);
        Assert.Equal(1, CollectionPolicyResolver.Effective(rules, "COLQ", "Cash", null, Aug1)!.Id);
        Assert.Equal(4, CollectionPolicyResolver.Effective(rules, "COLP", "UPI", "PHONEPE", Aug1)!.Id);
        Assert.Equal(3, CollectionPolicyResolver.Effective(rules, "COLP", "UPI", "PAYTM", Aug1)!.Id);
        Assert.Null(CollectionPolicyResolver.Effective(rules, "COLP", "Card", null, Aug1));
        Assert.Equal(2, CollectionPolicyResolver.CashOverdueAfterDays(rules, "colp", Aug1));
    }

    [Fact]
    public void A_rule_is_in_force_from_its_first_to_its_last_day_inclusive()
    {
        var rule = Rule(1, "Cash", "COLP", null, 3, Aug1, new DateOnly(2026, 8, 31));

        Assert.False(CollectionPolicyResolver.InForce(rule, Aug1.AddDays(-1)));
        Assert.True(CollectionPolicyResolver.InForce(rule, Aug1));
        Assert.True(CollectionPolicyResolver.InForce(rule, new DateOnly(2026, 8, 31)));
        Assert.False(CollectionPolicyResolver.InForce(rule, new DateOnly(2026, 9, 1)));
        // The deposit's own date chooses the rule: a deposit of 1 Sep is under the September rule, not August's.
        CollectionPolicyRow[] rules = [rule, Rule(2, "Cash", "COLP", null, 7, new DateOnly(2026, 9, 1))];
        Assert.Equal(3, CollectionPolicyResolver.CashOverdueAfterDays(rules, "COLP", new DateOnly(2026, 8, 15)));
        Assert.Equal(7, CollectionPolicyResolver.CashOverdueAfterDays(rules, "COLP", new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void A_deposit_is_overdue_only_when_older_than_the_rule_allows()
    {
        Assert.False(new DepositInTransit("COLP", Aug1, 1000m, 3, 3).IsOverdue);
        Assert.True(new DepositInTransit("COLP", Aug1, 1000m, 4, 3).IsOverdue);
    }

    [Fact]
    public void Two_rules_of_the_same_scope_may_not_cover_the_same_day_but_an_edit_is_not_compared_with_itself()
    {
        CollectionPolicyRow[] rules = [Rule(1, "Cash", "COLP", null, 3, Aug1, new DateOnly(2026, 8, 31))];
        CollectionPolicyDraft Draft(int? id, DateOnly from, DateOnly? to, string? store = "COLP", string mode = "Cash") => new(id, store, mode, null, null, 5, null, from, to);

        Assert.NotNull(CollectionPolicyResolver.Overlaps(rules, Draft(null, new DateOnly(2026, 8, 31), null)));
        Assert.NotNull(CollectionPolicyResolver.Overlaps(rules, Draft(null, new DateOnly(2026, 7, 1), Aug1)));
        Assert.Null(CollectionPolicyResolver.Overlaps(rules, Draft(null, new DateOnly(2026, 9, 1), null)));
        Assert.Null(CollectionPolicyResolver.Overlaps(rules, Draft(1, Aug1, null)));
        Assert.Null(CollectionPolicyResolver.Overlaps(rules, Draft(null, Aug1, null, store: null)));
        Assert.Null(CollectionPolicyResolver.Overlaps(rules, Draft(null, Aug1, null, mode: "UPI")));
        Assert.StartsWith("Another settlement rule", CollectionPolicyResolver.OverlapText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_must_say_something_and_say_it_consistently()
    {
        static CollectionPolicyDraft Draft(string mode, string? provider, int? window, int? overdue, decimal? tolerance) =>
            new(null, "colp", mode, provider, window, overdue, tolerance, Aug1, null);

        Assert.Contains("changes nothing", Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(Draft("Cash", null, null, null, null))).Message, StringComparison.Ordinal);
        Assert.Equal("Overdue after cannot be shorter than the settlement window.",
            Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(Draft("UPI", null, 3, 2, null))).Message);
        Assert.Contains("no provider", Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(Draft("Cash", "PHONEPE", null, 3, null))).Message, StringComparison.Ordinal);
        Assert.Equal("A cash tolerance belongs to a cash rule only.",
            Assert.Throws<ArgumentException>(() => CollectionsSetupRules.Normalise(Draft("UPI", null, 1, 2, 5m))).Message);

        var ok = CollectionsSetupRules.Normalise(Draft("Cash", " ", null, 3, 10m));
        Assert.Equal(("COLP", (string?)null), (ok.StoreCode, ok.ProviderKey));
        Assert.Equal("PHONEPE", CollectionsSetupRules.Normalise(Draft("UPI", "phonepe", 1, 2, null)).ProviderKey);
    }
}
