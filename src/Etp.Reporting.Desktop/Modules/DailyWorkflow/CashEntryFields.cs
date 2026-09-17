namespace Etp.Reporting.Desktop.Modules.DailyWorkflow;

/// <summary>
/// The cash-book figures staff type in by hand. They were reachable only by opening a
/// generic field dropdown, so Expenses in particular looked absent from the product
/// even though the Cash Book depends on it and reports itself incomplete without it.
/// These are surfaced as labelled fields on the Cash tab instead.
/// </summary>
public static class CashEntryFields
{
    /// <summary>
    /// Ordered to follow the cash-book formula, so the row reads the way the balance
    /// is calculated: opening + retail cash + service cash - expenses - deposit
    /// + adjustment = calculated closing, then the counted closing beside it.
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered =
    [
        "OPENING_CASH",
        "SERVICE_CASH",
        "SERVICE_CARD",
        "SERVICE_UPI",
        "EXPENSES",
        "CASH_DEPOSIT",
        "CASH_ADJUSTMENT",
        "CLOSING_CASH_COUNTED"
    ];

    /// <summary>
    /// Picks the cash fields out of whatever the database offers, in formula order.
    /// A field the database does not define, or has deactivated, is simply absent:
    /// the view must never invent an input the engine will not accept.
    /// </summary>
    public static IReadOnlyList<T> Prominent<T>(IReadOnlyList<T>? available, Func<T, string> fieldCode)
    {
        ArgumentNullException.ThrowIfNull(fieldCode);
        if (available is null || available.Count == 0) return [];
        var result = new List<T>(Ordered.Count);
        foreach (var wanted in Ordered)
        {
            foreach (var candidate in available)
            {
                if (!string.Equals(fieldCode(candidate), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(candidate);
                break;
            }
        }
        return result;
    }
}
