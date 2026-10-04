namespace Etp.Reporting.Application.Service;

/// <summary>
/// One S004 tender amount of the Service centre for one billing date (dbo.v_service_s004_daily, winning reading).
/// <c>Tender</c> is CASH, CARD, UPI, CHEQUE, RTGS or ADVANCE.
/// </summary>
public sealed record ServiceS004TenderAmount(DateOnly BusinessDate, string Tender, decimal? Amount);

/// <summary>
/// One manual Service entry (a SERVICE_* field of dbo.manual_operational_inputs) for one shop and date, as the read view
/// dbo.v_service_manual_money gives it. <c>IsServiceMoneyShop</c> is true for the shop that enters the Service centre's
/// money (decision 16, Q1); the view decides it from the store catalogue so no store code is written in C#.
/// <c>StoreName</c> is the catalogue name, used in the note of an entry that is not matched.
/// </summary>
public sealed record ServiceManualMoneyEntry(
    DateOnly BusinessDate,
    string StoreCode,
    string? StoreName,
    string FieldCode,
    decimal Amount,
    bool IsServiceMoneyShop);

/// <summary>
/// A manual Service entry made at another shop. It is listed beside the money check and never added to it
/// (decision 16, Q1); its note names the shop, for example "Service entry at Helios: not matched to" the Service centre.
/// </summary>
public sealed record ServiceUnmatchedMoneyEntry(
    DateOnly BusinessDate,
    string StoreCode,
    string FieldCode,
    decimal Amount,
    string Note);

/// <summary>
/// The Service money check rules of decision 16 (4 Oct 2026, Sagar), pure and without I/O:
/// <list type="bullet">
/// <item>Q1: all of the Service centre's cash, card and UPI money is entered at one shop, the Titan World shop.
/// Only that shop's SERVICE_CASH, SERVICE_CARD and SERVICE_UPI entries are compared; an entry at any other shop is
/// listed separately (<see cref="Unmatched"/>) and never added in.</item>
/// <item>Q2: S004 Cash, Card and UPI are compared with those entries by billing date, per tender. A difference is shown
/// (S004 minus manual) and nothing is corrected on either side.</item>
/// <item>Q3: SERVICE_WDC stays out of the tender comparison (it may later be checked against WDC CLAIM, S039).</item>
/// <item>Q4: the Service centre takes no job advances, so no advance is deducted or added; an S004 ADVANCE, CHEQUE or
/// RTGS amount that is not zero is still shown, with no manual side, so it cannot pass unseen.</item>
/// </list>
/// The repository (lane L4, SqlServerServiceReportQuery) reads the rows and hands them to <see cref="Compare"/> and
/// <see cref="Unmatched"/>; the screen (lane L5) shows both.
/// </summary>
public static class ServiceMoneyCheck
{
    /// <summary>The Service centre whose S004 money is checked.</summary>
    public const string ServiceCentreStoreCode = "AW330";

    /// <summary>The manual fields compared with S004, and the S004 tender each one is compared with.</summary>
    public static IReadOnlyDictionary<string, string> ComparedFields { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SERVICE_CASH"] = "CASH",
        ["SERVICE_CARD"] = "CARD",
        ["SERVICE_UPI"] = "UPI",
    };

    /// <summary>SERVICE_WDC is a manual Service field that the tender comparison leaves out (Q3).</summary>
    public const string ExcludedField = "SERVICE_WDC";

    /// <summary>The compared tenders, in screen order.</summary>
    public static IReadOnlyList<string> ComparedTenders { get; } = ["CASH", "CARD", "UPI"];

    /// <summary>
    /// One row per billing date and compared tender (CASH, CARD, UPI) that S004 or the Service-money shop's entries hold,
    /// plus any other S004 tender (CHEQUE, RTGS, ADVANCE) with an amount that is not zero. <c>Difference</c> = S004 -
    /// manual, only when both sides are present (a missing side is never read as zero). <c>ManualStores</c> names the
    /// shop whose entry was compared. Entries at other shops and SERVICE_WDC never reach a row.
    /// </summary>
    public static IReadOnlyList<ServiceMoneyDay> Compare(IEnumerable<ServiceS004TenderAmount> s004, IEnumerable<ServiceManualMoneyEntry> manual)
    {
        ArgumentNullException.ThrowIfNull(s004);
        ArgumentNullException.ThrowIfNull(manual);
        var s004ByKey = s004
            .Select(row => row with { Tender = row.Tender.Trim().ToUpperInvariant() })
            .Where(row => ComparedTenders.Contains(row.Tender) || row.Amount is { } amount && amount != 0m)
            .GroupBy(row => (row.BusinessDate, row.Tender))
            .ToDictionary(group => group.Key, group => group.Any(row => row.Amount.HasValue) ? group.Sum(row => row.Amount ?? 0m) : (decimal?)null);
        var manualByKey = manual
            .Where(entry => entry.IsServiceMoneyShop && ComparedFields.ContainsKey(entry.FieldCode.Trim()))
            .GroupBy(entry => (entry.BusinessDate, Tender: ComparedFields[entry.FieldCode.Trim()]))
            .ToDictionary(group => group.Key, group => (Amount: group.Sum(entry => entry.Amount),
                Stores: (IReadOnlyList<string>)[.. group.Select(entry => entry.StoreCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)]));
        return [.. s004ByKey.Keys.Union(manualByKey.Keys)
            .OrderBy(key => key.BusinessDate).ThenBy(key => TenderRank(key.Tender)).ThenBy(key => key.Tender, StringComparer.Ordinal)
            .Select(key =>
            {
                var amount = s004ByKey.TryGetValue(key, out var value) ? value : null;
                var hasManual = manualByKey.TryGetValue(key, out var entered);
                decimal? manualAmount = hasManual ? entered.Amount : null;
                return new ServiceMoneyDay(key.BusinessDate, key.Tender, amount, manualAmount,
                    amount.HasValue && manualAmount.HasValue ? amount.Value - manualAmount.Value : null,
                    hasManual ? entered.Stores : []);
            })];
    }

    /// <summary>
    /// Every SERVICE_* entry made at a shop other than the Service-money shop (SERVICE_WDC included: it is still a Service
    /// entry at the wrong shop), by date, shop and field. Listed separately, never added to the comparison (Q1).
    /// </summary>
    public static IReadOnlyList<ServiceUnmatchedMoneyEntry> Unmatched(IEnumerable<ServiceManualMoneyEntry> manual)
    {
        ArgumentNullException.ThrowIfNull(manual);
        return [.. manual
            .Where(entry => !entry.IsServiceMoneyShop && entry.FieldCode.Trim().StartsWith("SERVICE_", StringComparison.OrdinalIgnoreCase))
            .GroupBy(entry => (entry.BusinessDate, Store: entry.StoreCode.Trim().ToUpperInvariant(), Field: entry.FieldCode.Trim().ToUpperInvariant()))
            .OrderBy(group => group.Key.BusinessDate).ThenBy(group => group.Key.Store, StringComparer.Ordinal)
            .ThenBy(group => FieldRank(group.Key.Field)).ThenBy(group => group.Key.Field, StringComparer.Ordinal)
            .Select(group => new ServiceUnmatchedMoneyEntry(group.Key.BusinessDate, group.Key.Store, group.Key.Field,
                group.Sum(entry => entry.Amount), UnmatchedNote(group.Select(entry => entry.StoreName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)), group.Key.Store)))];
    }

    /// <summary>"Service entry at {shop}: not matched to" the Service centre code. The shop is its catalogue name, or its code when it has none.</summary>
    public static string UnmatchedNote(string? storeName, string storeCode) =>
        $"Service entry at {(string.IsNullOrWhiteSpace(storeName) ? storeCode.Trim() : storeName.Trim())}: not matched to {ServiceCentreStoreCode}.";

    private static int TenderRank(string tender)
    {
        for (var index = 0; index < ComparedTenders.Count; index++) if (ComparedTenders[index] == tender) return index;
        return ComparedTenders.Count;
    }

    private static int FieldRank(string field) => field switch
    {
        "SERVICE_CASH" => 0,
        "SERVICE_CARD" => 1,
        "SERVICE_UPI" => 2,
        ExcludedField => 3,
        _ => 4,
    };
}
