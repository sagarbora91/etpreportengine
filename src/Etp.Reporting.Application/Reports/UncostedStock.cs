namespace Etp.Reporting.Application.Reports;

/// <summary>
/// 1.12.1 F2 (decision 69): the number of items in one store and brand row whose stock has at least one snapshot line with a
/// quantity but no cost price. Stock VALUE leaves those lines out (it is not computed any differently); this count says so.
/// </summary>
public sealed record UncostedStockRecord(string StoreCode, string Brand, int Items);

/// <summary>Plain-English wording of the uncosted-stock note, shared by the screen, the Excel/PDF exports and the daily pack.</summary>
public static class UncostedStockNote
{
    /// <summary>"N items have no cost price; stock value leaves them out" (singular for one), or "" when there are none.</summary>
    public static string Title(int items) => items switch
    {
        <= 0 => "",
        1 => "1 item has no cost price; stock value leaves it out",
        _ => $"{items:N0} items have no cost price; stock value leaves them out",
    };

    public static int Total(IEnumerable<UncostedStockRecord> rows) => rows.Sum(x => x.Items);

    /// <summary>"WLMHW 3, HEMW 1": one count per store, stores in code order.</summary>
    public static string PerStore(IEnumerable<UncostedStockRecord> rows) =>
        string.Join(", ", rows.GroupBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key} {x.Sum(y => y.Items):N0}"));

    /// <summary>"WLMHW: CASIO 2, SEIKO 1; HEMW: FOSSIL 1" per store and brand row.</summary>
    public static string PerStoreBrand(IEnumerable<UncostedStockRecord> rows) =>
        string.Join("; ", rows.GroupBy(x => x.StoreCode, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(store => $"{store.Key}: " + string.Join(", ", store.GroupBy(x => x.Brand, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => $"{x.Key} {x.Sum(y => y.Items):N0}"))));

    /// <summary>The one-sentence note for an export or a message: title, then the per-store and per-brand counts. Empty when none.</summary>
    public static string Sentence(IReadOnlyCollection<UncostedStockRecord> rows)
    {
        var total = Total(rows);
        return total <= 0 ? "" : $"{Title(total)} ({PerStoreBrand(rows)}).";
    }

    /// <summary>The note for the daily pack's stock line, where quantity is reconciled and value is not shown.</summary>
    public static string PackSentence(IReadOnlyCollection<UncostedStockRecord> rows)
    {
        var total = Total(rows);
        return total <= 0 ? "" : $"{Title(total)} ({PerStore(rows)}).";
    }
}
