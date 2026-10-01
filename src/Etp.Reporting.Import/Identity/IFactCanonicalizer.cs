using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Identity;

/// <summary>
/// One row in canonical form under its family's column roles (spec 7.1). Hashes are SHA-256, lowercase hex.
/// Descriptive values never leave memory: only <see cref="DescriptiveHash"/> is kept, to partition rows.
/// </summary>
/// <param name="FactRowHash"><c>fact_row_hash</c>: Key and Fact fields.</param>
/// <param name="AttributeHash"><c>attribute_hash</c>: Attribute fields.</param>
/// <param name="DescriptiveHash">Descriptive fields; in memory only, never stored.</param>
/// <param name="ContentHash">Every staged field except Ignored ones: today's <c>content_key</c> before its <c>:n</c>.</param>
/// <param name="Facts">Canonical text of the Key and Fact fields, for diffs, fills and row keys.</param>
/// <param name="Attributes">Canonical text of the Attribute fields.</param>
public sealed record CanonicalRow(
    string FactRowHash,
    string AttributeHash,
    string DescriptiveHash,
    string ContentHash,
    IReadOnlyDictionary<string, string> Facts,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// The one canonicaliser shared by import, re-decide, approval apply and upgrade (spec 7.1). It gives the same
/// output for a staged row and for the typed landing row that stores it; SQL never rebuilds a tuple.
/// Rules (today's <c>EtpInvoiceIdentity.ContentHash</c>, unchanged): keys sorted ordinally, <c>len:key:len:value</c>
/// joined by newlines, decimals rounded to 4 places and printed <c>0.####</c>, dates <c>yyyy-MM-dd</c>, strings
/// trimmed, null empty; <c>*state_code</c> numbers printed with two digits.
/// </summary>
public interface IFactCanonicalizer
{
    /// <summary>Bumped whenever a rule here, the staging type list or a hash-relevant role changes (<c>content_hash_version</c>).</summary>
    int ContentHashVersion { get; }

    /// <summary>The canonical text of one staged or landed value.</summary>
    string Format(object? value);

    /// <summary>SHA-256 over the canonical <c>len:key:len:value</c> list of the given fields.</summary>
    string Hash(IEnumerable<KeyValuePair<string, object?>> values);

    /// <summary>One row, by canonical field name, under the family's roles.</summary>
    CanonicalRow Canonicalize(EtpReportFamily family, IReadOnlyDictionary<string, object?> values);

    /// <summary>SHA-256 over the ordinally sorted row hashes, with multiplicity: <c>fact_sha256</c>, <c>canonical_sha256</c>, <c>content_sha256</c>.</summary>
    string MultisetHash(IEnumerable<string> rowHashes);
}
