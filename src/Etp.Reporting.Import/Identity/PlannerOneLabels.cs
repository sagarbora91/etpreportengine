using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Identity;

/// <summary>
/// The labels planner 1 gives typed facts, so that either planner recognises the other's facts (spec 7.1, 13.6):
/// a sales line's <c>line_identifier</c> and an enrichment's <c>content_key</c> are both
/// <c>EtpInvoiceIdentity.LineKeys</c>. Planner 2 computes them over the kept rows of a document, so a stale copy can
/// never take <c>:1</c>; over a file without stale copies they equal planner 1's labels row for row.
/// </summary>
public static class PlannerOneLabels
{
    /// <summary>The fields <c>EtpInvoiceIdentity.LineKeys</c> hashes; a family hashes those of them it has.</summary>
    public static IReadOnlyList<string> LineFields { get; } =
    [
        "store_code", "invoice_year", "invoice_number", "transaction_date", "product_code", "source_transaction_type",
        "source_quantity", "source_net_amount", "source_net_value", "source_tax_amount", "cro_number",
        "scheme_discount", "user_discount", "pre_discount"
    ];

    /// <summary>
    /// <c>{hash}:{n}</c> for each row, in the order given: the canonical hash of the <see cref="LineFields"/> the row holds,
    /// and n counting the rows so far with the same hash.
    /// </summary>
    public static IReadOnlyList<string> LineKeys(
        IFactCanonicalizer canonicalizer, EtpReportFamily family, IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(rows);
        // Only the family's own columns, so a landing row's extra columns never take part.
        return LineKeys(canonicalizer, rows.Select(values => (IEnumerable<KeyValuePair<string, object?>>)values
            .Where(field => family.Columns.Any(column => column.CanonicalField == field.Key))));
    }

    /// <summary>
    /// Planner 1's own computation (<c>EtpInvoiceIdentity.LineKeys</c> delegates here): the <see cref="LineFields"/>
    /// each staged row holds, labelled in the order given.
    /// </summary>
    public static IReadOnlyList<string> LineKeys(IFactCanonicalizer canonicalizer, IEnumerable<IEnumerable<KeyValuePair<string, object?>>> rows)
    {
        ArgumentNullException.ThrowIfNull(canonicalizer);
        ArgumentNullException.ThrowIfNull(rows);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new List<string>();
        foreach (var values in rows)
        {
            var hash = canonicalizer.Hash(values.Where(field => LineFields.Contains(field.Key, StringComparer.Ordinal)));
            occurrences[hash] = occurrences.GetValueOrDefault(hash) + 1;
            labels.Add($"{hash}:{occurrences[hash]}");
        }
        return labels;
    }
}
