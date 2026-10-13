using Etp.Reporting.Application.Formatting;

namespace Etp.Reporting.Application.NoSaleDays;

/// <summary>
/// 1.12 lane N1 (decision 52): one store-day marked as a day with no sales. <see cref="Reason"/> is empty when the caller may
/// not read it (the Viewer: the free-text reason is Owner / Store Manager only).
/// </summary>
public sealed record NoSaleDay(string StoreCode, DateOnly BusinessDate, string ReasonCode, string Reason, string MarkedBy, DateTimeOffset MarkedUtc)
{
    /// <summary>
    /// Review M-1: sales or a daily entry other than zero were saved for the store-day after it was marked. The mark no longer
    /// applies (the day needs its normal files and entries, the DSR shows the sales) and should be withdrawn.
    /// </summary>
    public bool NoLongerApplies { get; init; }
}

/// <summary>
/// Mark as no-sale day (frozen contract, 112-W2-PLAN section 4). Decision 55 (Q1 option a): a marked store-day no longer asks
/// for the shop's daily exports or the daily entries; targets stay; the day is finalised and locked like any other.
/// </summary>
public interface INoSaleDayService
{
    /// <summary>The current (not withdrawn) marks for the business date, every store.</summary>
    Task<IReadOnlyList<NoSaleDay>> LoadAsync(DateOnly businessDate, CancellationToken ct = default);

    /// <summary>Marks the store-day. Throws <see cref="NoSaleDayRefusedException"/> with a plain sentence when refused.</summary>
    Task MarkAsync(string storeCode, DateOnly businessDate, string reasonCode, string reason, CancellationToken ct = default);

    /// <summary>Withdraws the store-day's mark. Throws <see cref="NoSaleDayRefusedException"/> with a plain sentence when refused.</summary>
    Task WithdrawAsync(string storeCode, DateOnly businessDate, string reason, CancellationToken ct = default);
}

/// <summary>A refusal the person can act on; <see cref="Exception.Message"/> is the sentence to show (never a SQL text).</summary>
public sealed class NoSaleDayRefusedException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);

/// <summary>One choice of why the day had no sales.</summary>
public sealed record NoSaleReasonChoice(string Code, string Label);

/// <summary>The reason codes, the words for them and the one line every screen and report shows for a marked day.</summary>
public static class NoSaleDayText
{
    public const string ClosedHoliday = "CLOSED_HOLIDAY";
    public const string ClosedOther = "CLOSED_OTHER";
    public const string OpenNoSale = "OPEN_NO_SALE";

    /// <summary>The longest reason the database keeps (no_sale_days.reason nvarchar(500)).</summary>
    public const int ReasonMaxLength = 500;

    public static IReadOnlyList<NoSaleReasonChoice> Reasons { get; } =
    [
        new(ClosedHoliday, "Shop closed for a holiday"),
        new(ClosedOther, "Shop closed for another reason"),
        new(OpenNoSale, "Shop open, but nothing was sold")
    ];

    public static bool IsReasonCode(string? code) => code is not null && Reasons.Any(x => x.Code == code);

    public static string ReasonLabel(string? code) => Reasons.FirstOrDefault(x => x.Code == code)?.Label ?? "No sales";

    /// <summary>The Windows account without its domain or PC part ("SHOPPC\\manager" reads "manager").</summary>
    public static string Who(string? account)
    {
        var value = (account ?? string.Empty).Trim();
        var slash = value.LastIndexOf('\\');
        return slash >= 0 && slash < value.Length - 1 ? value[(slash + 1)..] : value.Length == 0 ? "someone" : value;
    }

    /// <summary>The local calendar date a mark was made.</summary>
    public static DateOnly MarkedOn(DateTimeOffset markedUtc) => DateOnly.FromDateTime(markedUtc.ToLocalTime().DateTime);

    /// <summary>
    /// Review M-1: what Close day, Morning and the Today checklist say when a mark no longer applies because sales or a daily
    /// entry other than zero were saved for the day after it was marked.
    /// </summary>
    public const string NoLongerAppliesSentence =
        "This day is marked as a no-sale day, but sales or a daily entry were saved for it afterwards, so the mark no longer applies: the day needs its normal files and entries. Withdraw the mark.";

    /// <summary>"Shop One: no-sale mark no longer applies (marked by manager on 21 Aug 2026)" - short form for lists.</summary>
    public static string NoLongerAppliesLine(string? markedBy, DateTimeOffset markedUtc) =>
        $"No-sale mark no longer applies: sales or a daily entry were saved after it was made (marked by {Who(markedBy)} on {DisplayText.Date(MarkedOn(markedUtc))}). Withdraw the mark.";

    /// <summary>"No sales (marked by manager on 21 Aug 2026)" - the DSR, its PDF and the pack.</summary>
    public static string NoSalesLine(string? markedBy, DateTimeOffset markedUtc) =>
        $"No sales (marked by {Who(markedBy)} on {DisplayText.Date(MarkedOn(markedUtc))})";

    /// <summary>"Shop One: no-sale day (marked by manager on 21 Aug 2026)" - the Today checklist and the Morning page.</summary>
    public static string ChecklistLine(string shopName, string? markedBy, DateTimeOffset markedUtc) =>
        $"{shopName}: no-sale day (marked by {Who(markedBy)} on {DisplayText.Date(MarkedOn(markedUtc))})";

    /// <summary>The checks the screen makes before it calls the service; null when the input can be sent.</summary>
    public static string? ValidateMark(string? reasonCode, string? reason) =>
        !IsReasonCode(reasonCode) ? "Choose why there were no sales."
        : string.IsNullOrWhiteSpace(reason) ? "Write a short reason."
        : reason.Trim().Length > ReasonMaxLength ? $"Keep the reason under {ReasonMaxLength} characters."
        : null;

    public static string? ValidateWithdraw(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "Write a short reason for withdrawing the no-sale day."
        : reason.Trim().Length > ReasonMaxLength ? $"Keep the reason under {ReasonMaxLength} characters."
        : null;
}
