using System.Globalization;
using System.Text.Json;

namespace Etp.Reporting.Import.Collections;

public enum StatementLayoutKind { Bank, Provider }

/// <summary>A layout's column map could not be accepted. The message is a plain sentence for the setup screen.</summary>
public sealed class StatementLayoutException(string message) : Exception(message);

/// <summary>
/// An Owner-configured column map for one bank's or provider's export (statement_layouts.mapping_json; master plan 8a task 2).
/// A new bank or provider needs a layout, never code. The JSON is one flat object: column keys name a header of the file,
/// option keys set how values are read. Any other key is refused, so a typo can never be silently ignored.
/// <list type="bullet">
/// <item>BANK columns: value_date, posting_date (at least one), narration, reference, credit, debit, signed_amount, dr_cr_flag,
/// running_balance. Amounts: credit + debit, or signed_amount, or signed_amount + dr_cr_flag (with credit_flag_values).</item>
/// <item>PROVIDER columns: txn_reference, invoice_reference, merchant_id, terminal_id, approval_code, txn_datetime,
/// settlement_date (at least one date), settlement_id, bank_reference, status, line_kind_column, gross, fee, tax_on_fee, net
/// (gross or net), payer_name, payer_vpa.</item>
/// <item>Options: date_format and datetime_format (a .NET format or a list), datetime_utc, decimal_separator,
/// thousands_separator, credit_flag_values, status_failed_values, line_kind_values (file word to line kind),
/// line_kind_default (SALE or UNKNOWN; default UNKNOWN), line_kind_from_sign, fee_source (COLUMN, LINE or NONE).</item>
/// </list>
/// </summary>
public sealed class StatementLayout
{
    public static IReadOnlyList<string> BankColumns { get; } =
        ["value_date", "posting_date", "narration", "reference", "credit", "debit", "signed_amount", "dr_cr_flag", "running_balance"];

    public static IReadOnlyList<string> ProviderColumns { get; } =
        ["txn_reference", "invoice_reference", "merchant_id", "terminal_id", "approval_code", "txn_datetime", "settlement_date",
         "settlement_id", "bank_reference", "status", "line_kind_column", "gross", "fee", "tax_on_fee", "net", "payer_name", "payer_vpa"];

    public static IReadOnlyList<string> BankOptions { get; } =
        ["date_format", "decimal_separator", "thousands_separator", "credit_flag_values"];

    public static IReadOnlyList<string> ProviderOptions { get; } =
        ["date_format", "datetime_format", "datetime_utc", "decimal_separator", "thousands_separator", "status_failed_values",
         "line_kind_values", "line_kind_default", "line_kind_from_sign", "fee_source"];

    /// <summary>The provider line kinds (provider_settlement_lines.line_kind).</summary>
    public static IReadOnlyList<string> LineKinds { get; } =
        ["SALE", "REFUND", "CHARGEBACK", "FEE", "TAX_ON_FEE", "HOLDBACK", "RELEASE", "ADJUSTMENT", "UNKNOWN"];

    private StatementLayout(StatementLayoutKind kind) => Kind = kind;

    public StatementLayoutKind Kind { get; }
    public IReadOnlyDictionary<string, string> Columns { get; private init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> DateFormats { get; private init; } = [];
    public IReadOnlyList<string> DateTimeFormats { get; private init; } = [];
    public bool DateTimeUtc { get; private init; }
    public char DecimalSeparator { get; private init; } = '.';
    public char? ThousandsSeparator { get; private init; } = ',';
    public IReadOnlyList<string> CreditFlagValues { get; private init; } = [];
    public IReadOnlyList<string> StatusFailedValues { get; private init; } = [];
    public IReadOnlyDictionary<string, string> LineKindValues { get; private init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string LineKindDefault { get; private init; } = "UNKNOWN";
    public bool LineKindFromSign { get; private init; }
    public string FeeSource { get; private init; } = "NONE";

    public string? Column(string key) => Columns.TryGetValue(key, out var header) ? header : null;

    public static StatementLayout Parse(string mappingJson, StatementLayoutKind kind)
    {
        if (string.IsNullOrWhiteSpace(mappingJson)) throw new StatementLayoutException("The layout has no column map.");
        JsonDocument document;
        try { document = JsonDocument.Parse(mappingJson); }
        catch (JsonException) { throw new StatementLayoutException("The column map is not valid JSON."); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new StatementLayoutException("The column map must be one JSON object.");
            var columnKeys = kind == StatementLayoutKind.Bank ? BankColumns : ProviderColumns;
            var optionKeys = kind == StatementLayoutKind.Bank ? BankOptions : ProviderOptions;
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (columns.ContainsKey(property.Name) || options.ContainsKey(property.Name))
                    throw new StatementLayoutException($"The key '{property.Name}' appears twice.");
                if (columnKeys.Contains(property.Name, StringComparer.Ordinal))
                {
                    if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                        throw new StatementLayoutException($"'{property.Name}' must name a column of the file.");
                    columns[property.Name] = property.Value.GetString()!.Trim();
                }
                else if (optionKeys.Contains(property.Name, StringComparer.Ordinal)) options[property.Name] = property.Value.Clone();
                else throw new StatementLayoutException($"'{property.Name}' is not a {(kind == StatementLayoutKind.Bank ? "bank" : "provider")} layout key.");
            }

            var decimalSeparator = Separator(options, "decimal_separator") ?? '.';
            var thousands = options.ContainsKey("thousands_separator") ? Separator(options, "thousands_separator") : (decimalSeparator == ',' ? '.' : ',');
            if (thousands == decimalSeparator) throw new StatementLayoutException("The decimal and thousands separators must differ.");
            var layout = new StatementLayout(kind)
            {
                Columns = columns,
                DateFormats = Formats(options, "date_format"),
                DateTimeFormats = Formats(options, "datetime_format"),
                DateTimeUtc = Flag(options, "datetime_utc"),
                DecimalSeparator = decimalSeparator,
                ThousandsSeparator = thousands,
                CreditFlagValues = Words(options, "credit_flag_values"),
                StatusFailedValues = Words(options, "status_failed_values"),
                LineKindValues = KindMap(options),
                LineKindDefault = KindDefault(options),
                LineKindFromSign = Flag(options, "line_kind_from_sign"),
                FeeSource = FeeSourceOf(options, columns)
            };
            layout.Validate();
            return layout;
        }
    }

    private void Validate()
    {
        bool Has(string key) => Columns.ContainsKey(key);
        if (Kind == StatementLayoutKind.Bank)
        {
            if (!Has("value_date") && !Has("posting_date")) throw new StatementLayoutException("Choose the column with the value date or the posting date.");
            var twoColumns = Has("credit") && Has("debit");
            if (Has("credit") != Has("debit")) throw new StatementLayoutException("Choose both the credit (deposit) and the debit (withdrawal) columns.");
            if (twoColumns && Has("signed_amount")) throw new StatementLayoutException("Use either credit and debit columns or one amount column, not both.");
            if (!twoColumns && !Has("signed_amount")) throw new StatementLayoutException("Choose the amount columns: credit and debit, or one amount column.");
            if (Has("dr_cr_flag") && !Has("signed_amount")) throw new StatementLayoutException("A credit/debit marker column needs the amount column.");
            if (Has("dr_cr_flag") && CreditFlagValues.Count == 0) throw new StatementLayoutException("List the marker words that mean a credit (for example CR).");
            return;
        }
        if (!Has("gross") && !Has("net")) throw new StatementLayoutException("Choose the gross amount or the net amount column.");
        if (!Has("txn_datetime") && !Has("settlement_date")) throw new StatementLayoutException("Choose the transaction date column or the settlement date column.");
        if (FeeSource == "COLUMN" && !Has("fee")) throw new StatementLayoutException("Fees read from a column need the fee column.");
        if (FeeSource == "LINE" && !Has("line_kind_column")) throw new StatementLayoutException("Fees read from fee lines need the line type column.");
        if (LineKindValues.Count > 0 && !Has("line_kind_column")) throw new StatementLayoutException("Line type words need the line type column.");
        if (StatusFailedValues.Count > 0 && !Has("status")) throw new StatementLayoutException("Failed-status words need the status column.");
    }

    private static char? Separator(Dictionary<string, JsonElement> options, string key)
    {
        if (!options.TryGetValue(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (text is null || text.Length != 1 || char.IsLetterOrDigit(text[0]))
            throw new StatementLayoutException($"'{key}' must be one character such as \",\" or \".\".");
        return text[0];
    }

    private static IReadOnlyList<string> Formats(Dictionary<string, JsonElement> options, string key)
    {
        var formats = Words(options, key, keepCase: true);
        foreach (var format in formats)
        {
            try { _ = DateTime.MinValue.ToString(format, CultureInfo.InvariantCulture); }
            catch (FormatException) { throw new StatementLayoutException($"'{format}' is not a date format."); }
        }
        return formats;
    }

    private static IReadOnlyList<string> Words(Dictionary<string, JsonElement> options, string key, bool keepCase = false)
    {
        if (!options.TryGetValue(key, out var value)) return [];
        IEnumerable<JsonElement> items = value.ValueKind switch
        {
            JsonValueKind.String => [value],
            JsonValueKind.Array => value.EnumerateArray().ToArray(),
            _ => throw new StatementLayoutException($"'{key}' must be text or a list of text.")
        };
        var words = new List<string>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                throw new StatementLayoutException($"'{key}' must be text or a list of text.");
            var word = item.GetString()!.Trim();
            words.Add(keepCase ? word : word.ToUpperInvariant());
        }
        return words;
    }

    private static bool Flag(Dictionary<string, JsonElement> options, string key)
    {
        if (!options.TryGetValue(key, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new StatementLayoutException($"'{key}' must be true or false.")
        };
    }

    private static Dictionary<string, string> KindMap(Dictionary<string, JsonElement> options)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!options.TryGetValue("line_kind_values", out var value)) return map;
        if (value.ValueKind != JsonValueKind.Object) throw new StatementLayoutException("'line_kind_values' must map file words to line types.");
        foreach (var property in value.EnumerateObject())
        {
            var kind = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()?.Trim().ToUpperInvariant() : null;
            if (kind is null || !LineKinds.Contains(kind, StringComparer.Ordinal))
                throw new StatementLayoutException($"'{property.Name}' must map to one of {string.Join(", ", LineKinds)}.");
            if (string.IsNullOrWhiteSpace(property.Name) || !map.TryAdd(property.Name.Trim(), kind))
                throw new StatementLayoutException("Each file word in 'line_kind_values' must be given once.");
        }
        return map;
    }

    private static string KindDefault(Dictionary<string, JsonElement> options)
    {
        if (!options.TryGetValue("line_kind_default", out var value)) return "UNKNOWN";
        var kind = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim().ToUpperInvariant() : null;
        return kind is "SALE" or "UNKNOWN" ? kind! : throw new StatementLayoutException("'line_kind_default' must be SALE or UNKNOWN.");
    }

    private static string FeeSourceOf(Dictionary<string, JsonElement> options, Dictionary<string, string> columns)
    {
        if (!options.TryGetValue("fee_source", out var value)) return columns.ContainsKey("fee") ? "COLUMN" : "NONE";
        var source = value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim().ToUpperInvariant() : null;
        return source is "COLUMN" or "LINE" or "NONE" ? source! : throw new StatementLayoutException("'fee_source' must be COLUMN, LINE or NONE.");
    }
}
