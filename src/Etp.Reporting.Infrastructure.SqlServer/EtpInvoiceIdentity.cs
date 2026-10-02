using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class EtpInvoiceIdentity
{
    /// <summary>
    /// The financial year of an invoice or stock document's own date, the one year rule for every family (OD-1).
    /// ETP's INVOICEYEAR is only a label: a return dated 1 April carries the year before, and keying by it split
    /// R022 from R025's header (IF-019). <see cref="Etp.Reporting.Import.Staging.InvoiceYearLabels"/> reports the
    /// rows whose label differs.
    /// </summary>
    public static int FinancialYearEnd(DateOnly date) => Etp.Reporting.Import.Documents.DocumentKey.FinancialYearEnd(date);

    public static string ContentHash(IEnumerable<KeyValuePair<string, object?>> values) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            values.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x =>
                $"{x.Key.Length}:{x.Key}:{Format(x.Value).Length}:{Format(x.Value)}")))));

    private static string Format(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal d => decimal.Round(d,4,MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? ""
    };

    public static IReadOnlyDictionary<int, string> LineKeys(
        IEnumerable<Etp.Reporting.Import.Staging.StagedImportRow> rows)
    {
        string[] keys = ["store_code", "invoice_year", "invoice_number", "transaction_date", "product_code",
            "source_transaction_type", "source_quantity", "source_net_amount", "source_net_value", "source_tax_amount",
            "cro_number", "scheme_discount", "user_discount", "pre_discount"];
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            var hash = ContentHash(row.Values.Where(x => keys.Contains(x.Key, StringComparer.Ordinal)));
            occurrences.TryGetValue(hash, out var sequence);
            occurrences[hash] = ++sequence;
            result[row.SourceRowNumber] = $"{hash}:{sequence}";
        }
        return result;
    }
}
