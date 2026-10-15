namespace Etp.Reporting.Application.Collections;

/// <summary>
/// 1.12.1 lane P2: which of the Owner's settlement rules (dbo.collection_policies, D20) applies to a store, a payment mode and a
/// day. A rule is in force from its start date to its end date (open when blank). The most specific rule wins: a rule for the
/// store beats an all-stores rule, and a rule for the provider beats a rule for the whole mode. Two rules of the same scope may
/// not cover the same day (<see cref="Overlaps"/>; SQL refuses it again with THROW 52000), so the answer is never a guess.
/// No rule means nothing is ever overdue (D20 default). Rules only say when something is late; they never match payments
/// (matching is Phase 8, decision 61).
/// </summary>
public static class CollectionPolicyResolver
{
    /// <summary>The rule in force for the store, mode and provider on <paramref name="date"/>, or null.</summary>
    public static CollectionPolicyRow? Effective(IEnumerable<CollectionPolicyRow> policies, string storeCode, string mode, string? providerKey, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(policies);
        return policies
            .Where(policy => string.Equals(policy.Mode, mode, StringComparison.Ordinal)
                && (policy.StoreCode is null || string.Equals(policy.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase))
                && (policy.ProviderKey is null || string.Equals(policy.ProviderKey, providerKey, StringComparison.Ordinal))
                && InForce(policy, date))
            .OrderByDescending(policy => policy.StoreCode is null ? 0 : 1)
            .ThenByDescending(policy => policy.ProviderKey is null ? 0 : 1)
            .ThenByDescending(policy => policy.EffectiveFrom)
            .ThenByDescending(policy => policy.Id)
            .FirstOrDefault();
    }

    /// <summary>The "Overdue after" days of the Cash rule for a deposit sent on <paramref name="depositDate"/>, or null.</summary>
    public static int? CashOverdueAfterDays(IEnumerable<CollectionPolicyRow> policies, string storeCode, DateOnly depositDate) =>
        Effective(policies, storeCode, "Cash", null, depositDate)?.OverdueAfterDays;

    public static bool InForce(CollectionPolicyRow policy, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.EffectiveFrom <= date && (policy.EffectiveTo is null || policy.EffectiveTo.Value >= date);
    }

    /// <summary>An existing rule of the same scope (mode, store, provider) whose dates meet the draft's: the draft cannot be saved
    /// until the other rule ends before it starts. A rule being edited is not compared with itself.</summary>
    public static CollectionPolicyRow? Overlaps(IEnumerable<CollectionPolicyRow> policies, CollectionPolicyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(draft);
        var draftEnd = draft.EffectiveTo ?? DateOnly.MaxValue;
        return policies.FirstOrDefault(policy => policy.Id != draft.Id
            && string.Equals(policy.Mode, draft.Mode, StringComparison.Ordinal)
            && string.Equals(policy.StoreCode ?? string.Empty, draft.StoreCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.ProviderKey ?? string.Empty, draft.ProviderKey ?? string.Empty, StringComparison.Ordinal)
            && policy.EffectiveFrom <= draftEnd
            && (policy.EffectiveTo ?? DateOnly.MaxValue) >= draft.EffectiveFrom);
    }

    public const string OverlapText = "Another settlement rule for the same mode, store and provider covers some of these dates. Give the other rule an end date first.";
}
