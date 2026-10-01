using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Identity;

/// <summary>
/// The one canonicaliser (spec 7.1). Its text rules are today's <c>EtpInvoiceIdentity.ContentHash</c>, unchanged,
/// so a hash over the same fields equals planner 1's. A staged row and the typed landing row that stores it give the
/// same output: a landing <c>date</c> reads back as <see cref="DateTime"/>, a <c>decimal(19,4)</c> with four places,
/// an <c>int</c> instead of a <see cref="long"/> and a NULL as <see cref="DBNull"/>, and each prints as the staged
/// value does. Only the family's own columns are read, so a landing row's <c>etp_row_id</c> or <c>content_key</c>
/// never takes part, and a missing field counts as NULL.
/// </summary>
public sealed class FactCanonicalizer : IFactCanonicalizer
{
    /// <summary>The rules of version 1: today's content-key text, roles from the catalogue (spec 6.8).</summary>
    public const int CurrentContentHashVersion = 1;

    public static FactCanonicalizer Instance { get; } = new();

    public int ContentHashVersion => CurrentContentHashVersion;

    public string Format(object? value) => value switch
    {
        null or DBNull => "",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal number => decimal.Round(number, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? ""
    };

    /// <summary>
    /// The canonical text of one field: <see cref="Format"/>, except that a <c>*state_code</c> number is printed with
    /// two digits (<c>PhaseOneImportPersistence.ContentKeys</c>), so GST state 7 and "07" are one value.
    /// </summary>
    public string FormatField(string field, object? value)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.EndsWith("state_code", StringComparison.Ordinal) &&
            int.TryParse(Convert.ToString(value is DBNull ? null : value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var state)
            ? state.ToString("D2", CultureInfo.InvariantCulture)
            : Format(value);
    }

    public string Hash(IEnumerable<KeyValuePair<string, object?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Sha256(string.Join("\n", values
            .Select(pair => (pair.Key, Text: FormatField(pair.Key, pair.Value)))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key.Length}:{pair.Key}:{pair.Text.Length}:{pair.Text}")));
    }

    public CanonicalRow Canonicalize(EtpReportFamily family, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(values);
        var facts = new List<KeyValuePair<string, object?>>();
        var attributes = new List<KeyValuePair<string, object?>>();
        var descriptive = new List<KeyValuePair<string, object?>>();
        var content = new List<KeyValuePair<string, object?>>();
        foreach (var column in family.Columns)
        {
            var field = new KeyValuePair<string, object?>(column.CanonicalField, values.GetValueOrDefault(column.CanonicalField));
            if (column.Role == ColumnRole.Ignored) continue;
            content.Add(field);
            switch (column.Role)
            {
                case ColumnRole.Key or ColumnRole.Fact: facts.Add(field); break;
                case ColumnRole.Attribute: attributes.Add(field); break;
                case ColumnRole.Descriptive: descriptive.Add(field); break;
            }
        }
        return new(Hash(facts), Hash(attributes), Hash(descriptive), Hash(content), Texts(facts), Texts(attributes));
    }

    /// <summary>
    /// SHA-256 over the row hashes sorted ordinally and joined by newlines; a repeated hash counts each time, and the
    /// order of the rows never matters. No rows give the hash of the empty text.
    /// </summary>
    public string MultisetHash(IEnumerable<string> rowHashes)
    {
        ArgumentNullException.ThrowIfNull(rowHashes);
        return Sha256(string.Join("\n", rowHashes.OrderBy(hash => hash, StringComparer.Ordinal)));
    }

    private IReadOnlyDictionary<string, string> Texts(IEnumerable<KeyValuePair<string, object?>> fields) =>
        fields.ToDictionary(field => field.Key, field => FormatField(field.Key, field.Value), StringComparer.Ordinal);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
