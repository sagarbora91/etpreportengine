using System.Text.RegularExpressions;

namespace Etp.Reporting.Application.Collections;

/// <summary>Checks and tidies the Collections setup drafts before they reach SQL (whose CHECKs say the same again). Every
/// message is a plain sentence the setup screen can show as it is.</summary>
public static partial class CollectionsSetupRules
{
    /// <summary>The ten D11 cash-book modes, verbatim.</summary>
    public static IReadOnlyList<string> Modes { get; } =
        ["Cash", "Card", "UPI", "CN", "Gift Card", "Bank", "TC", "Service Cash", "Service Card", "Service UPI"];

    public static IReadOnlyList<string> SettlesVia { get; } = ["BANK", "NONE"];
    public static IReadOnlyList<string> StatementKinds { get; } = ["GROSS_WITH_DEDUCTIONS", "NET", "UNKNOWN"];
    public static IReadOnlyList<string> Granularities { get; } = ["TRANSACTION", "SETTLEMENT_SUMMARY", "UNKNOWN"];
    public static IReadOnlyList<string> LayoutKinds { get; } = ["BANK", "PROVIDER"];

    public static string Reason(string? reason)
    {
        var value = reason?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("Give a reason for this change.");
        if (value.Length > 500) throw new ArgumentException("Keep the reason to 500 characters.");
        return value;
    }

    /// <summary>An account, provider or layout key: 1-30 of A-Z, 0-9 and '_', upper-cased. It is the only identifier used in
    /// folder paths, so it is checked strictly.</summary>
    public static string Key(string? value, string label)
    {
        var key = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException($"The {label} must be 1-30 letters, digits or '_' (for example HDFC_MAIN).");
        return key;
    }

    public static BankAccountDraft Normalise(BankAccountDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var last4 = string.IsNullOrWhiteSpace(draft.AccountLast4) ? null : draft.AccountLast4.Trim();
        if (last4 is not null && !Last4Pattern().IsMatch(last4))
            throw new ArgumentException("Enter only the last four digits of the account number.");
        return draft with
        {
            AccountKey = Key(draft.AccountKey, "account key"),
            DisplayName = Name(draft.DisplayName, "account name"),
            BankName = Name(draft.BankName, "bank name"),
            AccountLast4 = last4,
            StoreCode = Store(draft.StoreCode)
        };
    }

    public static ProviderDraft Normalise(ProviderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Modes.Contains(draft.Mode, StringComparer.Ordinal)) throw new ArgumentException("Choose one of the payment modes.");
        if (!SettlesVia.Contains(draft.SettlesVia, StringComparer.Ordinal)) throw new ArgumentException("Choose whether the provider pays into a bank account.");
        if (!StatementKinds.Contains(draft.StatementKind, StringComparer.Ordinal)) throw new ArgumentException("Choose what the provider's file shows (gross, net or not known yet).");
        if (!Granularities.Contains(draft.Granularity, StringComparer.Ordinal)) throw new ArgumentException("Choose whether the file lists each payment or only settlements.");
        if (draft.RoundingTolerance is < 0) throw new ArgumentException("The rounding tolerance cannot be negative.");
        return draft with { ProviderKey = Key(draft.ProviderKey, "provider key"), DisplayName = Name(draft.DisplayName, "provider name") };
    }

    public static MerchantIdDraft Normalise(MerchantIdDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var merchant = draft.MerchantId?.Trim() ?? string.Empty;
        if (merchant.Length is 0 or > 50) throw new ArgumentException("Enter the merchant id (at most 50 characters).");
        var terminal = string.IsNullOrWhiteSpace(draft.TerminalId) ? null : draft.TerminalId.Trim();
        if (terminal is { Length: > 50 }) throw new ArgumentException("Keep the terminal id to 50 characters.");
        return draft with
        {
            ProviderKey = Key(draft.ProviderKey, "provider key"),
            MerchantId = merchant,
            TerminalId = terminal,
            StoreCode = Store(draft.StoreCode) ?? throw new ArgumentException("Choose the store this merchant id belongs to.")
        };
    }

    public static CollectionPolicyDraft Normalise(CollectionPolicyDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!Modes.Contains(draft.Mode, StringComparer.Ordinal)) throw new ArgumentException("Choose one of the payment modes.");
        if (draft.SettlementWindowDays is < 0 or > 90) throw new ArgumentException("The settlement window must be 0 to 90 days.");
        if (draft.OverdueAfterDays is < 0 or > 365) throw new ArgumentException("Overdue after must be 0 to 365 days.");
        if (draft.CashTolerance is < 0) throw new ArgumentException("The cash tolerance cannot be negative.");
        if (draft.EffectiveTo is { } to && to < draft.EffectiveFrom) throw new ArgumentException("The rule cannot end before it starts.");
        return draft with
        {
            StoreCode = Store(draft.StoreCode),
            ProviderKey = string.IsNullOrWhiteSpace(draft.ProviderKey) ? null : Key(draft.ProviderKey, "provider key")
        };
    }

    public static StatementLayoutDraft Normalise(StatementLayoutDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!LayoutKinds.Contains(draft.LayoutKind, StringComparer.Ordinal)) throw new ArgumentException("Choose whether this is a bank or a provider file.");
        var provider = string.IsNullOrWhiteSpace(draft.ProviderKey) ? null : Key(draft.ProviderKey, "provider key");
        if (draft.LayoutKind == "BANK" && (draft.BankAccountId is null || provider is not null))
            throw new ArgumentException("A bank file layout belongs to one bank account.");
        if (draft.LayoutKind == "PROVIDER" && (provider is null || draft.BankAccountId is not null))
            throw new ArgumentException("A provider file layout belongs to one provider.");
        var signature = draft.HeaderSignatureSha256?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SignaturePattern().IsMatch(signature)) throw new ArgumentException("Read the header from a sample file before saving the layout.");
        if (draft.HeaderRow is < 1 or > 1000) throw new ArgumentException("The header row must be 1 to 1000.");
        if (draft.SkipFooterRows is < 0 or > 1000) throw new ArgumentException("The footer lines to skip must be 0 to 1000.");
        var sheet = string.IsNullOrWhiteSpace(draft.SheetName) ? null : draft.SheetName.Trim();
        if (sheet is { Length: > 100 }) throw new ArgumentException("Keep the sheet name to 100 characters.");
        return draft with
        {
            LayoutKey = Key(draft.LayoutKey, "layout key"),
            DisplayName = Name(draft.DisplayName, "layout name"),
            ProviderKey = provider,
            HeaderSignatureSha256 = signature,
            SheetName = sheet
        };
    }

    public static TenderProviderDraft Normalise(TenderProviderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var code = draft.SourceTenderCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length is 0 or > 80) throw new ArgumentException("Choose a payment code.");
        return draft with
        {
            SourceTenderCode = code,
            ProviderKey = string.IsNullOrWhiteSpace(draft.ProviderKey) ? null : Key(draft.ProviderKey, "provider key")
        };
    }

    public static string EvidenceRoot(string? path)
    {
        var value = path?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 400 || !Path.IsPathFullyQualified(value) || value.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Choose a folder on this PC (a full path such as C:\\ProgramData\\EtpReporting\\CollectionsEvidence).");
        return value;
    }

    private static string Name(string? value, string label)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) throw new ArgumentException($"Enter the {label} (at most 100 characters).");
        if (FiveDigits().IsMatch(name)) throw new ArgumentException($"The {label} cannot contain an account number. Use the last four digits field.");
        return name;
    }

    private static string? Store(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    [GeneratedRegex("^[A-Z0-9_]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[0-9]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Last4Pattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SignaturePattern();

    [GeneratedRegex("[0-9]{5}", RegexOptions.CultureInvariant)]
    private static partial Regex FiveDigits();
}
