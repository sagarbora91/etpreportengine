namespace Etp.Reporting.Import.Profiles;

/// <summary>
/// Reads the canonical fields an <see cref="EtpFamilyIdentity"/> names (spec 7.1). A <c>RowKey</c> entry is one field,
/// or <c>COALESCE(a,b,...)</c> for the first of those fields that has a value, as R011 and R010 pair their items by
/// uid, then batch or lot, then EAN (spec 7.3). <see cref="RowKeyText"/> is the one row-key text: the projector stamps
/// it on incoming rows, and whatever rebuilds stored rows (upgrade, stock) must use it too, so the two always pair.
/// </summary>
public static class EtpFamilyIdentityFields
{
    private const string CoalescePrefix = "COALESCE(";

    /// <summary>The fields of one <c>RowKey</c> entry, in the order they are tried.</summary>
    public static IReadOnlyList<string> RowKeyAlternatives(string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        var text = entry.Trim();
        if (!text.StartsWith(CoalescePrefix, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(')'))
            return [text];
        var fields = text[CoalescePrefix.Length..^1].Split(',', StringSplitOptions.TrimEntries);
        if (fields.Length == 0 || fields.Any(string.IsNullOrEmpty))
            throw new FormatException($"RowKey entry '{entry}' is not COALESCE(field,field,...).");
        return fields;
    }

    /// <summary>Every field of the RowKey, COALESCE alternatives included.</summary>
    public static IEnumerable<string> RowKeyFields(this EtpFamilyIdentity identity) =>
        identity.RowKey.SelectMany(RowKeyAlternatives);

    /// <summary>Every canonical field the identity names: document key, row key, snapshot date column and legacy-nullable fields.</summary>
    public static IEnumerable<string> NamedFields(this EtpFamilyIdentity identity) =>
        identity.DocumentKey.Concat(identity.RowKeyFields())
            .Concat(identity.SnapshotDateColumn is { } column ? new[] { column } : Enumerable.Empty<string>())
            .Concat(identity.LegacyNullable)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The row key of one canonical row: per entry, the canonical text of the first field that is not empty, or empty
    /// when none is. <paramref name="facts"/> is <c>CanonicalRow.Facts</c>, which holds the Key and Fact fields.
    /// </summary>
    public static IReadOnlyList<string> RowKeyValues(this EtpFamilyIdentity identity, IReadOnlyDictionary<string, string> facts) =>
        identity.RowKey.Select(entry => RowKeyAlternatives(entry)
            .Select(field => facts.TryGetValue(field, out var value) ? value : "")
            .FirstOrDefault(value => value.Length > 0) ?? "").ToArray();

    /// <summary>
    /// <c>FactRow.RowKey</c>: the <see cref="RowKeyValues"/> joined by <c>|</c> and upper-cased, as the database's
    /// case-insensitive collation compares them. Rule 14 (<c>SNAPSHOT_SHRINK</c>) pairs a stored reading's rows with an
    /// incoming one's by this text, so every reader builds it here.
    /// </summary>
    public static string RowKeyText(this EtpFamilyIdentity identity, IReadOnlyDictionary<string, string> facts) =>
        string.Join('|', identity.RowKeyValues(facts)).ToUpperInvariant();
}
